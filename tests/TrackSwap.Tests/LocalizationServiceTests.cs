using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json;
using TrackSwap.Localization;
using TrackSwap.Models;

namespace TrackSwap.Tests;

public sealed class LocalizationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "TrackSwap.Localization.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingAndInvalidTranslationsDisplayKeysInsteadOfChinese()
    {
        Directory.CreateDirectory(_root);
        WritePack("en-US", new Dictionary<string, string>
        {
            ["valid.key"] = "Valid",
            ["invalid.key"] = string.Empty,
            ["stale.key"] = "Stale"
        });
        var service = CreateService();

        service.SetLanguage("en-US");

        Assert.Equal("Valid", service.Translate("valid.key"));
        Assert.Equal("[missing.key]", service.Translate("missing.key"));
        Assert.Equal("[invalid.key]", service.Translate("invalid.key"));
        Assert.Contains(service.Issues, issue => issue.Key == "missing.key" && issue.Kind == LocalizationIssueKind.Missing);
        Assert.Contains(service.Issues, issue => issue.Key == "invalid.key" && issue.Kind == LocalizationIssueKind.Invalid);
        Assert.Contains(service.Issues, issue => issue.Key == "stale.key" && issue.Kind == LocalizationIssueKind.Stale);
    }

    [Fact]
    public void ReloadPicksUpEditedTranslationWithoutRestart()
    {
        Directory.CreateDirectory(_root);
        WritePack("en-US", new Dictionary<string, string>
        {
            ["valid.key"] = "Before"
        });
        var service = CreateService();
        service.SetLanguage("en-US");
        Assert.Equal("Before", service.Translate("valid.key"));

        WritePack("en-US", new Dictionary<string, string>
        {
            ["valid.key"] = "After"
        });
        service.Reload("en-US");

        Assert.Equal("After", service.Translate("valid.key"));
    }

    [Fact]
    public void PacksWithTheSameLocaleCoexistAndAreSelectedByFileName()
    {
        Directory.CreateDirectory(_root);
        WritePack(
            "en-US-machine.json",
            "en-US",
            "English",
            "Machine",
            new Dictionary<string, string> { ["valid.key"] = "Machine" });
        WritePack(
            "en-US-refined.json",
            "en-US",
            "English",
            "Editor",
            new Dictionary<string, string> { ["valid.key"] = "Refined" });
        var service = CreateService();

        LanguageOption[] englishOptions = service.Languages
            .Where(option => string.Equals(option.Locale, "en-US", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(2, englishOptions.Length);
        Assert.Contains(englishOptions, option =>
            option.Label.IndexOf("en-US-machine.json", StringComparison.Ordinal) >= 0);
        Assert.Contains(englishOptions, option =>
            option.Label.IndexOf("en-US-refined.json", StringComparison.Ordinal) >= 0);

        service.SetLanguage("en-US", "en-US-refined.json");
        Assert.Equal("Refined", service.Translate("valid.key"));
        Assert.Equal("en-US-refined.json", service.CurrentPackFileName);
        service.Reload();
        Assert.Equal("Refined", service.Translate("valid.key"));
        Assert.Equal("en-US-refined.json", service.CurrentPackFileName);

        service.SetLanguage("en-US", "en-US-machine.json");
        Assert.Equal("Machine", service.Translate("valid.key"));
        Assert.Equal("en-US-machine.json", service.CurrentPackFileName);
    }

    [Fact]
    public void CommunityPackMayShareTheBuiltInLocale()
    {
        Directory.CreateDirectory(_root);
        WritePack(
            "zh-CN Dev.json",
            "zh-CN",
            "简体中文",
            "Hrenact-Dev",
            new Dictionary<string, string> { ["valid.key"] = "开发版" });
        var service = CreateService();

        LanguageOption communityOption = Assert.Single(service.Languages, option =>
            !option.IsOfficial &&
            string.Equals(option.Locale, LocalizationService.OfficialLocale, StringComparison.OrdinalIgnoreCase));
        Assert.True(communityOption.ShowFileName);
        Assert.True(communityOption.Label.IndexOf("zh-CN Dev.json", StringComparison.Ordinal) >= 0);

        service.SetLanguage("zh-CN", "zh-CN Dev.json");
        Assert.False(service.IsOfficialLanguage);
        Assert.Equal("开发版", service.Translate("valid.key"));

        service.SetLanguage("zh-CN");
        Assert.True(service.IsOfficialLanguage);
        Assert.Equal("有效", service.Translate("valid.key"));
    }

    [Fact]
    public void ExportedTemplateUsesFlatKeyValueStrings()
    {
        var service = CreateService();
        string output = Path.Combine(_root, "export", "template.json");

        service.ExportTemplate(output);

        LanguagePackDocument document = JsonConvert.DeserializeObject<LanguagePackDocument>(File.ReadAllText(output))!;
        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("有效", document.Strings!["valid.key"]);
        string json = File.ReadAllText(output);
        Assert.DoesNotContain("\"source\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\n\n    \"missing.key\"", json.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.True(json.IndexOf("\"invalid.key\"", StringComparison.Ordinal) < json.IndexOf("\"missing.key\"", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedOfficialCatalogLoadsAndUsesChinese()
    {
        var service = new LocalizationService(_root);

        Assert.True(service.IsOfficialLanguage);
        Assert.True(service.CatalogCount > 0);
        Assert.Equal("TrackSwap VR", service.Translate("app.title"));
        Assert.Equal("语言", service.Translate("language.title"));
    }

    [Fact]
    public void FormatAllowsTranslationsToReorderAndRepeatPlaceholders()
    {
        LocalizationManager.Initialize(CreateService());

        string formatted = Tr.Format("format.key", "first", "second");

        Assert.Equal("second / first / second", formatted);
    }

    [Fact]
    public void FormatSupportsCompositeNumericFormats()
    {
        LocalizationManager.Initialize(CreateService());
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            Assert.Equal("72.5 Hz", Tr.Format("formatted.frequency", 72.5));
            Assert.Equal("1.25–7 m", Tr.Format("formatted.range", 1.25, 7.0));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void MalformedLocalizedFormatDoesNotCrashTheCaller()
    {
        LocalizationManager.Initialize(CreateService());

        Assert.Equal("{0", Tr.Format("malformed.format", 1));
    }

    [Fact]
    public void DeviceBaseNameStripsStatusTemplateWhenPlaceholderIsReordered()
    {
        LocalizationManager.Initialize(CreateService());

        string displayName = DeviceOption.BaseDisplayName("Tracker · online");

        Assert.Equal("Tracker", displayName);
    }

    [Fact]
    public void WindowLocalizationChangesTitleWithoutReplacingContent()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                LocalizationManager.Initialize(CreateService());
                var content = new object();
                var window = new Window { Content = content };

                Localize.SetKey(window, "valid.key");

                Assert.Equal("有效", window.Title);
                Assert.Same(content, window.Content);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            throw failure;
        }
    }

    [Fact]
    public void MissingLocalizedControlDoesNotInterceptPointerInput()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                LocalizationManager.Initialize(CreateService());
                var button = new Button();
                Localize.SetKey(button, "missing.key");
                var mouseEvent = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
                };

                button.RaiseEvent(mouseEvent);

                Assert.False(mouseEvent.Handled);
                Assert.Null(button.ToolTip);
                Assert.Null(button.Cursor);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            throw failure;
        }
    }

    private LocalizationService CreateService()
    {
        return new LocalizationService(_root, new Dictionary<string, string>
        {
            ["valid.key"] = "有效",
            ["missing.key"] = "缺失",
            ["invalid.key"] = "当前原文",
            ["format.key"] = "{1} / {0} / {1}",
            ["formatted.frequency"] = "{0:0.##} Hz",
            ["formatted.range"] = "{0:0.##}–{1:0.##} m",
            ["malformed.format"] = "{0",
            ["device.status.online"] = "{0} · online",
            ["device.status.offline"] = "{0} · offline",
            ["device.status.saved"] = "{0} · saved"
        });
    }

    private void WritePack(string locale, Dictionary<string, string> strings)
    {
        WritePack(locale + ".json", locale, "English", "Test", strings);
    }

    private void WritePack(
        string fileName,
        string locale,
        string displayName,
        string author,
        Dictionary<string, string> strings)
    {
        var document = new LanguagePackDocument
        {
            SchemaVersion = 1,
            Locale = locale,
            DisplayName = displayName,
            Author = author,
            TargetTrackSwapVersion = "v011",
            Strings = strings
        };
        File.WriteAllText(
            Path.Combine(_root, fileName),
            JsonConvert.SerializeObject(document));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
