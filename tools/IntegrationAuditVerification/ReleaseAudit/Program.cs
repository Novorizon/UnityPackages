using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;
using UnityEngine;

internal static class Program
{
    private static int passed;
    private static int failed;

    private static void Main()
    {
        Application.streamingAssetsPath = Path.Combine(Path.GetTempPath(), "novorizon-audit-streaming");
        Run("packaged hash verification yields instead of hashing on the caller thread", () => VerifyYields(false));
        Run("resource code hash verification yields instead of hashing on the caller thread", () => VerifyYields(true));
        Run("corrupted packaged code is rejected before a prepared release is returned", () =>
        {
            var fixture = new Fixture(1024, false);
            fixture.Transport.Bytes[0] ^= 1;
            Throws<InvalidDataException>(fixture.Coordinator.Prepare(CancellationToken.None));
        });
        Run("corrupted resource code is rejected before a prepared release is returned", () =>
        {
            var fixture = new Fixture(1024, true);
            fixture.Transport.Bytes[0] ^= 1;
            Throws<InvalidDataException>(fixture.Coordinator.Prepare(CancellationToken.None));
        });
        Run("cancelled preparation does not publish code queued behind other CPU work", () =>
        {
            var fixture = new Fixture(1024, false);
            using var cancel = new CancellationTokenSource();
            using var worker = new OccupiedWorker();
            Task<PreparedRelease> preparing = fixture.Coordinator.Prepare(cancel.Token);
            cancel.Cancel();
            worker.Release();
            Throws<OperationCanceledException>(preparing);
        });
        Run("cancellation at the CPU verification completion boundary cannot publish code", () =>
        {
            var fixture = new Fixture(1024 * 1024, false);
            using var cancel = new CancellationTokenSource();
            var previous = SynchronizationContext.Current;
            var context = new CancelOnCompletion(cancel);
            using var worker = new OccupiedWorker();
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                Task<PreparedRelease> preparing = fixture.Coordinator.Prepare(cancel.Token);
                worker.Release();
                Throws<OperationCanceledException>(preparing);
                if (context.Deliveries == 0)
                {
                    throw new Exception("The verification continuation was not exercised.");
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });
        Run("a content provider cannot suppress a cancelled final preparation boundary", () =>
        {
            var fixture = new Fixture(1024, false);
            using var cancel = new CancellationTokenSource();
            fixture.Settings.prepareContentAsync = (context, token) =>
            {
                cancel.Cancel();
                return Task.CompletedTask;
            };
            Throws<OperationCanceledException>(fixture.Coordinator.Prepare(cancel.Token));
        });
        Run("packaged hot-code preparation performance sample", () =>
        {
            var fixture = new Fixture(8 * 1024 * 1024, false);
            fixture.Coordinator.Prepare(CancellationToken.None).GetAwaiter().GetResult();
            const int iterations = 12;
            var total = new double[iterations];
            var caller = new double[iterations];
            for (int i = 0; i < iterations; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Task<PreparedRelease> task = fixture.Coordinator.Prepare(CancellationToken.None);
                long returned = Stopwatch.GetTimestamp();
                PreparedRelease release = task.GetAwaiter().GetResult();
                long ended = Stopwatch.GetTimestamp();
                if (!ReferenceEquals(release.Assemblies[0], fixture.Transport.Bytes))
                {
                    throw new Exception("The returned byte array was not the verified payload.");
                }
                caller[i] = (returned - start) * 1000d / Stopwatch.Frequency;
                total[i] = (ended - start) * 1000d / Stopwatch.Frequency;
            }
            Array.Sort(caller);
            Array.Sort(total);
            Console.WriteLine("PERF release-prepare payloadBytes=" + fixture.Transport.Bytes.Length + " iterations=" + iterations +
                " medianCallerMilliseconds=" + caller[iterations / 2].ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                " medianTotalMilliseconds=" + total[iterations / 2].ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        });
        Console.WriteLine("RESULT release audit passed=" + passed + " failed=" + failed + "; real coordinator/policy, transport and Unity API are models.");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    private static void VerifyYields(bool resourceCode)
    {
        var fixture = new Fixture(1024, resourceCode);
        using var worker = new OccupiedWorker();
        Task<PreparedRelease> preparing = fixture.Coordinator.Prepare(CancellationToken.None);
        bool completedWithoutWorker = preparing.IsCompleted;
        worker.Release();
        PreparedRelease release = preparing.GetAwaiter().GetResult();
        if (completedWithoutWorker)
        {
            throw new Exception("Hash work ran synchronously on the caller while the CPU worker was occupied.");
        }
        if (!ReferenceEquals(release.Assemblies[0], fixture.Transport.Bytes))
        {
            throw new Exception("Prepared bytes changed.");
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error);
        }
    }

    private static void Throws<T>(Task task) where T : Exception
    {
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class Fixture
    {
        internal readonly FakeTransport Transport;
        internal readonly UpdateCoordinator Coordinator;
        internal readonly UpdateSettings Settings;
        internal Fixture(int length, bool resources)
        {
            var bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)i;
            }
            var manifest = new ReleaseManifest
            {
                appId = "audit",
                platform = "windows-x64",
                channel = "test",
                baselineId = "baseline",
                version = 1,
                assemblies = new[]
                {
                    new ReleaseFile { assemblyName = "Game", fileName = "Game.dll.bytes", size = length, sha256 = ReleasePolicy.Sha256(bytes) }
                },
                aotMetadata = Array.Empty<ReleaseFile>(),
                content = resources ? new ReleaseContent { packageName = "DefaultPackage", version = "1", codeInResources = true } : null
            };
            Transport = new FakeTransport(bytes, JsonUtility.ToJson(manifest));
            Settings = new UpdateSettings { appId = "audit", channel = "test", usePackagedReleaseOnly = true };
            if (resources)
            {
                Settings.loadCodeAsync = (context, file, token) => Task.FromResult(Transport.Bytes);
            }
            Coordinator = new UpdateCoordinator(Settings, _ => { }, Transport);
        }
    }

    private sealed class CancelOnCompletion : SynchronizationContext
    {
        private readonly CancellationTokenSource cancellation;
        internal int Deliveries;
        internal CancelOnCompletion(CancellationTokenSource cancellation)
        {
            this.cancellation = cancellation;
        }
        public override void Post(SendOrPostCallback callback, object state)
        {
            Interlocked.Increment(ref Deliveries);
            cancellation.Cancel();
            callback(state);
        }
    }

    private sealed class FakeTransport : IUpdateTransport
    {
        internal readonly byte[] Bytes;
        private readonly string manifest;
        internal FakeTransport(byte[] bytes, string manifest)
        {
            Bytes = bytes;
            this.manifest = manifest;
        }
        public Task<byte[]> ReadBytes(string url, long limit, CancellationToken token) => Task.FromResult(Bytes);
        public Task<string> ReadText(string url, CancellationToken token) => Task.FromResult(manifest);
        public Task DownloadVerified(Uri url, string destination, ReleaseFile file, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class OccupiedWorker : IDisposable
    {
        private readonly ManualResetEventSlim started = new ManualResetEventSlim();
        private readonly ManualResetEventSlim released = new ManualResetEventSlim();
        private readonly Task task;
        private readonly int minimumWorkers, minimumIo, maximumWorkers, maximumIo;
        internal OccupiedWorker()
        {
            ThreadPool.GetMinThreads(out minimumWorkers, out minimumIo);
            ThreadPool.GetMaxThreads(out maximumWorkers, out maximumIo);
            if (!ThreadPool.SetMinThreads(1, minimumIo) || !ThreadPool.SetMaxThreads(1, maximumIo))
            {
                throw new InvalidOperationException("Could not isolate the CPU worker for the affinity regression.");
            }
            task = Task.Run(() =>
            {
                started.Set();
                released.Wait();
            });
            if (!started.Wait(TimeSpan.FromSeconds(5)))
            {
                Release();
                throw new TimeoutException("Could not occupy the isolated worker.");
            }
        }
        internal void Release() => released.Set();
        public void Dispose()
        {
            Release();
            task.GetAwaiter().GetResult();
            ThreadPool.SetMaxThreads(maximumWorkers, maximumIo);
            ThreadPool.SetMinThreads(minimumWorkers, minimumIo);
            started.Dispose();
            released.Dispose();
        }
    }
}
