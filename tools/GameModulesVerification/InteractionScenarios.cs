using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Audio;
using Game.Framework.Config;
using Game.Framework.Config.Editor;
using Game.Framework.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using Rebinding = UnityEngine.InputSystem.InputActionRebindingExtensions;

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
            audio.MasterVolume = .25f;
            audio.MaxVoices = 7;
            audio.AutomaticUpdate = false;
            audio.UseUnscaledTime = false;
            var clip = new AudioClip();
            audio.Play(clip);
            True(audio.PlayingCount != 0);
            Game.Framework.SingletonLifecycle.Reset();
            Game.Framework.EditorStaticReset.RestoreDeclaredValues();
            Game.Framework.UnityThread.Initialize();
            Same(audio, AudioManager.Instance);
            Near(.25f, audio.MasterVolume);
            Equal(7, audio.MaxVoices);
            True(!audio.AutomaticUpdate && !audio.UseUnscaledTime);
            Equal(0, audio.PlayingCount);
        });
#endif
        Run("input clones assets and switches maps without changing project assets", () =>
        {
            using var input = new InputManager();
            var source = InputAsset();
            input.Initialize(source, "Gameplay");
            True(input.FindAction("Move").enabled);
            True(!source.FindAction("Move").enabled);
            input.EnableMap("UI");
            True(input.FindAction("Submit").enabled && input.FindAction("Move").enabled);
            input.SwitchMap("UI");
            True(!input.FindAction("Move").enabled && input.FindAction("Submit").enabled);
            Throws<ArgumentException>(() => input.SwitchMap("Missing"));
            True(input.FindAction("Submit").enabled);
        });
        Run("input nested global and map blocks restore the latest requested state", () =>
        {
            using var input = new InputManager();
            input.Initialize(InputAsset(), "Gameplay");
            using var outer = input.Block();
            using var inner = input.Block();
            using var mapBlock = input.Block("Gameplay");
            input.EnableMap("UI");
            outer.Dispose();
            True(!input.FindAction("Submit").enabled);
            inner.Dispose();
            True(input.FindAction("Submit").enabled && !input.FindAction("Move").enabled);
            input.DisableMap("Gameplay");
            mapBlock.Dispose();
            True(!input.FindAction("Move").enabled);
            input.EnableMap("Gameplay");
            True(input.FindAction("Move").enabled);
        });
        Run("input subscriptions, control scheme and device filters are scoped to the clone", () =>
        {
            using var input = new InputManager();
            var source = InputAsset();
            input.Initialize(source, "Gameplay");
            int calls = 0;
            var sub = input.Subscribe("Move", _ => calls++);
            input.FindAction("Move").PerformForTest();
            sub.Dispose();
            input.FindAction("Move").PerformForTest();
            Equal(1, calls);
            input.SetControlScheme("Keyboard");
            Equal("kb", input.Actions.bindingMask.Value.groups);
            True(!source.bindingMask.HasValue);
            input.SetControlScheme(null);
            True(!input.Actions.bindingMask.HasValue);
            var device = new InputDevice();
            input.SetDevices(device);
            Same(device, input.Actions.devices.Value[0]);
            input.SetDevices(null);
            True(!input.Actions.devices.HasValue);
            input.LoadBindingOverrides("{\"test\":1}");
            Equal("{\"test\":1}", input.SaveBindingOverrides());
        });
        Run("input rebinding completes, cancels and retains blocks while restoring actions", () =>
        {
            using var input = new InputManager();
            input.Initialize(InputAsset(), "Gameplay");
            var pending = input.RebindAsync("Move", 0);
            True(!input.FindAction("Move").enabled);
            Fails<InvalidOperationException>(input.RebindAsync("Move", 0));
            Throws<InvalidOperationException>(() => input.LoadBindingOverrides("{}"));
            var operation = Rebinding.LastForTest;
            using var block = input.Block();
            operation.CompleteForTest();
            Equal("{}", Complete(pending));
            True(operation.DisposedForTest && !input.FindAction("Move").enabled);
            block.Dispose();
            True(input.FindAction("Move").enabled);
            using var cancellation = new CancellationTokenSource();
            pending = input.RebindAsync("Move", 0, cancellation.Token);
            Complete(Task.Run(() => cancellation.Cancel()));
            Fails<OperationCanceledException>(pending);
            True(Rebinding.LastForTest.DisposedForTest && input.FindAction("Move").enabled);
            Rebinding.FailCreationForTest = true;
            try
            {
                Fails<InvalidOperationException>(input.RebindAsync("Move", 0));
                True(input.FindAction("Move").enabled);
            }
            finally
            {
                Rebinding.FailCreationForTest = false;
            }
        });
        Run("input dispose cancels rebinds and leases cannot resurrect a replaced singleton", () =>
        {
            InputManager.Reset();
            var input = InputManager.Instance;
            input.Initialize(InputAsset(), "Gameplay");
            var lease = input.Block();
            var pending = input.RebindAsync("Move", 0);
            InputManager.Reset();
            Fails<OperationCanceledException>(pending);
            lease.Dispose();
            var next = InputManager.Instance;
            True(!ReferenceEquals(input, next) && !next.Initialized);
            next.Initialize();
            InputManager.Reset(true);
            Throws<InvalidOperationException>(() =>
            {
                _ = InputManager.Instance;
            });
            InputManager.Reset();
        });
        Run("audio fade uses unified time and respects every pause source", () =>
        {
            var audio = AudioManager.Instance;
            var voice = audio.Play(new AudioClip(), loop: true);
            voice.FadeTo(0, 1);
            audio.Tick(.25f);
            Near(.75f, Sources().Single().volume);
            voice.Pause();
            audio.Tick(.25f);
            Near(.75f, Sources().Single().volume);
            voice.Resume();
            audio.PauseAll();
            audio.Tick(.25f);
            audio.ResumeAll();
            AudioListener.pause = true;
            audio.Tick(.25f);
            AudioListener.pause = false;
            audio.Tick(.25f);
            Near(.5f, Sources().Single().volume);
            voice.SetVolume(.8f);
            audio.Tick(1);
            Near(.8f, Sources().Single().volume);
            voice.FadeOut(.5f);
            audio.Tick(.5f);
            True(!voice.IsValid && audio.PooledCount == 1);
        });
        Run("audio music crossfade has equal power, capacity failure preserves current music", () =>
        {
            var audio = AudioManager.Instance;
            var first = audio.PlayMusic(new AudioClip(), 0);
            var clip = new AudioClip();
            var second = audio.PlayMusic(clip, 1);
            audio.Tick(.5f);
            var active = Sources().Where(source => source.clip != null).ToArray();
            Near(1, active.Sum(source => source.volume * source.volume));
            audio.MaxVoices = 2;
            Throws<InvalidOperationException>(() => audio.PlayMusic(new AudioClip(), 1));
            Equal(second, audio.CurrentMusic);
            audio.Tick(.5f);
            True(!first.IsValid && second.IsValid);
            Equal(second, audio.PlayMusic(clip, 1));
            audio.StopMusic(.5f);
            audio.Tick(.5f);
            True(!second.IsValid && !audio.CurrentMusic.IsValid);
        });
        Run("audio mixer routing updates playing voices and pool reuse resets fade and callback", () =>
        {
            var audio = AudioManager.Instance;
            var mixer = new UnityEngine.Audio.AudioMixerGroup();
            int completed = 0;
            var handle = audio.Play(new AudioClip(), completed: _ => completed++);
            audio.SetMixerGroup(AudioGroup.Sound, mixer);
            Same(mixer, Sources().Single().outputAudioMixerGroup);
            handle.FadeOut(.1f);
            audio.Tick(.1f);
            Equal(0, completed);
            handle = audio.Play(new AudioClip(), AudioGroup.UI, loop: true);
            True(Sources().Single().outputAudioMixerGroup == null);
            audio.Tick(1);
            True(handle.IsValid);
            Near(1, Sources().Single().volume);
            Throws<ArgumentOutOfRangeException>(() => audio.Tick(float.NaN));
            Throws<ArgumentOutOfRangeException>(() => handle.FadeTo(1, -1));
        });
        Run("audio warmed steady tick, fade and voice reuse avoid managed allocations", () =>
        {
            var audio = AudioManager.Instance;
            var clip = new AudioClip();
            var handle = audio.Play(clip, loop: true);
            handle.FadeTo(.3f, 10);
            for (int i = 0; i < 100; i++)
            {
                audio.Tick(.001f);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                audio.Tick(.001f);
            }

            Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            handle.Stop();
            handle = audio.Play(clip);
            handle.Stop();
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                audio.Play(clip).Stop();
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
            var handle = audio.Play(clip, volume: 0);
            handle.FadeTo(1, 1);
            Sources().Single().Stop();
            audio.Tick(1);
            True(handle.IsValid);
            Near(0, Sources().Single().volume);
            clip.loadState = AudioDataLoadState.Loaded;
            Sources().Single().Play();
            audio.Tick(.5f);
            Near(.5f, Sources().Single().volume);
        });
        Run("real Luban Excel generated C# reads the official binary runtime", () =>
        {
            using var config = LubanConfig();
            var tables = Complete(config.LoadLubanAsync(loader => new cfg.Tables(loader)));
            Equal("示例配置", tables.TbExample[1].Name);
            Equal("Second entry", tables.TbExample[2].Name);
            Same(tables, config.GetLuban<cfg.Tables>());
            Equal(1, config.Count);
        });
        Run("Luban corrupt data and failed reload retain the previous complete snapshot", () =>
        {
            using var config = new ConfigManager();
            bool fail = false;
            config.Initialize((_, __) => Task.FromResult(Manifest()), (_, __) => Task.FromResult(fail ? Array.Empty<byte>() : File.ReadAllBytes(Fixture("data/tbexample.bytes"))));
            var previous = Complete(config.LoadLubanAsync(loader => new cfg.Tables(loader)));
            fail = true;
            Throws<Exception>(() => Complete(config.LoadLubanAsync(loader => new cfg.Tables(loader))));
            Same(previous, config.GetLuban<cfg.Tables>());
        });
        Run("Luban rejects invalid manifests and missing table mappings before publishing", () =>
        {
            using var config = new ConfigManager();
            string manifest = "{}";
            int binaryCalls = 0;
            config.Initialize((_, __) => Task.FromResult(manifest), (_, __) =>
            {
                binaryCalls++;
                return Task.FromResult(Array.Empty<byte>());
            });
            Fails<ArgumentException>(config.LoadLubanAsync(loader => new cfg.Tables(loader)));
            Equal(0, binaryCalls);
            manifest = "{\"version\":1,\"format\":\"bin\",\"files\":[{\"name\":\"x\",\"location\":\"a\"},{\"name\":\"x\",\"location\":\"b\"}]}";
            Fails<ArgumentException>(config.LoadLubanAsync(loader => new cfg.Tables(loader)));
            Equal(0, binaryCalls);
            manifest = "{\"version\":1,\"format\":\"bin\",\"files\":[]}";
            Fails<System.Collections.Generic.KeyNotFoundException>(config.LoadLubanAsync(loader => new cfg.Tables(loader)));
            Equal(0, config.Count);
        });
        Run("Luban concurrent loads, clear and cancellation cannot publish stale snapshots", () =>
        {
            using var config = new ConfigManager();
            var pendingBytes = Gate<byte[]>();
            config.Initialize((_, __) => Task.FromResult(Manifest()), (_, __) => pendingBytes.Task);
            var stale = config.LoadLubanAsync(loader => new cfg.Tables(loader));
            var latest = config.LoadLubanAsync(loader => new cfg.Tables(loader));
            pendingBytes.SetResult(File.ReadAllBytes(Fixture("data/tbexample.bytes")));
            Fails<OperationCanceledException>(stale);
            var tables = Complete(latest);
            Same(tables, config.GetLuban<cfg.Tables>());
            pendingBytes = Gate<byte[]>();
            stale = config.LoadLubanAsync(loader => new cfg.Tables(loader));
            config.Clear();
            pendingBytes.SetResult(File.ReadAllBytes(Fixture("data/tbexample.bytes")));
            Fails<OperationCanceledException>(stale);
            using var canceled = new CancellationTokenSource();
            pendingBytes = Gate<byte[]>();
            stale = config.LoadLubanAsync(loader => new cfg.Tables(loader), token: canceled.Token);
            canceled.Cancel();
            Fails<OperationCanceledException>(stale);
            pendingBytes.SetResult(Array.Empty<byte>());
            Equal(0, config.Count);
        });
        Run("Luban exporter invokes real CLI in paths with spaces and retains last good outputs on failure", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "Luban project " + Guid.NewGuid().ToString("N"));
            string template = Path.GetFullPath("Packages/com.novorizon.config/Templates~/LubanProject");
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
    private static string Manifest() => "{\"version\":1,\"format\":\"bin\",\"files\":[{\"name\":\"tbexample\",\"location\":\"Config/Luban/tbexample\"}]}";
    private static ConfigManager LubanConfig()
    {
        var config = new ConfigManager();
        config.Initialize((_, __) => Task.FromResult(Manifest()), (_, __) => Task.FromResult(File.ReadAllBytes(Fixture("data/tbexample.bytes"))));
        return config;
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
