using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Framework.Localization
{
    /// <summary>提供本地化表数据行的实现。</summary>
    [Serializable]
    public sealed class LocalizationTableRow
    {
        public string key;
        public string[] values = Array.Empty<string>();
    }

    /// <summary>提供缺失翻译的实现。</summary>
    public sealed class MissingTranslation
    {
        public string Key { get; }
        public string Language { get; }

        /// <summary>使用调用方提供的key、language创建 MissingTranslation。</summary>
        internal MissingTranslation(string key, string language)
        {
            Key = key;
            Language = language;
        }
    }

    /// <summary>Validated, copied authoring data shared by CSV/TSV imports and Unity assets.</summary>
    /// <remarks>提供本地化表数据数据的实现。</remarks>
    public sealed class LocalizationTableData
    {
        private readonly string[] languages;
        private readonly LocalizationTableRow[] rows;
        private readonly LocalizationCatalog catalog;
        public IReadOnlyList<string> Languages { get; }
        public int RowCount => rows.Length;

        /// <summary>使用调用方提供的languages、rows创建 LocalizationTableData。</summary>
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

        /// <summary>创建目录。</summary>
        public LocalizationCatalog CreateCatalog() => catalog;

        /// <summary>构建目录。</summary>
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

        /// <summary>复制行。</summary>
        public LocalizationTableRow[] CopyRows()
        {
            var result = new LocalizationTableRow[rows.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Copy(rows[i]);
            }
            return result;
        }

        /// <summary>查找缺失 Translations。</summary>
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

        /// <summary>复制本地化表数据数据。</summary>
        private static LocalizationTableRow Copy(LocalizationTableRow row)
        {
            return new LocalizationTableRow { key = row.key, values = (string[])row.values.Clone() };
        }
    }
}
