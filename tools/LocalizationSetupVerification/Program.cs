using System;
using System.Linq;
using Game.Framework.Localization;

internal static class Program
{
    private static int passed;

    private static void Main()
    {
        Check("CSV preserves quotes, delimiters, multiline text and trailing empty cells", () =>
        {
            var table = LocalizationDelimitedText.Parse("key,en,fr\r\na,\"Hello, \"\"friend\"\"\",\"Bonjour\nami\"\r\nb,Only English,\r\n");
            var catalog = table.CreateCatalog();
            Equal(2, table.RowCount);
            True(catalog.TryGetExact("a", "en", out string english));
            Equal("Hello, \"friend\"", english);
            True(catalog.TryGetExact("a", "fr", out string french));
            Equal("Bonjour\nami", french);
            var missing = table.FindMissingTranslations();
            Equal(1, missing.Length);
            Equal("b", missing[0].Key);
            Equal("fr", missing[0].Language);
        });
        Check("BOM quoted headers and TSV exports are accepted", () =>
        {
            var table = LocalizationDelimitedText.Parse("\uFEFF\"key\"\ten\tzh-Hans\nui.confirm\tOK\t确定", '\t');
            True(table.CreateCatalog().TryGetExact("ui.confirm", "zh-Hans", out string text));
            Equal("确定", text);
        });
        Check("row and language authoring data is copied into a stable immutable snapshot", () =>
        {
            var languages = new[] { "en", "fr" };
            var rows = new[] { new LocalizationTableRow { key = "hello", values = new[] { "Hello", "Bonjour" } } };
            var table = new LocalizationTableData(languages, rows);
            languages[0] = "de";
            rows[0].values[0] = "mutated";
            var copied = table.CopyRows();
            copied[0].key = "changed";
            copied[0].values[0] = "changed";
            True(table.CreateCatalog().TryGetExact("hello", "en", out string text));
            Equal("Hello", text);
            Equal("en", table.Languages[0]);
            True(table.CreateCatalog().ContainsKey("hello"));
            True(!table.CreateCatalog().ContainsKey("changed"));
        });
        Check("exact cell inspection exposes missing translations instead of hiding them behind fallback", () =>
        {
            var catalog = LocalizationDelimitedText.Parse("key,en,fr\na,Hello,\nb,,").CreateCatalog();
            True(catalog.ContainsKey("b"));
            True(!catalog.TryGetExact("a", "fr", out _));
            True(!catalog.TryGetExact("a", "fr-CA", out _));
            Equal(new[] { "a", "b" }, catalog.Keys.ToArray());
        });
        Check("duplicate, empty and whitespace-padded keys cannot be baked", () =>
        {
            Throws<ArgumentException>(() => LocalizationDelimitedText.Parse("key,en\na,x\na,y"));
            Throws<ArgumentException>(() => LocalizationDelimitedText.Parse("key,en\n,x"));
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("key,en\n a,x"));
        });
        Check("bad headers, duplicate cultures and inconsistent cells cannot be baked", () =>
        {
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("id,en\na,x"));
            Throws<ArgumentException>(() => LocalizationDelimitedText.Parse("key,en,EN\na,x,y"));
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("key,en,fr\na,x"));
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("key,en\na,x,y"));
        });
        Check("malformed quoting fails without a partial result", () =>
        {
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("key,en\na,\"open"));
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("key,en\na,\"closed\"bad"));
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("key,en\na,in\"valid"));
            Throws<ArgumentOutOfRangeException>(() => LocalizationDelimitedText.Parse("key;en", ';'));
        });
        Check("composite formatting validation is retained for authoring imports", () =>
        {
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("key,en,fr\na,{0},{1}"));
            Throws<FormatException>(() => LocalizationDelimitedText.Parse("key,en\na,{"));
        });
        Check("blank physical records are ignored but blank authored translations remain visible", () =>
        {
            var table = LocalizationDelimitedText.Parse("\r\nkey,en,fr\n\na,,\n\n");
            Equal(1, table.RowCount);
            Equal(2, table.FindMissingTranslations().Length);
        });
        Check("invalid replacement does not mutate an existing parsed table", () =>
        {
            var current = LocalizationDelimitedText.Parse("key,en\na,good");
            Throws<FormatException>(() => current = LocalizationDelimitedText.Parse("key,en\na,\"broken"));
            True(current.CreateCatalog().TryGetExact("a", "en", out string text));
            Equal("good", text);
        });
        Console.WriteLine("PASS " + passed + " localization authoring/import snapshot scenarios; Unity assets/inspector/bootstrap are verified separately.");
    }

    private static void Check(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new Exception("Expected true.");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (expected is Array a && actual is Array b)
        {
            if (!a.Cast<object>().SequenceEqual(b.Cast<object>()))
            {
                throw new Exception("Sequence mismatch.");
            }
            return;
        }
        if (!Equals(expected, actual))
        {
            throw new Exception("Expected " + expected + ", got " + actual);
        }
    }

    private static void Throws<T>(Action test) where T : Exception
    {
        try
        {
            test();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
