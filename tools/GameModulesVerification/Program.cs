using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Audio;
using Game.Framework.Data;
using Game.Framework.Input;
using Game.Framework.Storage;
using Game.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using Object = UnityEngine.Object;
using Scenes = Game.Framework.Scenes.SceneManager;
using NativeScenes = UnityEngine.SceneManagement.SceneManager;

internal static partial class Program
{
    private static int passed;
    private static Pump context;
    private static void Main()
    {
        ResourcePackageScenarios();
        Run("pinned resource versions skip latest lookup and complete downloads before readiness", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            package.Downloader.TotalDownloadCount = 3;
            package.Downloader.TotalDownloadBytes = 100;
            True(Complete(resources.InitializeAsync(new ResourceInitializationOptions
            {
                PackageVersion = "release-2",
                DownloadAll = true
            })));
            Equal(0, package.VersionRequests);
            Equal("release-2", package.SelectedVersion);
            Equal("release-2", resources.PackageVersion);
            Equal(1, package.Downloader.Begun);
            True(resources.Initialized);
        });
        Run("cancelled resource downloads never publish an initialized package", () =>
        {
            var resources = ResourceManager.Instance;
            var pending = Gate<bool>();
            YooAssets.Package.Downloader.Task = pending.Task;
            using var stop = new CancellationTokenSource();
            var loading = resources.InitializeAsync(new ResourceInitializationOptions
            {
                PackageVersion = "release-3",
                DownloadAll = true
            }, stop.Token);
            stop.Cancel();
            Fails<OperationCanceledException>(loading);
            True(!resources.Initialized);
            Equal(1, YooAssets.Package.Downloader.Cancelled);
            pending.SetResult(true);
        });
        Run("shutdown waits for nonabortable native initialization after caller cancellation", () =>
        {
            var resources = ResourceManager.Instance;
            var pending = Gate<bool>();
            YooAssets.Package.InitializeGate = pending.Task;
            using var stop = new CancellationTokenSource();
            var loading = resources.InitializeAsync(new ResourceInitializationOptions
            {
                PackageVersion = "release-4"
            }, stop.Token);
            stop.Cancel();
            Fails<OperationCanceledException>(loading);
            var shutdown = resources.ShutdownAsync();
            True(!shutdown.IsCompleted);
            Equal(1, YooAssets.Package.InitializeCalls);
            pending.SetResult(true);
            Complete(shutdown);
            True(!resources.Initialized);
        });
        Run("parameterless initialization dispatches through singleton bases to actual services", () =>
        {
            using var data = new DataManager();
            ConfigurableSingleton<DataManager> commonData = data;
            commonData.Initialize();
            True(data.Initialized);
            using var scenes = new Scenes();
            ConfigurableSingleton<Scenes> commonScenes = scenes;
            commonScenes.Initialize();
            True(scenes.Initialized);
        });
        Run("asynchronous resource loading cannot be skipped through the synchronous base entry", () =>
        {
            Throws<InvalidOperationException>(() => ResourceManager.Instance.Initialize());
            True(!ResourceManager.Instance.Initialized);
        });
        Run("storage writes real files, retains a backup and recovers a missing main file", () =>
        {
            using var storage = new StorageFixture();
            storage.Manager.SaveText("saves/profile.txt", "first");
            storage.Manager.SaveText("saves/profile.txt", "第二次");
            Equal("第二次", storage.Manager.LoadText("saves/profile.txt"));
            File.Delete(Path.Combine(storage.Root, "saves/profile.txt"));
            Equal("first", storage.Manager.LoadText("saves/profile.txt"));
            storage.Manager.Delete("saves/profile.txt");
            True(!storage.Manager.Exists("saves/profile.txt"));
            Equal(0, Directory.GetFiles(storage.Root, "*.tmp-*", SearchOption.AllDirectories).Length);
        });
        Run("storage rejects traversal and transaction-file aliases", () =>
        {
            using var storage = new StorageFixture();
            foreach (string key in new[]
            {
                "../escape",
                "a/../b",
                "a\\..\\b",
                "/root",
                "C:\\root",
                "a//b",
                "a.bak",
                "a.tmp-write",
                "file.",
                "file ",
                "a\0b"
            }

            )
            {
                Throws<ArgumentException>(() => storage.Manager.SaveText(key, "invalid"));
            }

            storage.Manager.SaveBytes("bytes.bin", new byte[] { 0, 1, 255 });
            Equal("0,1,255", string.Join(",", storage.Manager.LoadBytes("bytes.bin")));
        });
        Run("storage JSON migration is explicit and never overwrites the original", () =>
        {
            using var storage = new StorageFixture();
            storage.Manager.SaveJson("settings.json", new Row { id = 1 }, 1);
            string original = storage.Manager.LoadText("settings.json");
            var row = storage.Manager.LoadJson<Row>("settings.json", 2, (version, json) =>
            {
                Equal(1, version);
                return "{\"id\":2}";
            });
            Equal(2, row.id);
            Equal(original, storage.Manager.LoadText("settings.json"));
            Throws<InvalidDataException>(() => storage.Manager.LoadJson<Row>("settings.json", 2));
            Throws<ArgumentNullException>(() => storage.Manager.SaveJson<Row>("settings.json", null));
            Equal(original, storage.Manager.LoadText("settings.json"));
        });
        Run("storage detects invalid and future records and refuses root changes", () =>
        {
            using var storage = new StorageFixture();
            storage.Manager.SaveText("bad.json", "{}");
            Throws<InvalidDataException>(() => storage.Manager.LoadJson<Row>("bad.json"));
            storage.Manager.SaveJson("future.json", new Row(), 3);
            Throws<InvalidDataException>(() => storage.Manager.LoadJson<Row>("future.json", 2, (_, json) => json));
            Throws<InvalidOperationException>(() => storage.Manager.Initialize(storage.Root + "other"));
            storage.Manager.Initialize(storage.Root);
        });
        Run("build scene loading, activation and external unload keep ownership synchronized", () =>
        {
            using var scenes = new Scenes();
            scenes.Initialize();
            var loaded = Complete(scenes.LoadAsync("World", LoadSceneMode.Additive));
            Equal(loaded.handle, NativeScenes.ActiveForTest.handle);
            Equal(1, scenes.LoadedCount);
            NativeScenes.UnloadSceneAsync(loaded);
            Equal(0, scenes.LoadedCount);
        });
        Run("scene cancellation does not overlap an already started native load", () =>
        {
            using var scenes = new Scenes();
            var loader = new ControlledScenes();
            scenes.Initialize(loader);
            using var cancellation = new CancellationTokenSource();
            var first = scenes.LoadAsync("one", LoadSceneMode.Additive, token: cancellation.Token);
            var second = scenes.LoadAsync("two", LoadSceneMode.Additive);
            Equal(1, loader.Calls);
            cancellation.Cancel();
            Fails<OperationCanceledException>(first);
            Equal(1, loader.Calls);
            loader.Pending[0].SetResult(NativeScenes.CreateForTest("one"));
            context.Until(() => loader.Calls == 2);
            loader.Pending[1].SetResult(NativeScenes.CreateForTest("two"));
            Complete(second);
            Equal(2, scenes.LoadedCount);
        });
        Run("scene cancellation before queue execution skips the provider", () =>
        {
            using var scenes = new Scenes();
            var loader = new ControlledScenes();
            scenes.Initialize(loader);
            var first = scenes.LoadAsync("one");
            using var cancellation = new CancellationTokenSource();
            var queued = scenes.LoadAsync("two", token: cancellation.Token);
            cancellation.Cancel();
            Fails<OperationCanceledException>(queued);
            loader.Pending[0].SetResult(NativeScenes.CreateForTest("one"));
            Complete(first);
            context.Drain();
            Equal(1, loader.Calls);
        });
        Run("failed scene loads release the queue and notification failures do not corrupt it", () =>
        {
            using var scenes = new Scenes();
            var loader = new ControlledScenes();
            scenes.Initialize(loader);
            var failed = scenes.LoadAsync("fail");
            loader.Pending[0].SetException(new InvalidOperationException());
            Fails<InvalidOperationException>(failed);
            int notified = 0;
            scenes.Loaded += _ => throw new InvalidOperationException("listener");
            scenes.Loaded += _ => notified++;
            var next = scenes.LoadAsync("next");
            loader.Pending[1].SetResult(NativeScenes.CreateForTest("next"));
            Complete(next);
            Equal(1, notified);
        });
        Run("scene disposal retires pending results and disposes its provider", () =>
        {
            var scenes = new Scenes();
            var loader = new ControlledScenes();
            scenes.Initialize(loader);
            int notified = 0;
            scenes.Loaded += _ => notified++;
            var loading = scenes.LoadAsync("one");
            scenes.Dispose();
            True(loader.Disposed);
            loader.Pending[0].SetResult(NativeScenes.CreateForTest("one"));
            Fails<OperationCanceledException>(loading);
            Equal(0, notified);
            Equal(0, scenes.LoadedCount);
        });
        Run("scene unloading sends one notification and rejects unowned scenes", () =>
        {
            using var scenes = new Scenes();
            scenes.Initialize();
            var scene = Complete(scenes.LoadAsync("owned", LoadSceneMode.Additive));
            int unloaded = 0;
            scenes.Unloaded += _ => unloaded++;
            Complete(scenes.UnloadAsync(scene));
            Equal(1, unloaded);
            Fails<InvalidOperationException>(scenes.UnloadAsync(scene));
        });
        Run("audio shares Framework singleton and old handles cannot stop reused sources", () =>
        {
            var manager = AudioManager.Instance;
            var first = manager.Play(new AudioClip());
            var source = Sources()[0];
            first.Stop();
            True(!first.IsValid);
            var next = manager.Play(new AudioClip());
            Same(source, Sources()[0]);
            first.Stop();
            True(next.IsValid && source.isPlaying);
        });
        Run("audio applies master, group and voice volume and honors capacity", () =>
        {
            var manager = AudioManager.Instance;
            manager.MaxVoices = 1;
            var handle = manager.Play(new AudioClip(), AudioGroup.Music, volume: .5f);
            manager.MasterVolume = .5f;
            manager.SetGroupVolume(AudioGroup.Music, .5f);
            Equal(.125f, Sources()[0].volume);
            True(!manager.TryPlay(new AudioClip(), out _));
            handle.SetVolume(1);
            Equal(.25f, Sources()[0].volume);
            Throws<ArgumentOutOfRangeException>(() => manager.MasterVolume = float.NaN);
        });
        Run("audio pauses survive ticks and global resume retains individually paused voices", () =>
        {
            var manager = AudioManager.Instance;
            var first = manager.Play(new AudioClip());
            var second = manager.Play(new AudioClip());
            first.Pause();
            manager.PauseAll();
            manager.ResumeAll();
            Tick(manager);
            True(first.IsValid && second.IsValid);
            True(!Sources()[0].isPlaying && Sources()[1].isPlaying);
            first.Resume();
            True(Sources()[0].isPlaying);
            AudioListener.pause = true;
            Tick(manager);
            Equal(2, manager.PlayingCount);
            AudioListener.pause = false;
        });
        Run("audio completion recycles before callbacks and reentrant play remains valid", () =>
        {
            var manager = AudioManager.Instance;
            AudioHandle next = default;
            int calls = 0;
            var first = manager.Play(new AudioClip(), completed: old =>
            {
                calls++;
                True(!old.IsValid);
                next = manager.Play(new AudioClip());
            });
            Sources()[0].FinishForTest();
            Tick(manager);
            Equal(1, calls);
            True(!first.IsValid && next.IsValid);
            Tick(manager);
            Equal(1, calls);
        });
        Run("audio 3D reuse resets source mode and group stop preserves other voices", () =>
        {
            var manager = AudioManager.Instance;
            manager.Play(new AudioClip(), AudioGroup.Music, loop: true, position: new Vector3(2, 3, 4));
            Equal(1f, Sources()[0].spatialBlend);
            manager.StopAll(AudioGroup.Music);
            manager.Play(new AudioClip(), AudioGroup.Sound);
            manager.Play(new AudioClip(), AudioGroup.Music);
            Equal(0f, Sources()[0].spatialBlend);
            True(!Sources()[0].loop);
            manager.StopAll(AudioGroup.Sound);
            Equal(1, manager.PlayingCount);
        });
        Run("audio destruction clears sources and cannot resurrect through an old handle", () =>
        {
            var manager = AudioManager.Instance;
            var handle = manager.Play(new AudioClip());
            AudioManager.DestroyInstance();
            True(!handle.IsValid);
            Equal(0, Sources().Length);
            Equal(0, AudioManager.Instance.PlayingCount);
        });
        Run("resource shutdown continues past a failed scene unload and releases assets", () =>
        {
            var manager = ResourceManager.Instance;
            Complete(manager.InitializeAsync());
            _ = manager.LoadAsset<TextAsset>("text");
            Complete(manager.LoadSceneAsync("bad", LoadSceneMode.Additive));
            Complete(manager.LoadSceneAsync("good", LoadSceneMode.Additive));
            YooAssets.Package.Scenes[0].FailUnload = true;
            Fails<AggregateException>(manager.ShutdownAsync());
            Equal(1, YooAssets.Package.Scenes[1].UnloadCount);
            Equal(1, YooAssets.Package.Assets[0].ReleaseCount);
            True(!manager.Initialized);
            Throws<InvalidOperationException>(() => manager.LoadAsset<TextAsset>("later"));
            YooAssets.Package.Scenes[0].FailUnload = false;
            Complete(manager.ShutdownAsync());
        });
        Run("old resource shutdown cannot release assets from a new play session", () =>
        {
            var manager = ResourceManager.Instance;
            Complete(manager.InitializeAsync());
            Complete(manager.LoadSceneAsync("old", LoadSceneMode.Additive));
            var gate = Gate<bool>();
            YooAssets.Package.Scenes[0].UnloadGate = gate.Task;
            var stopping = manager.ShutdownAsync();
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
            YooAssets.Package = new ResourcePackage();
            Complete(manager.InitializeAsync());
            var asset = manager.LoadAsset<TextAsset>("new");
            gate.SetResult(true);
            Complete(stopping);
            True(manager.Initialized);
            Equal(0, YooAssets.Package.Assets[0].ReleaseCount);
            Same(asset, manager.LoadAsset<TextAsset>("new"));
        });
        Run("YooAsset scene loader holds independent handles for repeated addresses", () =>
        {
            using var loader = new YooAssetSceneLoader(YooAssets.Package);
            var first = Complete(loader.LoadAsync("same", LoadSceneMode.Additive, null));
            var next = Complete(loader.LoadAsync("same", LoadSceneMode.Additive, null));
            True(first.handle != next.handle);
            Complete(loader.UnloadAsync(first));
            Equal(1, YooAssets.Package.Scenes[0].UnloadCount);
            Equal(0, YooAssets.Package.Scenes[1].UnloadCount);
            loader.Dispose();
            Equal(0, YooAssets.Package.Scenes[1].ReleaseCount);
            NativeScenes.UnloadSceneAsync(next);
            Equal(1, YooAssets.Package.Scenes[1].ReleaseCount);
        });
        Run("YooAsset scene load failure releases the acquired handle", () =>
        {
            using var loader = new YooAssetSceneLoader(YooAssets.Package);
            YooAssets.Package.FailNextScene = true;
            Fails<InvalidOperationException>(loader.LoadAsync("bad", LoadSceneMode.Additive, null));
            Equal(1, YooAssets.Package.Scenes[0].ReleaseCount);
        });
        Run("reset scene managers cannot overlap an old native operation", () =>
        {
            var previous = new Scenes();
            var oldLoader = new ControlledScenes();
            previous.Initialize(oldLoader);
            var oldLoad = previous.LoadAsync("old");
            previous.Dispose();
            Fails<OperationCanceledException>(oldLoad);
            using var current = new Scenes();
            var nextLoader = new ControlledScenes();
            current.Initialize(nextLoader);
            var nextLoad = current.LoadAsync("new");
            Equal(0, nextLoader.Calls);
            oldLoader.Pending[0].SetResult(NativeScenes.CreateForTest("old"));
            context.Until(() => nextLoader.Calls == 1);
            nextLoader.Pending[0].SetResult(NativeScenes.CreateForTest("new"));
            Complete(nextLoad);
        });
        Run("storage reset invalidates old references without deleting files", () =>
        {
            using var storage = new StorageFixture();
            storage.Manager.SaveText("kept.txt", "keep");
            storage.Manager.Dispose();
            Throws<ObjectDisposedException>(() => storage.Manager.LoadText("kept.txt"));
            True(File.Exists(Path.Combine(storage.Root, "kept.txt")));
        });
        Run("independent resource leases share assets without sharing release ownership", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var cached = resources.LoadAsset<TextAsset>("shared");
            var first = Complete(resources.AcquireAsync<TextAsset>("shared"));
            var second = Complete(resources.AcquireAsync<TextAsset>("shared"));
            Same(cached, first.Asset);
            Same(first.Asset, second.Asset);
            first.Dispose();
            first.Dispose();
            True(!first.IsValid && second.IsValid);
            Equal(1, YooAssets.Package.Assets[1].ReleaseCount);
            Equal(0, YooAssets.Package.Assets[0].ReleaseCount);
            resources.ReleaseAllAssets();
            True(second.IsValid);
            Equal(0, YooAssets.Package.Assets[2].ReleaseCount);
            second.Dispose();
            Equal(0, resources.OwnedAssetCount);
        });
        Run("cancelling one resource acquisition never cancels another acquisition", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var loading = Gate<bool>();
            YooAssets.Package.AssetGate = loading.Task;
            using var cancel = new CancellationTokenSource();
            var abandoned = resources.AcquireAsync<TextAsset>("same", cancel.Token);
            var current = resources.AcquireAsync<TextAsset>("same");
            cancel.Cancel();
            Fails<OperationCanceledException>(abandoned);
            Equal(0, YooAssets.Package.Assets[0].ReleaseCount);
            loading.SetResult(true);
            var lease = Complete(current);
            context.Until(() => resources.OwnedAssetCount == 1);
            Equal(1, YooAssets.Package.Assets[0].ReleaseCount);
            True(lease.IsValid);
            lease.Dispose();
        });
        Run("resource scope disposal cancels pending native loads and releases late handles", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var loading = Gate<bool>();
            YooAssets.Package.AssetGate = loading.Task;
            var scope = resources.CreateScope();
            var pending = scope.AcquireAsync<TextAsset>("late");
            scope.Dispose();
            Fails<OperationCanceledException>(pending);
            loading.SetResult(true);
            context.Until(() => resources.OwnedAssetCount == 0);
            Equal(1, YooAssets.Package.Assets[0].ReleaseCount);
        });
        Run("scene resource scope unload releases only that scene's resources", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var scene = NativeScenes.CreateForTest("owned resources");
            using var scope = resources.CreateSceneScope(scene);
            var own = Complete(scope.AcquireAsync<TextAsset>("same"));
            var other = Complete(resources.AcquireAsync<TextAsset>("same"));
            NativeScenes.UnloadSceneAsync(scene);
            True(!own.IsValid && other.IsValid);
            Equal(1, resources.OwnedAssetCount);
            other.Dispose();
        });
        Run("resource shutdown invalidates leases and awaits unfinished native releases", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var lease = Complete(resources.AcquireAsync<TextAsset>("ready"));
            var loading = Gate<bool>();
            YooAssets.Package.AssetGate = loading.Task;
            var late = resources.AcquireAsync<TextAsset>("late");
            var shutdown = resources.ShutdownAsync();
            True(!lease.IsValid && !shutdown.IsCompleted);
            Fails<OperationCanceledException>(late);
            loading.SetResult(true);
            Complete(shutdown);
            Equal(0, resources.OwnedAssetCount);
            True(!resources.Initialized);
        });
        Run("resource initialization waits for its own pending release without entry bookkeeping", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var loading = Gate<bool>();
            YooAssets.Package.AssetGate = loading.Task;
            var late = resources.AcquireAsync<TextAsset>("late release");
            ResourceManager.Release();
            var restarting = resources.InitializeAsync();
            True(!restarting.IsCompleted);
            Fails<OperationCanceledException>(late);
            loading.SetResult(true);
            True(Complete(restarting));
            True(resources.Initialized);
            Equal(0, resources.OwnedAssetCount);
        });
        Run("protected leases keep native consumers alive until they finish their own cleanup", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var lease = Complete(resources.AcquireAsync<TextAsset>("native consumer", keepAliveUntilDisposed: true));
            var stopping = resources.ShutdownAsync();
            True(!stopping.IsCompleted && lease.IsValid);
            Equal(0, YooAssets.Package.Assets[0].ReleaseCount);
            var restarting = resources.InitializeAsync();
            True(!restarting.IsCompleted);
            lease.Dispose();
            Complete(stopping);
            True(Complete(restarting));
            Equal(1, YooAssets.Package.Assets[0].ReleaseCount);
        });
        Run("play reset can retire protected leases without an old shutdown clearing the new play", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var lease = Complete(resources.AcquireAsync<TextAsset>("old native", keepAliveUntilDisposed: true));
            var stopping = resources.ShutdownAsync();
            True(!stopping.IsCompleted);
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resources, null);
            True(Complete(resources.InitializeAsync()));
            Complete(stopping);
            True(resources.Initialized && !lease.IsValid);
        });
        Run("failed or incorrectly typed acquisitions release their own handles", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var cached = resources.LoadAsset<TextAsset>("text");
            Fails<InvalidCastException>(resources.AcquireAsync<GameObject>("text"));
            Same(cached, resources.LoadAsset<TextAsset>("text"));
            Equal(1, YooAssets.Package.Assets[1].ReleaseCount);
            YooAssets.Package.FailNextAsset = true;
            Fails<InvalidOperationException>(resources.AcquireAsync<TextAsset>("bad"));
            Equal(0, resources.OwnedAssetCount);
        });
        Run("scope cancellation callback failure cannot prevent asset release", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var scope = resources.CreateScope();
            var asset = Complete(scope.AcquireAsync<TextAsset>("own"));
            scope.Token.Register(() => throw new InvalidOperationException("cancellation callback"));
            scope.Dispose();
            True(!asset.IsValid);
            Equal(0, resources.OwnedAssetCount);
        });
        Run("disposing a loader during loading retains a successful scene until native unload", () =>
        {
            var loading = Gate<bool>();
            YooAssets.Package.SceneGate = loading.Task;
            var loader = new YooAssetSceneLoader(YooAssets.Package);
            var scene = loader.LoadAsync("late scene", LoadSceneMode.Additive, null);
            loader.Dispose();
            loading.SetResult(true);
            Fails<ObjectDisposedException>(scene);
            Equal(0, YooAssets.Package.Scenes[0].ReleaseCount);
            NativeScenes.UnloadSceneAsync(YooAssets.Package.Scenes[0].SceneObject);
            Equal(1, YooAssets.Package.Scenes[0].ReleaseCount);
        });
        Run("play reset cannot release a legacy scene bundle before native scene unload", () =>
        {
            var resources = ResourceManager.Instance;
            Complete(resources.InitializeAsync());
            var scene = Complete(resources.LoadSceneAsync("retained", LoadSceneMode.Additive));
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resources, null);
            Equal(0, YooAssets.Package.Scenes[0].ReleaseCount);
            NativeScenes.UnloadSceneAsync(scene);
            Equal(1, YooAssets.Package.Scenes[0].ReleaseCount);
        });
        InteractionScenarios();
        TimerDriverScenarios();
        Console.WriteLine($"PASS {passed} service regression scenarios (actual Luban CLI/runtime; Unity/Input System/YooAsset API models; not native Unity).");
    }

    private static void Run(string name, Action test)
    {
        Object.ResetTestState();
        YooAssets.Package = new ResourcePackage();
        AudioListener.pause = false;
        context = new Pump();
        SynchronizationContext.SetSynchronizationContext(context);
        UnityThread.Reset();
        UnityThread.Initialize();
        test();
        // Native completion must release the shared queue even when the caller already observed cancellation.
        var operations = (SemaphoreSlim)typeof(Scenes).GetField("operations", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        context.Until(() => operations.CurrentCount == 1);
        // This harness owns the services it creates, just as the game entry does.
        InputManager.Release();
        DataManager.Release();
        Scenes.Release();
        if (ResourceManager.TryGetInstance(out var resources))
        {
            Complete(resources.ShutdownAsync());
        }

        Singleton<ResourceManager>.Release();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static AudioSource[] Sources() => Object.FindObjectsOfType<AudioSource>();
    private static void Tick(AudioManager manager) => typeof(AudioManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
    private static TaskCompletionSource<T> Gate<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Complete(Task task)
    {
        context.Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static T Complete<T>(Task<T> task)
    {
        Complete((Task)task);
        return task.GetAwaiter().GetResult();
    }

    private static void Fails<T>(Task task)
        where T : Exception => Throws<T>(() => Complete(task));
    private static void True(bool value)
    {
        if (!value)
        {
            throw new Exception("Assertion failed.");
        }
    }

    private static void Same(object expected, object actual) => True(ReferenceEquals(expected, actual));
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"Expected {expected}; actual {actual}");
        }
    }

    private static void Throws<T>(Action action)
        where T : Exception
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

    [Serializable]
    private sealed class Row
    {
        public int id;
        public string name;
    }

    private sealed class StorageFixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "novorizon-storage-" + Guid.NewGuid().ToString("N"));
        internal readonly LocalStorageManager Manager = new LocalStorageManager();
        internal StorageFixture() => Manager.Initialize(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class ControlledScenes : Game.Framework.Scenes.ISceneLoader, IDisposable
    {
        internal readonly List<TaskCompletionSource<Scene>> Pending = new List<TaskCompletionSource<Scene>>();
        internal int Calls;
        internal bool Disposed;
        public Task<Scene> LoadAsync(string location, LoadSceneMode mode, IProgress<float> progress)
        {
            Calls++;
            var pending = Gate<Scene>();
            Pending.Add(pending);
            return pending.Task;
        }

        public Task UnloadAsync(Scene scene) => Task.CompletedTask;
        public void Dispose() => Disposed = true;
    }

    private sealed class Pump : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback callback, object state)> callbacks = new ConcurrentQueue<(SendOrPostCallback, object)>();
        public override void Post(SendOrPostCallback callback, object state) => callbacks.Enqueue((callback, state));
        internal void Drain()
        {
            // Yield continuations may enqueue further work; cap each batch to let the test drive native gates.
            int count = callbacks.Count;
            for (int i = 0; i < count && callbacks.TryDequeue(out var work); i++)
            {
                work.callback(work.state);
            }
        }

        internal void Until(Func<bool> predicate)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!predicate())
            {
                if (timeout.ElapsedMilliseconds > 10000)
                {
                    throw new TimeoutException("Main-thread model did not complete.");
                }

                Drain();
                Thread.Sleep(1);
            }
        }
    }
}
