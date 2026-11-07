using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Localization;
using Game.Framework.Diagnostics.Editor;
using Luban;

internal static class Program
{
    private static int passed;
    private static void Main()
    {
        UnityThread.InitializeManual();
        try
        {
            Check("URP patch selection accepts newer 14.x and rejects unsupported major versions", () =>
            {
                Equal(true, Game.Framework.GpuAnimation.URP.Editor.UrpCompatibility.IsSupported("14.0.1"));
                Equal(true, Game.Framework.GpuAnimation.URP.Editor.UrpCompatibility.IsSupported("14.0.5"));
                Equal(true, Game.Framework.GpuAnimation.URP.Editor.UrpCompatibility.IsSupported("14.0.12"));
                Equal(false, Game.Framework.GpuAnimation.URP.Editor.UrpCompatibility.IsSupported("14.0.0"));
                Equal(false, Game.Framework.GpuAnimation.URP.Editor.UrpCompatibility.IsSupported("15.0.1"));
                Equal(false, Game.Framework.GpuAnimation.URP.Editor.UrpCompatibility.IsSupported("14.0.1-preview.1"));
                Equal(false, Game.Framework.GpuAnimation.URP.Editor.UrpCompatibility.IsSupported(null));
            });
            Check("real localization Excel generates independent Luban rows and binary translations", () =>
            {
                string root = Environment.GetEnvironmentVariable("LocalizationFixtureRoot");
                var tables = new localizationSample.Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(root, "data", name + ".bytes"))));
                var catalog = LocalizationCatalog.FromRows(tables.TbTranslation.DataList, row => row.Key,
                    new TranslationColumn<localizationSample.Translation>("zh-Hans", row => row.ZhHans),
                    new TranslationColumn<localizationSample.Translation>("en", row => row.En));
                using var manager = new LocalizationManager();
                manager.Initialize(catalog, "zh-Hans");
                Equal("确定", manager.Get("ui.confirm"));
                Equal("数量：42", manager.Format("item.count", 42));
                manager.SetLanguage("en-US");
                Equal("Quantity: 42", manager.Format("item.count", 42));
            });
            Check("catalog copies rows and resolves neutral, fallback and missing-key paths", () =>
            {
                var rows = new[] { new[] { "key", "你好", "Hello" }, new[] { "empty", "", "Fallback" } };
                var catalog = Catalog(rows);
                rows[0][2] = "Mutated";
                using var manager = new LocalizationManager();
                manager.Initialize(catalog, "zh-Hans-CN");
                Equal("你好", manager.Get("key"));
                Equal("Fallback", manager.Get("empty"));
                manager.SetLanguage("fr-FR");
                Equal("Hello", manager.Get("key"));
                Equal("missing", manager.Get("missing"));
            });
            Check("duplicate keys, languages, malformed format and placeholder drift are rejected", () =>
            {
                Throws<ArgumentException>(() => Catalog(new[] { new[] { "x", "a", "b" }, new[] { "x", "c", "d" } }));
                Throws<ArgumentException>(() => LocalizationCatalog.FromRows(new[] { "x" }, x => x, new TranslationColumn<string>("en", x => x), new TranslationColumn<string>("EN", x => x)));
                Throws<FormatException>(() => Catalog(new[] { new[] { "x", "{0}", "{1}" } }));
                Throws<FormatException>(() => Catalog(new[] { new[] { "x", "{", "ok" } }));
                Throws<FormatException>(() => Catalog(new[] { new[] { "x", "{100000}", "{100000}" } }));
                using var manager = new LocalizationManager();
                manager.Initialize(Catalog(new[] { new[] { "x", "{{{0}}}", "{{{0}}}" } }), "en");
                Equal("{7}", manager.Format("x", 7));
            });
            Check("failed preparation leaves the language and observers unchanged", () =>
            {
                using var manager = Service();
                int changed = 0;
                manager.LanguageChanged += _ => changed++;
                var operation = manager.SetLanguageAsync("zh-Hans", (_, _) => Task.FromException(new IOException("missing font")));
                Pump(operation);
                Throws<IOException>(() => operation.GetAwaiter().GetResult());
                Equal("en", manager.Language);
                Equal(0, changed);
            });
            Check("a later language change cancels slow preparation without stale publication", () =>
            {
                using var manager = Service();
                var late = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = manager.SetLanguageAsync("zh-Hans", (_, _) => late.Task);
                manager.SetLanguageAsync("fr-FR", (_, _) => Task.CompletedTask).GetAwaiter().GetResult();
                late.SetResult(true);
                Pump(first);
                Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
                Equal("fr-FR", manager.Language);
                Equal("Hello", manager.Get("key"));
            });
            Check("dispose cancels waiting preparation and cannot publish after restart", () =>
            {
                var manager = Service();
                var late = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = manager.SetLanguageAsync("zh-Hans", (_, _) => late.Task);
                manager.Dispose();
                late.SetResult(true);
                Pump(first);
                Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
                Equal(false, manager.IsInitialized);
                Throws<InvalidOperationException>(() => manager.Get("key"));
            });
            Check("a language change from a cancellation callback wins over the interrupted request", () =>
            {
                using var manager = Service();
                var late = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var first = manager.SetLanguageAsync("zh-Hans", (_, token) =>
                {
                    token.Register(() => manager.SetLanguage("fr-FR"));
                    return late.Task;
                });
                manager.SetLanguage("zh-Hans");
                Pump(first);
                Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
                Equal("fr-FR", manager.Language);
                late.SetResult(true);
            });
            Check("editor cleanup can dispose a service after the dispatch host has stopped", () =>
            {
                var manager = Service();
                UnityThread.Reset();
                manager.Dispose();
                Equal(false, manager.IsInitialized);
                UnityThread.InitializeManual();
            });
            Check("notification exceptions do not prevent remaining UI observers", () =>
            {
                using var manager = Service();
                int notified = 0;
                manager.LanguageChanged += _ => throw new InvalidOperationException("observer");
                manager.LanguageChanged += _ => notified++;
                manager.SetLanguage("zh-Hans");
                Equal(1, notified);
                Equal("你好", manager.Get("key"));
            });
            Check("reentrant language publication suppresses stale outer notifications", () =>
            {
                using var manager = Service();
                var observed = new List<string>();
                manager.LanguageChanged += language =>
                {
                    if (language == "zh-Hans")
                    {
                        manager.SetLanguage("fr-FR");
                    }
                };
                manager.LanguageChanged += language => observed.Add(language);
                manager.SetLanguage("zh-Hans");
                Equal("fr-FR", manager.Language);
                Equal("fr-FR", string.Join(",", observed));
            });
            Check("diagnostics never touches Instance or constructs uncreated services", () =>
            {
                ProbeService.Created = false;
                var metrics = PackageDiagnostics.Read(typeof(ProbeService), true, "Count");
                Equal("Not created / released", metrics.Single().value);
                Equal(0, ProbeService.InstanceCalls);
                Equal(false, Game.Framework.Timing.TimerManager.IsCreated);
                PackageDiagnostics.Capture();
                Equal(false, Game.Framework.Timing.TimerManager.IsCreated);
                Equal(false, LocalizationManager.IsCreated);
            });
            Check("diagnostics samples nested public counters and isolates failing getters", () =>
            {
                ProbeService.Created = true;
                var metrics = PackageDiagnostics.Read(typeof(ProbeService), true, "Count", "Bad", "Child.Count");
                Equal("3", metrics[0].value);
                Equal(true, metrics[1].value.StartsWith("Error: getter failed"));
                Equal("7", metrics[2].value);
                Equal(0, ProbeService.InstanceCalls);
                Equal("No read-only singleton accessor", PackageDiagnostics.Read(typeof(NoAccessor), true, "Count").Single().value);
            });
            Console.WriteLine("PASS " + passed + " localization/Luban/diagnostic scenarios; engine APIs and rendering are verified separately.");
        }
        finally
        {
            UnityThread.Reset();
        }
    }

    private static LocalizationCatalog Catalog(string[][] rows) => LocalizationCatalog.FromRows(rows, row => row[0], new TranslationColumn<string[]>("zh-Hans", row => row[1]), new TranslationColumn<string[]>("en", row => row[2]));
    private static LocalizationManager Service()
    {
        var manager = new LocalizationManager();
        manager.Initialize(Catalog(new[] { new[] { "key", "你好", "Hello" } }), "en");
        return manager;
    }

    private static void Pump(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.ElapsedMilliseconds < 5000)
        {
            UnityThread.Update();
            Thread.Sleep(1);
        }

        if (!task.IsCompleted)
        {
            throw new TimeoutException("Main-thread task did not finish.");
        }
    }

    private static void Check(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception("Expected " + expected + ", got " + actual);
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception("Expected " + typeof(T).Name);
    }

    public sealed class ProbeService
    {
        public static bool Created;
        public static int InstanceCalls;
        public static ProbeService Instance
        {
            get
            {
                InstanceCalls++;
                throw new Exception("Instance must never be used");
            }
        }
        public static bool TryGetInstance(out ProbeService value)
        {
            value = Created ? new ProbeService() : null;
            return Created;
        }
        public int Count => 3;
        public int Bad => throw new Exception("getter failed");
        public Nested Child => new Nested();
    }
    public sealed class Nested
    {
        public int Count => 7;
    }
    public sealed class NoAccessor
    {
        public static NoAccessor Instance => throw new Exception("Do not touch");
    }
}
