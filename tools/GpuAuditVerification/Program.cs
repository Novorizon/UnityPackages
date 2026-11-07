using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Game.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class Program
{
    private static readonly AnimationData Shared = new AnimationData
    {
        totalFrame = 4,
        totalBoneNum = 1,
        texWidth = 32,
        texHeight = 32,
        texBytes = new byte[] { 1, 2, 3 },
        clips = new[] { new AnimationClipData("loop", 0, 3, true, 10) }
    };

    private static void Main(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0] == "--benchmark")
        {
            Benchmark();
            return;
        }
        Run("batch unregister retains survivors and releases all stopped references", BatchUnregister);
        Run("paused manual scheduler bounds registration churn", PausedChurn);
        Run("callback mutations retain registration order and defer new players", ReentrantOrder);
        Run("retiring a manager inside Tick clears every stored player reference", RetireDuringTick);
        Run("steady scheduler ticks allocate no managed memory", ZeroAllocation);
        Run("baked data roundtrips current and legacy public names", RoundTrip);
        Run("unexpected formatter payload is rejected before its callback", RejectUnexpected);
        Console.WriteLine("PASS 7 GPU audit scenarios; Unity APIs use the finite boundary model.");
    }

    private static GpuAnimatorManager Manager()
    {
        Object.ResetTestState();
        var manager = GpuAnimatorManager.Instance;
        manager.AutomaticUpdate = false;
        return manager;
    }

    private static GpuAnimator Player()
    {
        var root = new GameObject("GPU audit");
        var player = root.AddComponent<GpuAnimator>();
        if (!player.Initialize(root.AddComponent<Renderer>(), Shared) || !player.TryPlay("loop"))
        {
            throw new Exception("Player setup failed.");
        }
        return player;
    }

    private static int StoredCount(GpuAnimatorManager manager)
    {
        var field = typeof(GpuAnimatorManager).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);
        return ((System.Collections.ICollection)field.GetValue(manager)).Count;
    }

    private static void BatchUnregister()
    {
        var manager = Manager();
        var players = new GpuAnimator[1000];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = Player();
        }
        for (int i = 0; i < players.Length; i += 2)
        {
            players[i].Stop();
        }
        Equal(500, manager.Count);
        manager.Tick(.1f);
        Equal(500, StoredCount(manager));
        for (int i = 0; i < players.Length; i++)
        {
            Equal(i % 2 == 0 ? 0 : 1, players[i].CurrentFrame);
            players[i].Stop();
        }
        Equal(0, manager.Count);
        Equal(0, StoredCount(manager));
    }

    private static void PausedChurn()
    {
        var manager = Manager();
        manager.IsPaused = true;
        var survivor = Player();
        var churn = Player();
        for (int i = 0; i < 10000; i++)
        {
            churn.Stop();
            if (!churn.TryPlay("loop"))
            {
                throw new Exception("Replaying a stopped player failed.");
            }
        }
        Equal(2, manager.Count);
        if (StoredCount(manager) > 66)
        {
            throw new Exception("Paused registration churn retained an unbounded tombstone list.");
        }
        survivor.Stop();
        churn.Stop();
        Equal(0, StoredCount(manager));
    }

    private static void ReentrantOrder()
    {
        var manager = Manager();
        var first = Player();
        var removed = Player();
        var last = Player();
        first.Stop();
        first.AnimationCompleted += (player, clip) =>
        {
            removed.Stop();
            removed.TryPlay("loop");
        };
        first.TryPlay("loop", loop: false);
        manager.Tick(1);
        Equal(2, manager.Count);
        Equal(0, removed.CurrentFrame);
        Equal(2, last.CurrentFrame);
        manager.Tick(.1f);
        Equal(1, removed.CurrentFrame);
        Equal(3, last.CurrentFrame);
        removed.Stop();
        last.Stop();
    }

    private static void RetireDuringTick()
    {
        var manager = Manager();
        var player = Player();
        player.Stop();
        player.TryPlay("loop", loop: false);
        manager.AnimationCompleted += (current, clip) => GpuAnimatorManager.DestroyInstance();
        manager.Tick(1);
        Equal(0, manager.Count);
        Equal(0, StoredCount(manager));
        if (GpuAnimatorManager.TryGetInstance(out _))
        {
            throw new Exception("A retired manager remained published.");
        }
    }

    private static void ZeroAllocation()
    {
        var manager = Manager();
        for (int i = 0; i < 100; i++)
        {
            Player();
        }
        for (int i = 0; i < 100; i++)
        {
            manager.Tick(.1f);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            manager.Tick(.1f);
        }
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static byte[] Encode(object value, SerializationBinder binder = null)
    {
        using (var stream = new MemoryStream())
        {
            var formatter = new BinaryFormatter { Binder = binder };
            formatter.Serialize(stream, value);
            return stream.ToArray();
        }
    }

    private sealed class LegacyNames : SerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            throw new NotSupportedException();
        }

        public override void BindToName(Type type, out string assemblyName, out string typeName)
        {
            assemblyName = type.Assembly.FullName;
            typeName = type == typeof(AnimationData) ? "GpuSkinningAnimData" :
                type == typeof(AnimationClipData) ? "GpuSkinningAnimClip" :
                type == typeof(AnimationClipData[]) ? "GpuSkinningAnimClip[]" : type.FullName;
        }
    }

    private static void RoundTrip()
    {
        foreach (SerializationBinder binder in new SerializationBinder[] { null, new LegacyNames() })
        {
            var decoded = AnimationData.FromBytes(Encode(Shared, binder));
            Equal(4, decoded.totalFrame);
            Equal(1, decoded.totalBoneNum);
            Equal(3, decoded.texBytes.Length);
            Equal("loop", decoded.clips[0].name);
        }
    }

    [Serializable]
    private sealed class UnexpectedPayload
    {
        internal static bool CallbackRan;

        [OnDeserialized]
        private void OnDecoded(StreamingContext context)
        {
            CallbackRan = true;
        }
    }

    private static void RejectUnexpected()
    {
        UnexpectedPayload.CallbackRan = false;
        try
        {
            AnimationData.FromBytes(Encode(new UnexpectedPayload()));
            throw new Exception("Unexpected payload was accepted.");
        }
        catch (SerializationException)
        {
        }
        if (UnexpectedPayload.CallbackRan)
        {
            throw new Exception("Unexpected formatter type ran its deserialization callback.");
        }
    }

    private static void Benchmark()
    {
        var warmManager = Manager();
        for (int i = 0; i < 100; i++)
        {
            Player().Stop();
        }
        foreach (int count in new[] { 2000, 4000, 8000 })
        {
            var samples = new double[5];
            long allocated = 0;
            for (int sample = 0; sample < samples.Length; sample++)
            {
                var manager = Manager();
                var players = new GpuAnimator[count];
                for (int i = 0; i < count; i++)
                {
                    players[i] = Player();
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < count; i++)
                {
                    players[i].Stop();
                }
                samples[sample] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                allocated = Math.Max(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
                Equal(0, manager.Count);
            }
            Array.Sort(samples);
            Console.WriteLine("GPU_STOP count=" + count + " samples=5 median_ms=" + samples[2].ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " max_ms=" + samples[4].ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " allocated_bytes=" + allocated);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual))
        {
            throw new Exception("Expected " + expected + ", actual " + actual);
        }
    }

    private static void Run(string name, Action action)
    {
        action();
        Console.WriteLine("PASS " + name);
    }
}
