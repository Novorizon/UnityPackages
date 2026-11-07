using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Framework.Ads.Csj
{
    /// <summary>Domestic CSJ SDK, not the global Pangle SDK or the Ocean Engine campaign API.</summary>
    public sealed class CsjAdProvider : IAdProvider
    {
        private sealed class Entry
        {
            internal string Id;
            internal AdPlacement Placement;
            internal bool Loaded, Shown, Rewarded;
        }

        private static CsjAdProvider owner;
        private readonly string appId, userId;
        private readonly bool privacyConsent;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Queue<string> history = new Queue<string>();
        private readonly Dictionary<string, Entry> loaded = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private CsjCallbackHost host;
        private TaskCompletionSource<bool> completion;
        private string activeId, activeKind;
        private bool initialized, disposed;
        public string Id => "bytedance-csj";
        public event Action<AdReward> RewardEarned;

        public CsjAdProvider(string appId, bool privacyConsent, string rewardUserId = "")
        {
            if (string.IsNullOrWhiteSpace(appId))
            {
                throw new ArgumentException("The domestic CSJ App ID is required.", nameof(appId));
            }
            this.appId = appId;
            this.privacyConsent = privacyConsent;
            userId = rewardUserId ?? "";
        }

        public async Task InitializeAsync(CancellationToken token)
        {
            Ready(false);
            token.ThrowIfCancellationRequested();
            if (initialized)
            {
                return;
            }
            if (!privacyConsent)
            {
                throw new InvalidOperationException("Initialize CSJ after the player accepts the project's privacy agreement.");
            }
            if (Application.isEditor || (Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer))
            {
                throw new PlatformNotSupportedException("CSJ native ads require an Android or iOS player.");
            }
            if (owner != null && owner != this)
            {
                throw new InvalidOperationException("Another provider owns the shared CSJ SDK.");
            }
            owner = this;
            if (host == null)
            {
                var root = new GameObject("NovorizonCsj_" + Guid.NewGuid().ToString("N"));
                Object.DontDestroyOnLoad(root);
                host = root.AddComponent<CsjCallbackHost>();
                host.Received = Receive;
            }
            Begin(Guid.NewGuid().ToString("N"), "init");
            try
            {
                Native("initialize", activeId, appId, 0);
                await completion.Task;
                token.ThrowIfCancellationRequested();
                initialized = true;
            }
            finally
            {
                End();
            }
        }

        public async Task LoadAsync(AdPlacement placement, CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (placement == null)
            {
                throw new ArgumentNullException(nameof(placement));
            }
            var entry = new Entry { Id = Guid.NewGuid().ToString("N"), Placement = placement };
            Begin(entry.Id, "load");
            entries.Add(entry.Id, entry);
            try
            {
                Native("load", entry.Id, placement.UnitId, (int)placement.Format);
                await completion.Task;
                token.ThrowIfCancellationRequested();
                entry.Loaded = true;
                string key = Key(placement);
                if (loaded.TryGetValue(key, out var previous))
                {
                    Native("release", previous.Id, "", 0);
                    entries.Remove(previous.Id);
                }
                loaded[key] = entry;
            }
            catch
            {
                entries.Remove(entry.Id);
                Native("release", entry.Id, "", 0);
                throw;
            }
            finally
            {
                End();
            }
        }

        public bool IsReady(AdPlacement placement)
        {
            Ready();
            return placement != null && loaded.TryGetValue(Key(placement), out var entry) && entry.Loaded && !entry.Shown;
        }

        public async Task ShowAsync(AdPlacement placement, CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (!IsReady(placement))
            {
                throw new InvalidOperationException("Load a CSJ ad before showing it.");
            }
            var entry = loaded[Key(placement)];
            Begin(entry.Id, "show");
            entry.Shown = true;
            loaded.Remove(Key(placement));
            try
            {
                Native("show", entry.Id, "", (int)placement.Format);
                await completion.Task;
            }
            finally
            {
                // Preserve identity for delayed reward verification after closing; memory is bounded.
                history.Enqueue(entry.Id);
                if (history.Count > 256)
                {
                    string retired = history.Dequeue();
                    entries.Remove(retired);
                    Native("release", retired, "", 0);
                }
                End();
            }
        }

        private static string Key(AdPlacement placement) => ((int)placement.Format) + ":" + placement.UnitId;

        private void Begin(string request, string kind)
        {
            if (completion != null)
            {
                throw new InvalidOperationException("A native CSJ operation is still active.");
            }
            activeId = request;
            activeKind = kind;
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void End()
        {
            completion = null;
            activeId = null;
            activeKind = null;
        }

        private void Receive(CsjCallbackHost.Response response)
        {
            if (disposed)
            {
                return;
            }
            if (response.kind == "reward")
            {
                if (entries.TryGetValue(response.requestId, out var entry) && entry.Shown && !entry.Rewarded && entry.Placement.Format == AdFormat.Rewarded)
                {
                    if (double.IsNaN(response.amount) || double.IsInfinity(response.amount) || response.amount < 0)
                    {
                        return;
                    }
                    entry.Rewarded = true;
                    Publish(new AdReward(Id, entry.Id, entry.Placement.Id, response.label, response.amount));
                }
                return;
            }
            if (response.requestId != activeId)
            {
                return;
            }
            if ((activeKind == "init" && response.kind == "initialized") ||
                (activeKind == "load" && response.kind == "loaded") ||
                (activeKind == "show" && response.kind == "closed"))
            {
                completion?.TrySetResult(true);
            }
            else if ((activeKind == "init" && response.kind == "init_failed") ||
                     (activeKind == "load" && response.kind == "load_failed") ||
                     (activeKind == "show" && response.kind == "display_failed"))
            {
                completion?.TrySetException(new InvalidOperationException("CSJ " + activeKind + " failed."));
            }
        }

        private void Publish(AdReward reward)
        {
            var handlers = RewardEarned;
            if (handlers == null)
            {
                return;
            }
            foreach (Action<AdReward> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(reward);
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }
        }

        private void Native(string operation, string request, string value, int format)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass("com.novorizon.ads.CsjBridge"))
            {
                bridge.CallStatic(operation, host.gameObject.name, request, value, format, userId);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            NovorizonCsj(host.gameObject.name, operation, request, value, format, userId);
#else
            throw new PlatformNotSupportedException("CSJ native ads require an Android or iOS player.");
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void NovorizonCsj(string receiver, string operation, string request, string value, int format, string userId);
#endif

        private void Ready(bool requireInitialized = true)
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use CSJ on the initialized Unity main thread.");
            }
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CsjAdProvider));
            }
            if (requireInitialized && !initialized)
            {
                throw new InvalidOperationException("Await CSJ initialization first.");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            Ready(false);
            if (completion != null)
            {
                throw new InvalidOperationException("Await the native CSJ operation before disposing its provider.");
            }
            foreach (var entry in entries.Values)
            {
                Native("release", entry.Id, "", 0);
            }
            disposed = true;
            loaded.Clear();
            entries.Clear();
            if (host != null)
            {
                host.Received = null;
                Object.Destroy(host.gameObject);
            }
            if (owner == this)
            {
                owner = null;
            }
            RewardEarned = null;
        }
    }
}
