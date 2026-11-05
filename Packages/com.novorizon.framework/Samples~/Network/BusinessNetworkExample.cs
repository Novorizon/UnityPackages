#if UNITY_2022_3_OR_NEWER
using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network;
using Game.Framework.Network.Samples;
using Game.Framework.Network.Samples.Messages;
using UnityEngine;

namespace Game.Framework.Samples
{
    // Configure Resources/NetworkSettings once. Business code only supplies account credentials.
    public sealed class BusinessNetworkExample : MonoBehaviour
    {
        private GameServerClient game;
        private IDisposable notices;
        private readonly CancellationTokenSource destroyed = new CancellationTokenSource();
        private bool isDestroyed;

        public async Task LoginAsync(string deviceId, string accountJwt, CancellationToken ct = default)
        {
            ThrowIfDestroyed();
            ct.ThrowIfCancellationRequested();
            if (game == null)
            {
                var current = GameServerClient.Instance;
                current.Initialize();
                var subscription = current.Network.Subscribe<ServerNotice>(MessageId.ServerNotice, OnNotice);
                game = current;
                notices = subscription;
            }
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyed.Token, ct))
                await game.ConnectAsync(deviceId, accountJwt, cancellation.Token);
        }
        public async Task<ProgressResponse> ReadProgressAsync(CancellationToken ct = default)
        {
            ThrowIfDestroyed();
            var shared = GameServerClient.Instance;
            if (!shared.Network.IsConnected) throw new InvalidOperationException("Connect and authenticate first.");
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyed.Token, ct))
                return await shared.GetProgressAsync(cancellation.Token);
        }
        private void ThrowIfDestroyed()
        {
            if (isDestroyed) throw new ObjectDisposedException(nameof(BusinessNetworkExample));
        }
        private void OnNotice(ServerNotice message) { Debug.Log(message.Text); }
        private void OnDestroy()
        {
            if (isDestroyed) return;
            isDestroyed = true;
            // This view owns its requests and subscription, not the application's shared connection.
            try { destroyed.Cancel(); }
            finally { notices?.Dispose(); notices = null; destroyed.Dispose(); }
        }
    }
}
#endif
