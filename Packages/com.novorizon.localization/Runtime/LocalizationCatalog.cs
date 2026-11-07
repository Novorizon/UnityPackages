using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Framework.Localization
{
    public sealed class TranslationColumn<T>
    {
        public string Language { get; }
        internal Func<T, string> Read { get; }

        public TranslationColumn(string language, Func<T, string> read)
        {
            Language = CultureInfo.GetCultureInfo(language).Name;
            Read = read ?? throw new ArgumentNullException(nameof(read));
            if (Language.Length == 0)
            {
                throw new ArgumentException("Use an explicit language.", nameof(language));
            }
        }
    }

    /// <summary>An immutable copy of project-owned rows, including generated Luban rows.</summary>
    public sealed class LocalizationCatalog
    {
        private readonly Dictionary<string, Dictionary<string, string>> languages;
        private readonly HashSet<string> keys;
        public IReadOnlyList<string> Languages { get; }
        public int KeyCount => Keys.Count;
        public IReadOnlyList<string> Keys { get; }

        private LocalizationCatalog(Dictionary<string, Dictionary<string, string>> languages, List<string> keys)
        {
            this.languages = languages;
            Languages = new List<string>(languages.Keys).AsReadOnly();
            Keys = keys.AsReadOnly();
            this.keys = new HashSet<string>(keys, StringComparer.Ordinal);
        }

        public static LocalizationCatalog FromRows<T>(IEnumerable<T> rows, Func<T, string> key, params TranslationColumn<T>[] columns)
        {
            if (rows == null || key == null || columns == null)
            {
                throw new ArgumentNullException(rows == null ? nameof(rows) : key == null ? nameof(key) : nameof(columns));
            }

            if (columns.Length == 0)
            {
                throw new ArgumentException("At least one language is required.", nameof(columns));
            }

            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                if (column == null || result.ContainsKey(column.Language))
                {
                    throw new ArgumentException("Null or duplicate language column.", nameof(columns));
                }

                result.Add(column.Language, new Dictionary<string, string>(StringComparer.Ordinal));
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            var orderedKeys = new List<string>();
            foreach (var row in rows)
            {
                string id = key(row);
                if (string.IsNullOrWhiteSpace(id) || !keys.Add(id))
                {
                    throw new ArgumentException("Empty or duplicate translation key: " + id, nameof(rows));
                }

                orderedKeys.Add(id);
                HashSet<int> expected = null;
                foreach (var column in columns)
                {
                    string text = column.Read(row);
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    var arguments = Arguments(text);
                    if (expected != null && !expected.SetEquals(arguments))
                    {
                        throw new FormatException("Placeholder mismatch for " + id + " / " + column.Language);
                    }

                    expected = arguments;
                    result[column.Language].Add(id, text);
                }
            }

            return new LocalizationCatalog(result, orderedKeys);
        }

        private sealed class FormatProbe : IFormattable
        {
            public string ToString(string format, IFormatProvider provider) => string.Empty;
        }

        private static HashSet<int> Arguments(string text)
        {
            var indices = new HashSet<int>();
            int maximum = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '{')
                {
                    continue;
                }

                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    i++;
                    continue;
                }

                int start = ++i;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                {
                    i++;
                }

                if (i == start || !int.TryParse(text.Substring(start, i - start), out int index) || index > 63)
                {
                    throw new FormatException("Use indexed placeholders {0} through {63}.");
                }

                indices.Add(index);
                maximum = Math.Max(maximum, index);
            }

            var probes = new object[maximum + 1];
            for (int i = 0; i < probes.Length; i++)
            {
                probes[i] = new FormatProbe();
            }

            // Let the platform validate braces, alignment and composite-format syntax.
            string.Format(CultureInfo.InvariantCulture, text, probes);
            return indices;
        }

        public bool ContainsLanguage(string language) => languages.ContainsKey(CultureInfo.GetCultureInfo(language).Name);

        public bool ContainsKey(string key)
        {
            return key != null && keys.Contains(key);
        }

        /// <summary>Read an exact authored cell without parent/fallback substitution; empty cells return false.</summary>
        public bool TryGetExact(string key, string language, out string text)
        {
            if (key == null || language == null)
            {
                throw new ArgumentNullException(key == null ? nameof(key) : nameof(language));
            }
            if (languages.TryGetValue(CultureInfo.GetCultureInfo(language).Name, out var values))
            {
                return values.TryGetValue(key, out text);
            }
            text = null;
            return false;
        }

        internal bool TryGet(string key, CultureInfo culture, out string text)
        {
            while (culture.Name.Length != 0)
            {
                if (languages.TryGetValue(culture.Name, out var values) && values.TryGetValue(key, out text))
                {
                    return true;
                }

                culture = culture.Parent;
            }

            text = null;
            return false;
        }
    }
}
