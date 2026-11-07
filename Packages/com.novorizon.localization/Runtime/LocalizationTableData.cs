using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Framework.Localization
{
    [Serializable]
    public sealed class LocalizationTableRow
    {
        public string key;
        public string[] values = Array.Empty<string>();
    }

    public sealed class MissingTranslation
    {
        public string Key { get; }
        public string Language { get; }

        internal MissingTranslation(string key, string language)
        {
            Key = key;
            Language = language;
        }
    }

    /// <summary>Validated, copied authoring data shared by CSV/TSV imports and Unity assets.</summary>
    public sealed class LocalizationTableData
    {
        private readonly string[] languages;
        private readonly LocalizationTableRow[] rows;
        private readonly LocalizationCatalog catalog;
        public IReadOnlyList<string> Languages { get; }
        public int RowCount => rows.Length;

        public LocalizationTableData(IReadOnlyList<string> languages, IReadOnlyList<LocalizationTableRow> rows)
        {
            if (languages == null || rows == null)
            {
                throw new ArgumentNullException(languages == null ? nameof(languages) : nameof(rows));
            }
            this.languages = new string[languages.Count];
            for (int i = 0; i < languages.Count; i++)
            {
                this.languages[i] = CultureInfo.GetCultureInfo(languages[i]).Name;
            }
            this.rows = new LocalizationTableRow[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || row.values == null || row.values.Length != languages.Count)
                {
                    throw new ArgumentException("Every row needs one value per language column.", nameof(rows));
                }
                this.rows[i] = Copy(row);
            }
            Languages = Array.AsReadOnly(this.languages);
            catalog = BuildCatalog();
        }

        public LocalizationCatalog CreateCatalog() => catalog;

        private LocalizationCatalog BuildCatalog()
        {
            var columns = new TranslationColumn<LocalizationTableRow>[languages.Length];
            for (int index = 0; index < columns.Length; index++)
            {
                int column = index;
                columns[index] = new TranslationColumn<LocalizationTableRow>(languages[index], row => row.values[column]);
            }
            return LocalizationCatalog.FromRows(rows, row => row.key, columns);
        }

        public LocalizationTableRow[] CopyRows()
        {
            var result = new LocalizationTableRow[rows.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Copy(rows[i]);
            }
            return result;
        }

        public MissingTranslation[] FindMissingTranslations()
        {
            var result = new List<MissingTranslation>();
            foreach (var row in rows)
            {
                for (int column = 0; column < languages.Length; column++)
                {
                    if (string.IsNullOrEmpty(row.values[column]))
                    {
                        result.Add(new MissingTranslation(row.key, languages[column]));
                    }
                }
            }
            return result.ToArray();
        }

        private static LocalizationTableRow Copy(LocalizationTableRow row)
        {
            return new LocalizationTableRow { key = row.key, values = (string[])row.values.Clone() };
        }
    }
}
