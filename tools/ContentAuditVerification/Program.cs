using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Diagnostics.Editor;
using Game.Framework.Localization;
using Game.Framework.Story;

internal static class Program
{
    private sealed class Row
    {
        internal string Key;
        internal string English;
        internal string French;
    }

    private sealed class ImmediatePresenter : IStoryPresenter
    {
        public Task OpenAsync(StorySequence sequence, Guid sessionId, CancellationToken token) => Task.CompletedTask;
        public Task CloseAsync(Guid sessionId) => Task.CompletedTask;
        public void Advance(StoryStepContext context)
        {
        }
        public Task<StoryStepOutcome> ExecuteAsync(StoryNode node, StoryStepContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(node.Kind == StoryNodeKind.Choice ? StoryStepOutcome.Choose(node.Choices[0].Id) : StoryStepOutcome.Continue);
        }
    }

    private sealed class Measurement
    {
        public string Name { get; set; }
        public int OperationsPerSample { get; set; }
        public int Samples { get; set; }
        public double MedianMilliseconds { get; set; }
        public double P95Milliseconds { get; set; }
        public long MedianAllocatedBytes { get; set; }
    }

    private sealed class PrivateGetterProbe
    {
        internal static int Reads;
        public static int Hidden
        {
            private get
            {
                Reads++;
                return 42;
            }
            set
            {
            }
        }
        public static int Visible => 7;
    }

    private static int checks;
    private static object retained;

