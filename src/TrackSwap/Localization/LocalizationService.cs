using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TrackSwap.Protocol;

namespace TrackSwap.Localization
{
    public sealed class LocalizationService
    {
        public const string OfficialLocale = "zh-CN";
        private const int MaximumPackBytes = 1024 * 1024;
        private const int MaximumTranslationCharacters = 1024;
        private static readonly Regex LocalePattern = new Regex(
            "^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$",
            RegexOptions.CultureInvariant);
        private readonly string _languageDirectory;
        private readonly Dictionary<string, string> _catalog;
        private readonly List<LanguageOption> _languages = new List<LanguageOption>();
        private readonly List<LocalizationIssue> _issues = new List<LocalizationIssue>();
        private Dictionary<string, string> _activeTranslations = new Dictionary<string, string>(StringComparer.Ordinal);

        public LocalizationService(string languageDirectory = null)
            : this(languageDirectory, LoadOfficialCatalog())
        {
        }

        internal LocalizationService(
            string languageDirectory,
            IReadOnlyDictionary<string, string> catalog)
        {
            _languageDirectory = Path.GetFullPath(languageDirectory ?? Path.Combine(
                TrackSwapDataPaths.ActiveDataDirectory,
                "i18n"));
            _catalog = catalog.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal);
            Reload(OfficialLocale);
        }

        public event EventHandler LanguageChanged;

        public string LanguageDirectory => _languageDirectory;

        public string CurrentLocale { get; private set; } = OfficialLocale;

        public string CurrentPackFileName { get; private set; }

        public IReadOnlyList<LanguageOption> Languages => _languages;

        public IReadOnlyList<LocalizationIssue> Issues => _issues;

        public int CatalogCount => _catalog.Count;

        public bool IsOfficialLanguage => CurrentPackFileName == null &&
            string.Equals(CurrentLocale, OfficialLocale, StringComparison.OrdinalIgnoreCase);

