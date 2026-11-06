using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Game.Framework;
using Game.Framework.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class Program
{
    private static int passed;
    private static readonly MetadataReference[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
        .Append(typeof(Singleton<>).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    private const string Fixture = @"
using System;
using System.Collections.Generic;
using Game.Framework;
public class Middle : Singleton<Demo> { }
public partial class Demo : Middle
{
    public static int Count;
    public static int Limit = LIMIT_VALUE;
    private static int secret = 7;
    public static int Copy = Limit;
    public static List<int> Items = new List<int> { 3, 4 };
    public static readonly List<int> ReadonlyItems = new List<int> { 8, 9 };
    public static readonly Dictionary<string, int> Map = new Dictionary<string, int> { [""a""] = 1 };
    public static readonly HashSet<int> Set = new HashSet<int> { 2, 3 };
    public static readonly object Gate = new object();
    public static readonly System.Threading.SemaphoreSlim Operations = new System.Threading.SemaphoreSlim(1, 1);
    public static int[] Array = { 1, 2 };
    public static int Property { get; private set; } = 6;
    public static event Action Changed;
    public static void Touch()
    {
        Count = 99;
        Limit = 99;
        Copy = 99;
        secret = 99;
        Property = 99;
        Items = null;
        Array[0] = 99;
        ReadonlyItems.Clear();
        Map.Clear();
        Set.Clear();
        Changed += () => { };
    }
    public static int Secret() => secret;
    public static int ListenerCount() => Changed?.GetInvocationList().Length ?? 0;
}
public partial class Generic<T> : ConfigurableSingleton<Generic<T>> where T : class, new()
{
    public static int Value = 5;
}
public partial class Container<T> where T : class
{
    public partial class Nested : Singleton<Nested>
    {
        private static int value = 23;
        public static int Read() => value;
        public static void Change()
        {
            value = 99;
        }
    }
}
public class Ordinary
{
    public static int Value = 30;
}
";

    private static void Main()
    {
        foreach (int limit in new[] { 1, 2 })
        {
            var assembly = Compile(Fixture.Replace("LIMIT_VALUE", limit.ToString()));
            var demo = assembly.GetType("Demo");
            demo.GetMethod("Touch").Invoke(null, null);
            var readonlyList = demo.GetField("ReadonlyItems").GetValue(null);
            var gate = demo.GetField("Gate").GetValue(null);
            var operations = demo.GetField("Operations").GetValue(null);
            EditorStaticReset.RestoreDeclaredValues();
            Equal(0, demo.GetField("Count").GetValue(null));
            Equal(limit, demo.GetField("Limit").GetValue(null));
            Equal(limit, demo.GetField("Copy").GetValue(null));
            Equal(7, demo.GetMethod("Secret").Invoke(null, null));
            Equal(6, demo.GetProperty("Property").GetValue(null));
            Equal(0, demo.GetMethod("ListenerCount").Invoke(null, null));
            True(((List<int>)demo.GetField("Items").GetValue(null)).SequenceEqual(new[] { 3, 4 }));
            True(((int[])demo.GetField("Array").GetValue(null)).SequenceEqual(new[] { 1, 2 }));
            True(ReferenceEquals(readonlyList, demo.GetField("ReadonlyItems").GetValue(null)));
            True(((List<int>)readonlyList).SequenceEqual(new[] { 8, 9 }));
            Equal(1, ((Dictionary<string, int>)demo.GetField("Map").GetValue(null))["a"]);
            True(((HashSet<int>)demo.GetField("Set").GetValue(null)).SetEquals(new[] { 2, 3 }));
            True(ReferenceEquals(gate, demo.GetField("Gate").GetValue(null)));
            True(ReferenceEquals(operations, demo.GetField("Operations").GetValue(null)));
            Pass("private/default/initializer/array/property/event/readonly collections; declaration limit=" + limit);
            demo.GetMethod("Touch").Invoke(null, null);
            EditorStaticReset.RestoreDeclaredValues();
            Equal(limit, demo.GetField("Limit").GetValue(null));
            Pass("registration persists across repeated Play resets");

            var generic = assembly.GetType("Generic`1");
            foreach (var argument in new[] { typeof(object), typeof(List<int>) })
            {
                generic.MakeGenericType(argument).GetField("Value").SetValue(null, 99);
            }

            EditorStaticReset.RestoreDeclaredValues();
            foreach (var argument in new[] { typeof(object), typeof(List<int>) })
            {
                Equal(5, generic.MakeGenericType(argument).GetField("Value").GetValue(null));
            }

            Pass("closed generic registrations retain separate state and constraints");
            var nested = assembly.GetType("Container`1+Nested").MakeGenericType(typeof(string));
            nested.GetMethod("Change").Invoke(null, null);
            EditorStaticReset.RestoreDeclaredValues();
            Equal(23, nested.GetMethod("Read").Invoke(null, null));
            Pass("private static state in a nested generic partial type");
            assembly.GetType("Ordinary").GetField("Value").SetValue(null, 99);
            EditorStaticReset.RestoreDeclaredValues();
            Equal(99, assembly.GetType("Ordinary").GetField("Value").GetValue(null));
            Pass("ordinary classes are outside the singleton reset scope");
        }

        var nullable = Compile(@"
#nullable enable
using System;
using System.Collections.Generic;
public partial class NullableState : Game.Framework.Singleton<NullableState>
{
    public static string?[]? Values = { null, ""ok"" };
    public static List<string?>? Items = new List<string?> { null, ""ok"" };
}");
        var nullableType = nullable.GetType("NullableState");
        nullableType.GetField("Values").SetValue(null, null);
        nullableType.GetField("Items").SetValue(null, null);
        EditorStaticReset.RestoreDeclaredValues();
        True(((string[])nullableType.GetField("Values").GetValue(null)).SequenceEqual(new string[] { null, "ok" }));
        Pass("nullable declarations preserve array element and reference annotations");

        var aliases = Compile(@"
using Alias = System.Int32;
namespace DemoNamespace
{
    public partial class Aliases : Game.Framework.Singleton<Aliases>
    {
        public static Alias First = 11;
    }
}", @"
using Alias = System.String;
namespace DemoNamespace
{
    public partial class Aliases
    {
        public static Alias Second = ""ok"";
    }
}");
        var aliasType = aliases.GetType("DemoNamespace.Aliases");
        aliasType.GetField("First").SetValue(null, 99);
        aliasType.GetField("Second").SetValue(null, "bad");
        EditorStaticReset.RestoreDeclaredValues();
        Equal(11, aliasType.GetField("First").GetValue(null));
        Equal("ok", aliasType.GetField("Second").GetValue(null));
        Pass("partial declarations preserve each file's using aliases");

        foreach (var item in new[]
        {
            ("public class Bad : Game.Framework.Singleton<Bad> { public static int Value = 1; }", "NVR001"),
            ("public partial class Bad : Game.Framework.Singleton<Bad> { static Bad() { Value = 2; } public static int Value = 1; }", "NVR002"),
            ("public partial class Bad : Game.Framework.Singleton<Bad> { public static readonly System.Text.StringBuilder Value = new System.Text.StringBuilder(); }", "NVR003"),
            ("public partial class Bad : Game.Framework.Singleton<Bad> { [System.ThreadStatic] public static int Value; }", "NVR003"),
            ("public partial class Bad : Game.Framework.Singleton<Bad> { public static int __NovorizonStaticResetValue; }", "NVR004"),
            ("public partial class A : Game.Framework.Singleton<A> { public static int Value = 1; } public partial class Bad : Game.Framework.Singleton<Bad> { public static int Value = A.Value; }", "NVR005"),
            ("public class Container { public partial class Bad : Game.Framework.Singleton<Bad> { public static int Value = 1; } }", "NVR001"),
            ("public partial class Bad : Game.Framework.Singleton<Bad> { public static event System.Action Changed { add { } remove { } } }", "NVR003")
        })
        {
            var generated = Generate(new[] { item.Item1 }, true);
            True(generated.Driver.GetRunResult().Diagnostics.Any(diagnostic => diagnostic.Id == item.Item2));
            Pass("unsupported declaration diagnosed as " + item.Item2);
        }

        var player = Generate(new[] { Fixture.Replace("LIMIT_VALUE", "1") }, false);
        True(!player.Driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Any());
        True(!player.Compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Pass("Player compilation emits no reset code or registry dependency");

        var late = Compile(@"
public partial class UsedLate : Game.Framework.Singleton<UsedLate>
{
    public static int Value = 42;
}");
        EditorStaticReset.RestoreDeclaredValues();
        var lateType = late.GetType("UsedLate");
        Equal(42, lateType.GetField("Value").GetValue(null));
        lateType.GetField("Value").SetValue(null, 99);
        EditorStaticReset.RestoreDeclaredValues();
        Equal(42, lateType.GetField("Value").GetValue(null));
        Pass("type first used after the startup reset joins subsequent resets");

        bool cleaned = false;
        var owned = Compile(@"
using System;
public partial class Owned : Game.Framework.Singleton<Owned>, IDisposable
{
    public static int Value = 1;
    public static Action Disposed;
    public void Dispose()
    {
        Disposed?.Invoke();
    }
}");
        var ownedType = owned.GetType("Owned");
        _ = ownedType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
        ownedType.GetField("Value").SetValue(null, 99);
        ownedType.GetField("Disposed").SetValue(null, (Action)(() =>
        {
            Equal(99, ownedType.GetField("Value").GetValue(null));
            cleaned = true;
        }));
        EditorStaticReset.RestoreDeclaredValues();
        True(cleaned);
        Equal(1, ownedType.GetField("Value").GetValue(null));
        Equal(null, ownedType.GetField("Disposed").GetValue(null));
        Pass("actual Framework instance cleanup runs before generated static state is cleared");
        Console.WriteLine("PASS " + passed + " static reset scenarios with Roslyn 3.8 and actual Framework Core; native Unity not executed.");
    }

    private static (CSharpCompilation Compilation, GeneratorDriver Driver) Generate(string[] sources, bool editor)
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: editor ? new[] { "UNITY_EDITOR" } : Array.Empty<string>());
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, parse, "Fixture" + index + ".cs"));
        var compilation = CSharpCompilation.Create("StaticResetFixture_" + Guid.NewGuid().ToString("N"), trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, generalDiagnosticOption: ReportDiagnostic.Error));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new SingletonStaticsGenerator() }, parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return ((CSharpCompilation)updated, driver);
    }

    private static Assembly Compile(params string[] sources)
    {
        var generated = Generate(sources, true);
        var errors = generated.Driver.GetRunResult().Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        True(errors.Length == 0, string.Join("\n", errors.Select(diagnostic => diagnostic.ToString())));
        using var bytes = new MemoryStream();
        var result = generated.Compilation.Emit(bytes);
        True(result.Success, string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes.ToArray()));
    }

    private static void True(bool value, string message = "Assertion failed")
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal(object expected, object actual)
    {
        True(Equals(expected, actual), "Expected " + expected + ", got " + actual);
    }

    private static void Pass(string description)
    {
        passed++;
        Console.WriteLine("PASS " + description);
    }
}