    private static void Main(string[] args)
    {
        VerifyCatalogFormats();
        VerifyCatalogSnapshotsAndTableParsing();
        VerifyStorySnapshotsAndLongGraphs();
        VerifyDiagnosticIdentityAndIsolation();

        Row[] rows = Enumerable.Range(0, 10000).Select(index => new Row
        {
            Key = "key." + index.ToString(CultureInfo.InvariantCulture),
            English = "English {0} / {63}",
            French = "Français {63} / {0}"
        }).ToArray();
        var columns = new[]
        {
            new TranslationColumn<Row>("en", row => row.English),
            new TranslationColumn<Row>("fr", row => row.French)
        };
        var catalog = LocalizationCatalog.FromRows(rows, row => row.Key, columns);
        var french = CultureInfo.GetCultureInfo("fr-CA");
        var oldLeases = Enumerable.Range(0, 10000).Select(index => new DiagnosticLease
        {
            sessionId = "session",
            leaseId = index + 1,
            owner = "Battle/HUD",
            location = "portraits/hero",
            state = "Active"
        }).ToArray();
        var oldSnapshot = new DiagnosticSnapshot { leases = oldLeases };
        Game.Resource.ResourceManager.Publish(oldLeases);
        var newSnapshot = new DiagnosticSnapshot { leases = oldLeases.Select(lease => new DiagnosticLease
        {
            sessionId = lease.sessionId,
            leaseId = lease.leaseId,
            owner = lease.owner,
            location = lease.location,
            state = lease.leaseId % 10 == 0 ? "Releasing" : "Active"
        }).ToArray() };
        var nodes = Enumerable.Range(0, 1000).Select(index => new StoryNode("line." + index, StoryNodeKind.Say, textKey: "hello")).ToList();
        nodes.Add(new StoryNode("end", StoryNodeKind.End));
        var story = new StorySequence("linear", "1", nodes);
        var presenter = new ImmediatePresenter();
        var measurements = new List<Measurement>
        {
            Measure("CatalogBuild10000RowsTwoLanguagesSparse63", 20000, () => retained = LocalizationCatalog.FromRows(rows, row => row.Key, columns)),
            Measure("CatalogParentLookup100000", 100000, () =>
            {
                string value = null;
                for (int index = 0; index < 100000; index++)
                {
                    if (!catalog.TryGet(rows[index % rows.Length].Key, french, out value))
                    {
                        throw new InvalidOperationException("Expected authored French via fr-CA parent.");
                    }
                }
                retained = value;
            }),
            Measure("DiagnosticDiff10000LeasesTenPercentChanged", 10000, () => retained = DiagnosticSnapshotComparer.Compare(oldSnapshot, newSnapshot)),
            Measure("DiagnosticCapture10000LeasesPublicReflection", 10000, () => retained = PackageDiagnostics.Capture()),
            Measure("Story1000ImmediateDialogueNodes", 1000, () =>
            {
                using (var player = new StoryPlayer(presenter))
                {
                    StoryResult result = player.Play(story).Completion.GetAwaiter().GetResult();
                    Equal(StoryStatus.Completed, result.Status);
                    retained = result;
                }
            })
        };
        var report = new
        {
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CorrectnessChecks = checks,
            Scope = "Actual C# source on .NET; excludes Unity/TMP/Video native execution and target-device performance. Timing is descriptive, without pass/fail thresholds.",
            Measurements = measurements
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
        if (args.Length == 2 && args[0] == "--output")
        {
            File.WriteAllText(args[1], json + Environment.NewLine);
        }
        GC.KeepAlive(retained);
    }

    private static Measurement Measure(string name, int operations, Action action)
    {
        for (int index = 0; index < 3; index++)
        {
            action();
        }
        var elapsed = new double[9];
        var allocated = new long[9];
        for (int index = 0; index < elapsed.Length; index++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            action();
            long end = Stopwatch.GetTimestamp();
            allocated[index] = GC.GetAllocatedBytesForCurrentThread() - before;
            elapsed[index] = (end - start) * 1000d / Stopwatch.Frequency;
        }
        Array.Sort(elapsed);
        Array.Sort(allocated);
        return new Measurement
        {
            Name = name,
            OperationsPerSample = operations,
            Samples = elapsed.Length,
            MedianMilliseconds = elapsed[elapsed.Length / 2],
            P95Milliseconds = elapsed[elapsed.Length - 1],
            MedianAllocatedBytes = allocated[allocated.Length / 2]
        };
    }

    private static void VerifyCatalogFormats()
    {
        var columns = new[]
        {
            new TranslationColumn<Row>("en", row => row.English),
            new TranslationColumn<Row>("fr", row => row.French)
        };
        foreach (string text in new[] { "Plain", "{{literal}}", "{0}", "{63}", "{0} / {63} / {0}", "{0,8:N2}", "{0,-8:yyyy-MM-dd}" })
        {
            var catalog = LocalizationCatalog.FromRows(new[] { new Row { Key = "hello", English = text, French = text } }, row => row.Key, columns);
            Equal(text, Exact(catalog, "hello", "en"));
        }
        string every = string.Join(" ", Enumerable.Range(0, 64).Select(index => "{" + index + "}"));
        var all = LocalizationCatalog.FromRows(new[] { new Row { Key = "all", English = every, French = every } }, row => row.Key, columns);
        Equal(every, Exact(all, "all", "fr"));
        foreach (string text in new[] { "{64}", "{-1}", "{99999999999999999999999999}", "{", "}", "{0", "{0,}", "{0:x{1}}" })
        {
            Throws<FormatException>(() => LocalizationCatalog.FromRows(new[] { new Row { Key = "bad", English = text, French = text } }, row => row.Key, columns));
        }
        Throws<FormatException>(() => LocalizationCatalog.FromRows(new[] { new Row { Key = "mismatch", English = "{0}", French = "{63}" } }, row => row.Key, columns));
        var duplicate = LocalizationCatalog.FromRows(new[] { new Row { Key = "same", English = "{0} {0}", French = "{0}" } }, row => row.Key, columns);
        Equal("{0}", Exact(duplicate, "same", "fr"));
        var empty = LocalizationCatalog.FromRows(new[] { new Row { Key = "missing", English = "{63}", French = "" } }, row => row.Key, columns);
        Equal(false, empty.TryGetExact("missing", "fr", out _));
    }

    private static void VerifyCatalogSnapshotsAndTableParsing()
    {
        var data = LocalizationDelimitedText.Parse("key,en,fr\r\nhello,\"Hello, {0}\",\"Bonjour, {0}\"\r\nline,\"one\ntwo\",\"un\ndeux\"\r\n");
        var rows = data.CopyRows();
        rows[0].values[0] = "mutated";
        Equal("Hello, {0}", Exact(data.CreateCatalog(), "hello", "en"));
        Equal("un\ndeux", Exact(data.CreateCatalog(), "line", "fr"));
        Equal(false, data.CreateCatalog().TryGetExact("unknown", "en", out _));
        var errors = data.FindMissingTranslations();
        Equal(0, errors.Length);
    }

    private static void VerifyStorySnapshotsAndLongGraphs()
    {
        var options = new[] { new StoryChoice("yes", "yes", "end") };
        var node = new StoryNode("choice", StoryNodeKind.Choice, choices: options);
        options[0] = new StoryChoice("no", "no", "end");
        Equal("yes", node.Choices[0].Id);
        var nodes = new List<StoryNode>();
        for (int index = 0; index < 20000; index++)
        {
            nodes.Add(new StoryNode("node." + index, StoryNodeKind.SetBackground));
        }
        nodes.Add(new StoryNode("end", StoryNodeKind.End));
        var sequence = new StorySequence("long", "1", nodes);
        Equal(20001, sequence.Nodes.Count);
        Equal("node.0", sequence.EntryNodeId);
        Throws<ArgumentException>(() => new StorySequence("cycle", "1", new[]
        {
            new StoryNode("a", StoryNodeKind.SetBackground, nextId: "b"),
            new StoryNode("b", StoryNodeKind.SetBackground, nextId: "a")
        }));
        Throws<ArgumentException>(() => new StorySequence("unreachable", "1", new[]
        {
            new StoryNode("end", StoryNodeKind.End),
            new StoryNode("extra", StoryNodeKind.End)
        }));
    }

    private static void VerifyDiagnosticIdentityAndIsolation()
    {
        PrivateGetterProbe.Reads = 0;
        var guarded = PackageDiagnostics.Read(typeof(PrivateGetterProbe), false, "Hidden", "Visible");
        Equal(0, PrivateGetterProbe.Reads);
        Equal(2, guarded.Length);
        Equal(true, guarded[0].value.StartsWith("Error:", StringComparison.Ordinal));
        Equal("7", guarded[1].value);
        var before = new DiagnosticSnapshot
        {
            metrics = new[] { new DiagnosticMetric { name = "Count", value = "2" } },
            leases = new[]
            {
                new DiagnosticLease { sessionId = "old", leaseId = 1, owner = "A", state = "Active" },
                new DiagnosticLease { sessionId = "old", leaseId = 2, owner = "B", state = "Active" }
            }
        };
        var after = new DiagnosticSnapshot
        {
            metrics = new[] { new DiagnosticMetric { name = "Count", value = "1" } },
            leases = new[]
            {
                new DiagnosticLease { sessionId = "new", leaseId = 1, owner = "A", state = "Active" },
                new DiagnosticLease { sessionId = "old", leaseId = 2, owner = "B", state = "Releasing" }
            }
        };
        var difference = DiagnosticSnapshotComparer.Compare(before, after);
        Equal(3, difference.leases.Length);
        Equal(1, difference.leases.Count(change => change.kind == "Added"));
        Equal(1, difference.leases.Count(change => change.kind == "Removed"));
        Equal(1, difference.leases.Count(change => change.kind == "Changed"));
        Equal("Active", before.leases[1].state);
        Equal("2", before.metrics[0].value);
        Equal("Changed", difference.metrics[0].kind);
    }

    private static string Exact(LocalizationCatalog catalog, string key, string language)
    {
        Equal(true, catalog.TryGetExact(key, language, out string text));
        return text;
    }

    private static void Equal<T>(T expected, T actual)
    {
        checks++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException("Expected " + expected + "; actual " + actual);
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        checks++;
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