        public string Translate(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "[missing-key]";
            }
            if (_activeTranslations.TryGetValue(key, out string translated) &&
                !string.IsNullOrWhiteSpace(translated))
            {
                return translated;
            }
            return "[" + key + "]";
        }

        public bool IsMissing(string key)
        {
            return !_activeTranslations.TryGetValue(key ?? string.Empty, out string translated) ||
                string.IsNullOrWhiteSpace(translated);
        }

        public void Reload(string requestedLocale = null, string requestedPackFileName = null)
        {
            Directory.CreateDirectory(_languageDirectory);
            _languages.Clear();
            _issues.Clear();
            _languages.Add(new LanguageOption(OfficialLocale, "简体中文", "Hrenact", true, null, false));

            var packs = new List<LoadedLanguagePack>();
            foreach (string path in Directory
                .EnumerateFiles(_languageDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(item => Path.GetFileName(item), StringComparer.OrdinalIgnoreCase))
            {
                TryLoadPack(path, packs);
            }

            var duplicateLocales = new HashSet<string>(
                packs.GroupBy(item => item.Locale, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);
            if (packs.Any(pack => string.Equals(
                    pack.Locale,
                    OfficialLocale,
                    StringComparison.OrdinalIgnoreCase)))
            {
                duplicateLocales.Add(OfficialLocale);
            }
            foreach (LoadedLanguagePack pack in packs
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Author, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
            {
                _languages.Add(new LanguageOption(
                    pack.Locale,
                    pack.DisplayName,
                    pack.Author,
                    false,
                    pack.FileName,
                    duplicateLocales.Contains(pack.Locale)));
            }

            string desired = string.IsNullOrWhiteSpace(requestedLocale) ? CurrentLocale : requestedLocale;
            string desiredPackFileName = requestedPackFileName;
            if (requestedLocale == null && requestedPackFileName == null)
            {
                desiredPackFileName = CurrentPackFileName;
            }
            LoadedLanguagePack selected = null;
            bool requestedSpecificPack = !string.IsNullOrWhiteSpace(desiredPackFileName);
            if (requestedSpecificPack)
            {
                selected = packs.FirstOrDefault(pack =>
                    string.Equals(pack.FileName, desiredPackFileName, StringComparison.OrdinalIgnoreCase));
            }
            if (selected == null && !requestedSpecificPack &&
                !string.Equals(desired, OfficialLocale, StringComparison.OrdinalIgnoreCase))
            {
                selected = packs.FirstOrDefault(pack =>
                    string.Equals(pack.Locale, desired, StringComparison.OrdinalIgnoreCase));
            }
            if (selected == null)
            {
                CurrentLocale = OfficialLocale;
                CurrentPackFileName = null;
                _activeTranslations = _catalog.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal);
            }
            else
            {
                CurrentLocale = selected.Locale;
                CurrentPackFileName = selected.FileName;
                _activeTranslations = selected.Translations;
                BuildSelectedPackIssues(selected);
            }
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetLanguage(string locale, string packFileName = null)
        {
            Reload(locale, packFileName);
        }

        public void ExportTemplate(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A template destination is required.", nameof(path));
            }
            var template = new LanguagePackDocument
            {
                SchemaVersion = 1,
                Locale = "en-US",
                DisplayName = "English",
                Author = string.Empty,
                TargetTrackSwapVersion = "v013",
                Strings = _catalog.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
            };
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(
                fullPath,
                SerializeLanguagePack(template),
                new UTF8Encoding(false));
        }

        private static string SerializeLanguagePack(LanguagePackDocument document)
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"schemaVersion\": " + document.SchemaVersion.ToString(CultureInfo.InvariantCulture) + ",");
            builder.AppendLine("  \"locale\": " + JsonConvert.ToString(document.Locale) + ",");
            builder.AppendLine("  \"displayName\": " + JsonConvert.ToString(document.DisplayName) + ",");
            builder.AppendLine("  \"author\": " + JsonConvert.ToString(document.Author ?? string.Empty) + ",");
            builder.AppendLine("  \"targetTrackSwapVersion\": " + JsonConvert.ToString(document.TargetTrackSwapVersion) + ",");
            builder.AppendLine("  \"strings\": {");

            KeyValuePair<string, string>[] strings = (document.Strings ?? new Dictionary<string, string>())
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToArray();
            string previousGroup = null;
            for (int index = 0; index < strings.Length; index++)
            {
                KeyValuePair<string, string> item = strings[index];
                int separator = item.Key.IndexOf('.');
                string group = separator < 0 ? item.Key : item.Key.Substring(0, separator);
                if (previousGroup != null && !string.Equals(previousGroup, group, StringComparison.Ordinal))
                {
                    builder.AppendLine();
                }
                builder.Append("    ");
                builder.Append(JsonConvert.ToString(item.Key));
                builder.Append(": ");
                builder.Append(JsonConvert.ToString(item.Value));
                builder.AppendLine(index < strings.Length - 1 ? "," : string.Empty);
                previousGroup = group;
            }

            builder.AppendLine("  }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private void TryLoadPack(string path, ICollection<LoadedLanguagePack> packs)
        {
            string fileKey = "file:" + Path.GetFileName(path);
            try
            {
                var file = new FileInfo(path);
                if (file.Length > MaximumPackBytes)
                {
                    _issues.Add(new LocalizationIssue(fileKey, LocalizationIssueKind.Invalid, "Language pack exceeds 1 MiB."));
                    return;
                }
                LanguagePackDocument document = JsonConvert.DeserializeObject<LanguagePackDocument>(File.ReadAllText(path));
                if (document == null || document.SchemaVersion != 1 ||
                    string.IsNullOrWhiteSpace(document.Locale) || !LocalePattern.IsMatch(document.Locale) ||
                    string.IsNullOrWhiteSpace(document.DisplayName) || document.Strings == null)
                {
                    _issues.Add(new LocalizationIssue(fileKey, LocalizationIssueKind.Invalid, "Language pack metadata is invalid."));
                    return;
                }
                var translations = new Dictionary<string, string>(StringComparer.Ordinal);
                var invalidKeys = new HashSet<string>(StringComparer.Ordinal);
                var staleKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> pair in document.Strings)
                {
                    if (!_catalog.ContainsKey(pair.Key))
                    {
                        staleKeys.Add(pair.Key);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > MaximumTranslationCharacters ||
                        ContainsUnsupportedControlCharacter(pair.Value))
                    {
                        invalidKeys.Add(pair.Key);
                        continue;
                    }
                    translations[pair.Key] = pair.Value;
                }
                packs.Add(new LoadedLanguagePack(
                    Path.GetFileName(path),
                    document.Locale,
                    document.DisplayName.Trim(),
                    document.Author?.Trim() ?? string.Empty,
                    translations,
                    invalidKeys,
                    staleKeys));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
            {
                _issues.Add(new LocalizationIssue(fileKey, LocalizationIssueKind.Invalid, exception.Message));
            }
        }

        private void BuildSelectedPackIssues(LoadedLanguagePack pack)
        {
            foreach (string key in _catalog.Keys.Where(key => !pack.Translations.ContainsKey(key) && !pack.InvalidKeys.Contains(key)).OrderBy(key => key, StringComparer.Ordinal))
            {
                _issues.Add(new LocalizationIssue(key, LocalizationIssueKind.Missing, "Translation is missing."));
            }
            foreach (string key in pack.InvalidKeys.OrderBy(key => key, StringComparer.Ordinal))
            {
                _issues.Add(new LocalizationIssue(key, LocalizationIssueKind.Invalid, "Translation is empty or contains unsupported control characters."));
            }
            foreach (string key in pack.StaleKeys.OrderBy(key => key, StringComparer.Ordinal))
            {
                _issues.Add(new LocalizationIssue(key, LocalizationIssueKind.Stale, "Key no longer exists in the official catalog."));
            }
        }

        private static bool ContainsUnsupportedControlCharacter(string value)
        {
            return value.Any(character => char.IsControl(character) && character != '\r' && character != '\n' && character != '\t');
        }

        private static IReadOnlyDictionary<string, string> LoadOfficialCatalog()
        {
            Assembly assembly = typeof(LocalizationService).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream("TrackSwap.Localization.zh-CN.json") ??
                throw new InvalidOperationException("The built-in localization catalog is missing."))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                LanguagePackDocument document = JsonConvert.DeserializeObject<LanguagePackDocument>(reader.ReadToEnd()) ??
                    throw new InvalidDataException("The built-in localization catalog is invalid.");
                return document.Strings ?? throw new InvalidDataException("The built-in localization catalog has no strings.");
            }
        }

        private sealed class LoadedLanguagePack
        {
            public LoadedLanguagePack(string fileName, string locale, string displayName, string author, Dictionary<string, string> translations, HashSet<string> invalidKeys, HashSet<string> staleKeys)
            {
                FileName = fileName;
                Locale = locale;
                DisplayName = displayName;
                Author = author;
                Translations = translations;
                InvalidKeys = invalidKeys;
                StaleKeys = staleKeys;
            }

            public string FileName { get; }
            public string Locale { get; }
            public string DisplayName { get; }
            public string Author { get; }
            public Dictionary<string, string> Translations { get; }
            public HashSet<string> InvalidKeys { get; }
            public HashSet<string> StaleKeys { get; }
        }
    }

    public static class LocalizationManager
    {
        private static LocalizationService _current;

        public static LocalizationService Current => _current ?? (_current = new LocalizationService());

        public static void Initialize(LocalizationService service)
        {
            _current = service ?? throw new ArgumentNullException(nameof(service));
        }
    }

    public sealed class LanguagePackDocument
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }
        [JsonProperty("locale")]
        public string Locale { get; set; }
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
        [JsonProperty("author")]
        public string Author { get; set; }
        [JsonProperty("targetTrackSwapVersion")]
        public string TargetTrackSwapVersion { get; set; }
        [JsonProperty("strings")]
        public Dictionary<string, string> Strings { get; set; }
    }

    public sealed class LanguageOption
    {
        public LanguageOption(
            string locale,
            string displayName,
            string author,
            bool isOfficial,
            string fileName,
            bool showFileName)
        {
            Locale = locale;
            DisplayName = displayName;
            Author = author;
            IsOfficial = isOfficial;
            FileName = fileName;
            ShowFileName = showFileName;
        }

        public string Locale { get; }
        public string DisplayName { get; }
        public string Author { get; }
        public bool IsOfficial { get; }
        public string FileName { get; }
        public bool ShowFileName { get; }
        public string Label
        {
            get
            {
                string label = string.IsNullOrWhiteSpace(Author) ? DisplayName : DisplayName + " · " + Author;
                return ShowFileName && !string.IsNullOrWhiteSpace(FileName)
                    ? label + "（" + FileName + "）"
                    : label;
            }
        }
    }

    public enum LocalizationIssueKind
    {
        Missing,
        Invalid,
        Stale
    }

    public sealed class LocalizationIssue
    {
        public LocalizationIssue(string key, LocalizationIssueKind kind, string detail)
        {
            Key = key;
            Kind = kind;
            Detail = detail;
        }

        public string Key { get; }
        public LocalizationIssueKind Kind { get; }
        public string Detail { get; }
    }
}
