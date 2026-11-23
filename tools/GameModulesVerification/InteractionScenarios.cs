using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Audio;
using Game.Framework.Data;
using Game.Framework.Data.Editor;
using Game.Framework.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using Rebinding = UnityEngine.InputSystem.InputActionRebindingExtensions;
using Object = UnityEngine.Object;

internal static partial class Program
{
    private static void InteractionScenarios()
    {
#if UNITY_EDITOR
        Run("generated static reset follows the current Domain Reload option", () =>
        {
            var reset = typeof(Game.Framework.MonoSingleton<>).Assembly.GetType("Game.Framework.UnityFrameworkBootstrap")
                .GetMethod("Reset", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            _ = StaticResetProbe.Value;
            int before = StaticResetCounter.Calls;
            try
            {
                UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = false;
                StaticResetProbe.Value = 99;
                reset.Invoke(null, null);
                Equal(99, StaticResetProbe.Value);
                Equal(before, StaticResetCounter.Calls);
                UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = true;
                UnityEditor.EditorSettings.enterPlayModeOptions = UnityEditor.EnterPlayModeOptions.DisableSceneReload;
                reset.Invoke(null, null);
                Equal(99, StaticResetProbe.Value);
                Equal(before, StaticResetCounter.Calls);
                UnityEditor.EditorSettings.enterPlayModeOptions = UnityEditor.EnterPlayModeOptions.DisableDomainReload;
                reset.Invoke(null, null);
                Equal(5, StaticResetProbe.Value);
                Equal(before + 1, StaticResetCounter.Calls);
                StaticResetProbe.Value = 98;
                UnityEditor.EditorSettings.enterPlayModeOptions |= UnityEditor.EnterPlayModeOptions.DisableSceneReload;
                reset.Invoke(null, null);
                Equal(5, StaticResetProbe.Value);
                Equal(before + 2, StaticResetCounter.Calls);
            }
            finally
            {
                UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = true;
                UnityEditor.EditorSettings.enterPlayModeOptions = UnityEditor.EnterPlayModeOptions.DisableDomainReload;
            }
        });
        Run("retained audio component keeps configuration while releasing playback", () =>
        {
            var audio = AudioManager.Instance;
            AudioManager.MasterVolume = .25f;
            AudioManager.MaxVoices = 7;
            AudioManager.AutomaticUpdate = false;
            AudioManager.UseUnscaledTime = false;
            var clip = new AudioClip();
            AudioManager.Play(clip);
            True(AudioManager.PlayingCount != 0);
            Object.ResetSingletonsForPlay();
            Game.Framework.UnityThread.Initialize();
            Same(audio, AudioManager.Instance);
            Near(.25f, AudioManager.MasterVolume);
            Equal(7, AudioManager.MaxVoices);
            True(!AudioManager.AutomaticUpdate && !AudioManager.UseUnscaledTime);
            Equal(0, AudioManager.PlayingCount);
        });
#endif
        Run("input supports the common default entry and its typed parameter overload", () =>
        {
            using var defaults = new InputManager();
            Game.Framework.Singleton<InputManager> common = defaults;
            common.Initialize();
            True(defaults.IsConfigured);
            using var configured = new InputManager();
            configured.Initialize(InputAsset(), "Gameplay");
            True(configured.FindActionInternal("Move").enabled);
        });
        Run("input clones assets and switches maps without changing project assets", () =>
        {
            using var input = new InputManager();
            var source = InputAsset();
            input.Initialize(source, "Gameplay");
            True(input.FindActionInternal("Move").enabled);
            True(!source.FindAction("Move").enabled);
            input.EnableMapInternal("UI");
            True(input.FindActionInternal("Submit").enabled && input.FindActionInternal("Move").enabled);
            input.SwitchMapInternal("UI");
            True(!input.FindActionInternal("Move").enabled && input.FindActionInternal("Submit").enabled);
            Throws<ArgumentException>(() => input.SwitchMapInternal("Missing"));
            True(input.FindActionInternal("Submit").enabled);
        });
        Run("input nested global and map blocks restore the latest requested state", () =>
        {
            using var input = new InputManager();
            input.Initialize(InputAsset(), "Gameplay");
            using var outer = input.BlockInternal();
            using var inner = input.BlockInternal();
            using var mapBlock = input.BlockInternal("Gameplay");
            input.EnableMapInternal("UI");
            outer.Dispose();
            True(!input.FindActionInternal("Submit").enabled);
            inner.Dispose();
            True(input.FindActionInternal("Submit").enabled && !input.FindActionInternal("Move").enabled);
            input.DisableMapInternal("Gameplay");
            mapBlock.Dispose();
            True(!input.FindActionInternal("Move").enabled);
            input.EnableMapInternal("Gameplay");
            True(input.FindActionInternal("Move").enabled);
        });
        Run("input subscriptions, control scheme and device filters are scoped to the clone", () =>
        {
            using var input = new InputManager();
            var source = InputAsset();
            input.Initialize(source, "Gameplay");
            int calls = 0;
            var sub = input.SubscribeInternal("Move", _ => calls++);
            input.FindActionInternal("Move").PerformForTest();
            sub.Dispose();
            input.FindActionInternal("Move").PerformForTest();
            Equal(1, calls);
            input.SetControlSchemeInternal("Keyboard");
            Equal("kb", input.ConfiguredActions.bindingMask.Value.groups);
            True(!source.bindingMask.HasValue);
            input.SetControlSchemeInternal(null);
            True(!input.ConfiguredActions.bindingMask.HasValue);
            var device = new InputDevice();
            input.SetDevicesInternal(device);
            Same(device, input.ConfiguredActions.devices.Value[0]);
            input.SetDevicesInternal(null);
            True(!input.ConfiguredActions.devices.HasValue);
            input.LoadBindingOverridesInternal("{\"test\":1}");
            Equal("{\"test\":1}", input.SaveBindingOverridesInternal());
        });
        Run("input rebinding completes, cancels and retains blocks while restoring actions", () =>
        {
            using var input = new InputManager();
            input.Initialize(InputAsset(), "Gameplay");
            var pending = input.RebindAsyncInternal("Move", 0);
            True(!input.FindActionInternal("Move").enabled);
            Fails<InvalidOperationException>(input.RebindAsyncInternal("Move", 0));
            Throws<InvalidOperationException>(() => input.LoadBindingOverridesInternal("{}"));
            var operation = Rebinding.LastForTest;
            using var block = input.BlockInternal();
            operation.CompleteForTest();
            Equal("{}", Complete(pending));
            True(operation.DisposedForTest && !input.FindActionInternal("Move").enabled);
            block.Dispose();
            True(input.FindActionInternal("Move").enabled);
            using var cancellation = new CancellationTokenSource();
            pending = input.RebindAsyncInternal("Move", 0, cancellation.Token);
            Complete(Task.Run(() => cancellation.Cancel()));
            Fails<OperationCanceledException>(pending);
            True(Rebinding.LastForTest.DisposedForTest && input.FindActionInternal("Move").enabled);
            Rebinding.FailCreationForTest = true;
            try
            {
                Fails<InvalidOperationException>(input.RebindAsyncInternal("Move", 0));
                True(input.FindActionInternal("Move").enabled);
            }
            finally
            {
                Rebinding.FailCreationForTest = false;
            }
        });
        Run("input dispose cancels rebinds and leaves old leases inactive", () =>
        {
            InputManager.Reset();
            var input = InputManager.Instance;
            input.Initialize(InputAsset(), "Gameplay");
            var lease = input.BlockInternal();
            var pending = input.RebindAsyncInternal("Move", 0);
            InputManager.Reset();
            Fails<OperationCanceledException>(pending);
            lease.Dispose();
            True(!InputManager.Initialized);
        });
        Run("audio fade uses unified time and respects every pause source", () =>
        {
            var audio = AudioManager.Instance;
            var voice = AudioManager.Play(new AudioClip(), loop: true);
            voice.FadeTo(0, 1);
            AudioManager.Tick(.25f);
            Near(.75f, Sources().Single().volume);
            voice.Pause();
            AudioManager.Tick(.25f);
            Near(.75f, Sources().Single().volume);
            voice.Resume();
            AudioManager.PauseAll();
            AudioManager.Tick(.25f);
            AudioManager.ResumeAll();
            AudioListener.pause = true;
            AudioManager.Tick(.25f);
            AudioListener.pause = false;
            AudioManager.Tick(.25f);
            Near(.5f, Sources().Single().volume);
            voice.SetVolume(.8f);
            AudioManager.Tick(1);
            Near(.8f, Sources().Single().volume);
            voice.FadeOut(.5f);
            AudioManager.Tick(.5f);
            True(!voice.IsValid && AudioManager.PooledCount == 1);
        });
        Run("audio music crossfade has equal power, capacity failure preserves current music", () =>
        {
            var audio = AudioManager.Instance;
            var first = AudioManager.PlayMusic(new AudioClip(), 0);
            var clip = new AudioClip();
            var second = AudioManager.PlayMusic(clip, 1);
            AudioManager.Tick(.5f);
            var active = Sources().Where(source => source.clip != null).ToArray();
            Near(1, active.Sum(source => source.volume * source.volume));
            AudioManager.MaxVoices = 2;
            Throws<InvalidOperationException>(() => AudioManager.PlayMusic(new AudioClip(), 1));
            Equal(second, AudioManager.CurrentMusic);
            AudioManager.Tick(.5f);
            True(!first.IsValid && second.IsValid);
            Equal(second, AudioManager.PlayMusic(clip, 1));
            AudioManager.StopMusic(.5f);
            AudioManager.Tick(.5f);
            True(!second.IsValid && !AudioManager.CurrentMusic.IsValid);
        });
        Run("audio mixer routing updates playing voices and pool reuse resets fade and callback", () =>
        {
            var audio = AudioManager.Instance;
            var mixer = new UnityEngine.Audio.AudioMixerGroup();
            int completed = 0;
            var handle = AudioManager.Play(new AudioClip(), completed: _ => completed++);
            AudioManager.SetMixerGroup(AudioGroup.Sound, mixer);
            Same(mixer, Sources().Single().outputAudioMixerGroup);
            handle.FadeOut(.1f);
            AudioManager.Tick(.1f);
            Equal(0, completed);
            handle = AudioManager.Play(new AudioClip(), AudioGroup.UI, loop: true);
            True(Sources().Single().outputAudioMixerGroup == null);
            AudioManager.Tick(1);
            True(handle.IsValid);
            Near(1, Sources().Single().volume);
            Throws<ArgumentOutOfRangeException>(() => AudioManager.Tick(float.NaN));
            Throws<ArgumentOutOfRangeException>(() => handle.FadeTo(1, -1));
        });
        Run("audio warmed steady tick, fade and voice reuse avoid managed allocations", () =>
        {
            var audio = AudioManager.Instance;
            var clip = new AudioClip();
            var handle = AudioManager.Play(clip, loop: true);
            handle.FadeTo(.3f, 10);
            for (int i = 0; i < 100; i++)
            {
                AudioManager.Tick(.001f);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                AudioManager.Tick(.001f);
            }

            Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            handle.Stop();
            handle = AudioManager.Play(clip);
            handle.Stop();
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                AudioManager.Play(clip).Stop();
            }

            Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        });
        Run("audio waits for background clip data before advancing fades or recycling", () =>
        {
            var audio = AudioManager.Instance;
            var clip = new AudioClip
            {
                loadState = AudioDataLoadState.Loading
            };
            var handle = AudioManager.Play(clip, volume: 0);
            handle.FadeTo(1, 1);
            Sources().Single().Stop();
            AudioManager.Tick(1);
            True(handle.IsValid);
            Near(0, Sources().Single().volume);
            clip.loadState = AudioDataLoadState.Loaded;
            Sources().Single().Play();
            AudioManager.Tick(.5f);
            Near(.5f, Sources().Single().volume);
        });
        Run("real Luban Excel generated C# reads the official binary runtime", () =>
        {
            InitializeLubanData();
            var tables = Complete(DataManager.LoadAsync(loader => new cfg.Tables(loader)));
            Equal("示例配置", tables.TbExample[1].Name);
            Equal("Second entry", tables.TbExample[2].Name);
            Same(tables, DataManager.Get<cfg.Tables>());
            Equal(1, DataManager.Count);
        });
        Run("Luban corrupt data and failed reload retain the previous complete snapshot", () =>
        {
            bool fail = false;
            DataManager.Initialize((location, token) => Task.FromResult(IsManifest(location) ? ManifestBytes() : fail ? Array.Empty<byte>() : File.ReadAllBytes(Fixture("data/tbexample.bytes"))));
            var previous = Complete(DataManager.LoadAsync(loader => new cfg.Tables(loader)));
            fail = true;
            Throws<Exception>(() => Complete(DataManager.LoadAsync(loader => new cfg.Tables(loader))));
            Same(previous, DataManager.Get<cfg.Tables>());
        });
        Run("Luban rejects invalid manifests and missing table mappings before publishing", () =>
        {
            string manifest = "{}";
            int binaryCalls = 0;
            DataManager.Initialize((location, token) =>
            {
                if (IsManifest(location))
                {
                    return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(manifest));
                }

                binaryCalls++;
                return Task.FromResult(Array.Empty<byte>());
            });
            Fails<ArgumentException>(DataManager.LoadAsync(loader => new cfg.Tables(loader)));
            Equal(0, binaryCalls);
            manifest = "{\"version\":1,\"format\":\"bin\",\"files\":[{\"name\":\"x\",\"location\":\"a\"},{\"name\":\"x\",\"location\":\"b\"}]}";
            Fails<ArgumentException>(DataManager.LoadAsync(loader => new cfg.Tables(loader)));
            Equal(0, binaryCalls);
            manifest = "{\"version\":1,\"format\":\"bin\",\"files\":[]}";
            Fails<System.Collections.Generic.KeyNotFoundException>(DataManager.LoadAsync(loader => new cfg.Tables(loader)));
            Equal(0, DataManager.Count);
        });
        Run("Luban concurrent loads, clear and cancellation cannot publish stale snapshots", () =>
        {
            var pendingBytes = Gate<byte[]>();
            DataManager.Initialize((location, token) => IsManifest(location) ? Task.FromResult(ManifestBytes()) : pendingBytes.Task);
            var stale = DataManager.LoadAsync(loader => new cfg.Tables(loader));
            var latest = DataManager.LoadAsync(loader => new cfg.Tables(loader));
            pendingBytes.SetResult(File.ReadAllBytes(Fixture("data/tbexample.bytes")));
            Fails<OperationCanceledException>(stale);
            var tables = Complete(latest);
            Same(tables, DataManager.Get<cfg.Tables>());
            pendingBytes = Gate<byte[]>();
            stale = DataManager.LoadAsync(loader => new cfg.Tables(loader));
            DataManager.Clear();
            pendingBytes.SetResult(File.ReadAllBytes(Fixture("data/tbexample.bytes")));
            Fails<OperationCanceledException>(stale);
            using var canceled = new CancellationTokenSource();
            pendingBytes = Gate<byte[]>();
            stale = DataManager.LoadAsync(loader => new cfg.Tables(loader), token: canceled.Token);
            canceled.Cancel();
            Fails<OperationCanceledException>(stale);
            pendingBytes.SetResult(Array.Empty<byte>());
            Equal(0, DataManager.Count);
        });
        Run("Luban load constructs the supplied Tables directly without registration", () =>
        {
            InitializeLubanData();
            int constructions = 0;
            True(!DataManager.TryGet<cfg.Tables>(out _));
            Throws<System.Collections.Generic.KeyNotFoundException>(() => DataManager.Get<cfg.Tables>());
            var first = Complete(DataManager.LoadAsync(reader =>
            {
                constructions++;
                return new cfg.Tables(reader);
            }));
            Equal(1, constructions);
            DataManager.Clear();
            Equal(0, DataManager.Count);
            var next = Complete(DataManager.LoadAsync(reader => new cfg.Tables(reader)));
            True(!ReferenceEquals(first, next));
            Same(next, DataManager.Get<cfg.Tables>());
        });
        Run("Luban default loader reads both manifest and table bytes from Resources", () =>
        {
            DataManager.Initialize();
            Resources.CompleteSynchronouslyForTest = true;
            Resources.RegisterAssetForTest("Config/Luban/manifest", new TextAsset { bytes = ManifestBytes() });
            Resources.RegisterAssetForTest("Config/Luban/tbexample", new TextAsset { bytes = File.ReadAllBytes(Fixture("data/tbexample.bytes")) });
            var loaded = Complete(DataManager.LoadAsync(reader => new cfg.Tables(reader)));
            Equal("示例配置", loaded.TbExample[1].Name);
            Fails<InvalidOperationException>(DataManager.LoadAsync(reader => new cfg.Tables(reader), "missing"));
            Same(loaded, DataManager.Get<cfg.Tables>());
        });
        Run("Luban Reset cancels a blocked loader and late bytes cannot replace the new snapshot", () =>
        {
            var pending = Gate<byte[]>();
            DataManager.Initialize((location, token) => IsManifest(location) ? Task.FromResult(ManifestBytes()) : pending.Task);
            var loading = DataManager.LoadAsync(reader => new cfg.Tables(reader));
            DataManager.Reset();
            Fails<OperationCanceledException>(loading);
            True(!DataManager.TryGet<cfg.Tables>(out _));
            InitializeLubanData();
            var fresh = Complete(DataManager.LoadAsync(reader => new cfg.Tables(reader)));
            pending.SetResult(File.ReadAllBytes(Fixture("data/tbexample.bytes")));
            context.Drain();
            Same(fresh, DataManager.Get<cfg.Tables>());
        });
        Run("Luban rejects missing factories, bad locations and use before initialization", () =>
        {
            Fails<InvalidOperationException>(DataManager.LoadAsync(reader => new cfg.Tables(reader)));
            DataManager.Initialize((location, token) => Task.FromResult(ManifestBytes()));
            Fails<ArgumentNullException>(DataManager.LoadAsync<cfg.Tables>(null));
            Fails<ArgumentException>(DataManager.LoadAsync(reader => new cfg.Tables(reader), " "));
        });
        Run("Luban null factories and failed construction retain the prior snapshot", () =>
        {
            InitializeLubanData();
            var previous = Complete(DataManager.LoadAsync(reader => new cfg.Tables(reader)));
            Fails<InvalidOperationException>(DataManager.LoadAsync<cfg.Tables>(_ => null));
            Same(previous, DataManager.Get<cfg.Tables>());
            Fails<InvalidOperationException>(DataManager.LoadAsync<cfg.Tables>(_ => throw new InvalidOperationException("factory")));
            Same(previous, DataManager.Get<cfg.Tables>());
        });
        Run("Luban reset clears tables and allows a new loader", () =>
        {
            InitializeLubanData();
            Complete(DataManager.LoadAsync(reader => new cfg.Tables(reader)));
            DataManager.Reset();
            True(!DataManager.Initialized);
            Equal(0, DataManager.Count);
            InitializeLubanData();
            True(DataManager.Initialized);
            True(Complete(DataManager.LoadAsync(reader => new cfg.Tables(reader))) != null);
        });
        Run("Luban exporter invokes real CLI in paths with spaces and retains last good outputs on failure", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "Luban project " + Guid.NewGuid().ToString("N"));
            string template = Path.GetFullPath("Packages/com.novorizon.data/Templates~/LubanProject");
            string source = Path.Combine(root, "Config/Luban");
            try
            {
                foreach (string file in Directory.GetFiles(template, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(source, file.Substring(template.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target);
                }

                Task Generate() => LubanExporter.GenerateAsync(root, "Config/Luban/luban.conf", "client", "Assets/Generated/Config", "Assets/Resources/Config/Luban", "Config/Luban", Environment.GetEnvironmentVariable("NOVORIZON_DOTNET"), Environment.GetEnvironmentVariable("LUBAN_CLI"));
                Complete(Generate());
                string dataPath = Path.Combine(root, "Assets/Resources/Config/Luban/tbexample.bytes");
                byte[] good = File.ReadAllBytes(dataPath);
                string generated = Path.Combine(root, "Assets/Generated/Config/Tables.cs");
                File.WriteAllText(generated + ".meta", "guid: retained");
                Complete(Generate());
                Equal("guid: retained", File.ReadAllText(generated + ".meta"));
                True(File.ReadAllText(Path.Combine(root, "Assets/Resources/Config/Luban/manifest.json")).Contains("Config/Luban/tbexample"));
                // Fail the second publication after the first output was replaced, then require rollback.
                string codeBefore = File.ReadAllText(generated);
                File.WriteAllText(Path.Combine(root, "Assets/blocked"), "not a directory");
                Fails<IOException>(LubanExporter.GenerateAsync(root, "Config/Luban/luban.conf", "client", "Assets/Generated/Config", "Assets/blocked/Data", "Config/Luban", Environment.GetEnvironmentVariable("NOVORIZON_DOTNET"), Environment.GetEnvironmentVariable("LUBAN_CLI")));
                Equal(codeBefore, File.ReadAllText(generated));
                Equal("guid: retained", File.ReadAllText(generated + ".meta"));
                True(good.SequenceEqual(File.ReadAllBytes(dataPath)));
                File.WriteAllText(Path.Combine(source, "Data/example.xlsx"), "invalid workbook");
                Fails<InvalidOperationException>(Generate());
                True(good.SequenceEqual(File.ReadAllBytes(dataPath)));
                Equal("guid: retained", File.ReadAllText(generated + ".meta"));
                string unowned = Path.Combine(root, "Assets/Unowned");
                Directory.CreateDirectory(unowned);
                File.WriteAllText(Path.Combine(unowned, "handwritten.cs"), "user file");
                Fails<InvalidOperationException>(LubanExporter.GenerateAsync(root, "Config/Luban/luban.conf", "client", "Assets/Unowned", "Assets/Resources/Config/Luban", "Config/Luban", Environment.GetEnvironmentVariable("NOVORIZON_DOTNET"), Environment.GetEnvironmentVariable("LUBAN_CLI")));
                True(File.Exists(Path.Combine(unowned, "handwritten.cs")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    private static void Near(float expected, float actual) => True(Math.Abs(expected - actual) < .0001f);
    private static string Fixture(string relative) => Path.Combine(Environment.GetEnvironmentVariable("LubanFixtureRoot"), relative);
    private static bool IsManifest(string location) => location.EndsWith("manifest", StringComparison.Ordinal);
    private static byte[] ManifestBytes() => System.Text.Encoding.UTF8.GetBytes(Manifest());
    private static string Manifest() => "{\"version\":1,\"format\":\"bin\",\"files\":[{\"name\":\"tbexample\",\"location\":\"Config/Luban/tbexample\"}]}";
    private static void InitializeLubanData()
    {
        DataManager.Initialize((location, token) => Task.FromResult(IsManifest(location) ? ManifestBytes() : File.ReadAllBytes(Fixture("data/tbexample.bytes"))));
    }

    private static InputActionAsset InputAsset()
    {
        var source = ScriptableObject.CreateInstance<InputActionAsset>();
        var gameplay = new InputActionMap
        {
            name = "Gameplay"
        };
        var ui = new InputActionMap
        {
            name = "UI"
        };
        gameplay.ActionsForTest.Add(new InputAction { name = "Move", actionMap = gameplay, bindings = new ReadOnlyArray<InputBinding>(new[] { new InputBinding() }) });
        ui.ActionsForTest.Add(new InputAction { name = "Submit", actionMap = ui });
        source.actionMaps = new ReadOnlyArray<InputActionMap>(new[] { gameplay, ui });
        source.controlSchemes = new ReadOnlyArray<InputControlScheme>(new[] { new InputControlScheme { name = "Keyboard", bindingGroup = "kb" } });
        return source;
    }
#if UNITY_EDITOR
    private static class StaticResetCounter
    {
        internal static int Calls;
        internal static int Next()
        {
            Calls++;
            return 5;
        }
    }

    public sealed partial class StaticResetProbe : Game.Framework.Singleton<StaticResetProbe>
    {
        public static int Value = StaticResetCounter.Next();
    }
#endif

}
