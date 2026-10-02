using System;
using System.Collections.Generic;
using System.IO;
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
            ["device.status.online"] = "{0} · online",
            ["device.status.offline"] = "{0} · offline",
            ["device.status.saved"] = "{0} · saved"
        });
    }

    private void WritePack(string locale, Dictionary<string, string> strings)
    {
        var document = new LanguagePackDocument
        {
            SchemaVersion = 1,
            Locale = locale,
            DisplayName = "English",
            Author = "Test",
            TargetTrackSwapVersion = "v011",
            Strings = strings
        };
        File.WriteAllText(
            Path.Combine(_root, locale + ".json"),
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
