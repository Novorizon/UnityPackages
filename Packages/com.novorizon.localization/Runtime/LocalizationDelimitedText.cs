using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Framework.Localization
{
    /// <summary>Strict UTF-8 text-table shape: key,en,zh-Hans...; quoted fields may contain delimiters/newlines.</summary>
    public static class LocalizationDelimitedText
    {
        public static LocalizationTableData Parse(string text, char delimiter = ',')
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            if (delimiter != ',' && delimiter != '\t')
            {
                throw new ArgumentOutOfRangeException(nameof(delimiter), "Use CSV comma or TSV tab.");
            }
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }
            var records = ReadRecords(text, delimiter);
            if (records.Count == 0 || records[0].Length < 2 ||
                !string.Equals(records[0][0].TrimStart('\uFEFF').Trim(), "key", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("The first row must contain key followed by one or more language codes.");
            }
            var languages = new string[records[0].Length - 1];
            for (int i = 0; i < languages.Length; i++)
            {
                languages[i] = records[0][i + 1].Trim();
            }
            var rows = new List<LocalizationTableRow>();
            for (int index = 1; index < records.Count; index++)
            {
                var record = records[index];
                if (record.Length != languages.Length + 1)
                {
                    throw new FormatException("Record " + (index + 1) + " has a different column count than the header.");
                }
                if (record[0] != record[0].Trim())
                {
                    throw new FormatException("Translation keys cannot contain leading/trailing whitespace (record " + (index + 1) + ").");
                }
                var values = new string[languages.Length];
                Array.Copy(record, 1, values, 0, values.Length);
                rows.Add(new LocalizationTableRow { key = record[0], values = values });
            }
            return new LocalizationTableData(languages, rows);
        }

        private static List<string[]> ReadRecords(string text, char delimiter)
        {
            var result = new List<string[]>();
            var cells = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, closed = false, fieldStarted = false;
            for (int i = 0; i <= text.Length; i++)
            {
                bool end = i == text.Length;
                char value = end ? '\n' : text[i];
                if (quoted)
                {
                    if (end)
                    {
                        throw new FormatException("A quoted field is not closed.");
                    }
                    if (value == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                            closed = true;
                        }
                    }
                    else
                    {
                        field.Append(value);
                    }
                    continue;
                }
                if (value == '"')
                {
                    if (fieldStarted || closed)
                    {
                        throw new FormatException("Quotes must begin a field and embedded quotes must be doubled.");
                    }
                    quoted = true;
                    fieldStarted = true;
                    continue;
                }
                if (value == delimiter || value == '\n' || value == '\r')
                {
                    cells.Add(field.ToString());
                    field.Clear();
                    fieldStarted = closed = false;
                    if (value == delimiter && !end)
                    {
                        continue;
                    }
                    if (value == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    bool nonempty = false;
                    foreach (var cell in cells)
                    {
                        nonempty |= cell.Length != 0;
                    }
                    if (nonempty)
                    {
                        result.Add(cells.ToArray());
                    }
                    cells.Clear();
                    continue;
                }
                if (closed)
                {
                    throw new FormatException("Unexpected characters after a closing quote.");
                }
                fieldStarted = true;
                field.Append(value);
            }
            return result;
        }
    }
}
