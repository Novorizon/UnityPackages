#if UNITY_2022_3_OR_NEWER
using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Samples.Messages;
using UnityEngine;

namespace Game.Framework.Network.Samples
{
    // Uses the application singleton. Automatic Unity dispatch needs no Update method.
    // Use a test account: WriteProgressAsync and SaveAsync change that account's server data.
    public sealed class TcpNetworkSample : MonoBehaviour
    {
        private readonly CancellationTokenSource destroyed = new CancellationTokenSource();
        private CancellationTokenSource requests = new CancellationTokenSource();
        private NetworkManager network;
        private GameServerClient game;
        private IDisposable notices;
        private bool isDestroyed;

        public long PlayerId => game?.PlayerId ?? 0;

        public NetworkManager Network
        {
            get { ThrowIfDestroyed(); return NetworkManager.Instance; }
        }

        public async Task ConnectAsync(string deviceId, string accountJwt, CancellationToken ct = default)
        {
            ThrowIfDestroyed();
            ct.ThrowIfCancellationRequested();
            if (network == null)
            {
                game = GameServerClient.Instance;
                game.Initialize();
                var current = game.Network;
                var subscription = current.Subscribe<ServerNotice>(MessageId.ServerNotice, OnServerNotice);
                current.Connected += OnConnected;
                current.Reconnected += OnReconnected;
                current.Disconnected += OnDisconnected;
                current.Error += OnNetworkError;
                network = current;
                notices = subscription;
            }
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyed.Token, ct))
                await game.ConnectAsync(deviceId, accountJwt, cancellation.Token);
            Debug.Log("Authenticated player: " + game.PlayerId);
        }

        public async Task<ProgressResponse> ReadProgressAsync(CancellationToken ct = default)
        {
            using (var cancellation = RequestCancellation(ct))
                return await RequireGame().GetProgressAsync(cancellation.Token);
        }

        public async Task<ProgressResponse> ReadProgressByMessageAsync(CancellationToken ct = default)
        {
            using (var cancellation = RequestCancellation(ct))
                return await RequireConnected().RequestAsync<ProgressResponse>(MessageId.ProgressRead,
                    new ProgressReadRequest(), cancellation.Token);
        }

        public async Task<ProgressResponse> WriteProgressAsync(int progress, long expectedVersion, CancellationToken ct = default)
        {
            using (var cancellation = RequestCancellation(ct))
                return await RequireGame().SetProgressAsync(progress, expectedVersion, cancellation.Token);
        }

        public async Task SaveAsync(byte[] data, CancellationToken ct = default)
        {
            using (var cancellation = RequestCancellation(ct))
                await RequireGame().SetSaveAsync(data, cancellation.Token);
        }

        public async Task<byte[]> LoadSaveAsync(CancellationToken ct = default)
        {
            using (var cancellation = RequestCancellation(ct))
            {
                var response = await RequireGame().GetSaveAsync(cancellation.Token);
                return response.Data.ToByteArray();
            }
        }

        public async Task<ProgressResponse[]> ReadConcurrentlyAsync(CancellationToken ct = default)
        {
            using (var cancellation = RequestCancellation(ct))
            {
                var current = RequireConnected();
                var operations = new Task<ProgressResponse>[5];
                for (int i = 0; i < operations.Length; i++) operations[i] = RequireGame().GetProgressAsync(cancellation.Token);
                return await Task.WhenAll(operations);
            }
        }

        public async Task SendWithoutWaitingForResponseAsync(CancellationToken ct = default)
        {
            using (var cancellation = RequestCancellation(ct))
            {
                // Demonstrates SendAsync with a read-only message. Its response is deliberately ignored.
                // For a normal business read, use RequestAsync to obtain the returned data.
                await RequireConnected().SendAsync(MessageId.ProgressRead, new ProgressReadRequest(), cancellation.Token);
            }
        }

        public void CancelPendingRequests()
        {
            if (isDestroyed) return;
            var previous = requests;
            requests = new CancellationTokenSource();
            previous.Cancel(); previous.Dispose();
            // Cancellation stops local waiting; already-sent writes may have executed on the server.
        }

        public void UnsubscribeNotices() { notices?.Dispose(); notices = null; }
        public Task DisconnectAsync()
        {
            ThrowIfDestroyed();
            return NetworkManager.IsCreated ? NetworkManager.Instance.DisconnectAsync() : Task.CompletedTask;
        }

        private CancellationTokenSource RequestCancellation(CancellationToken ct)
        {
            ThrowIfDestroyed();
            return CancellationTokenSource.CreateLinkedTokenSource(destroyed.Token, requests.Token, ct);
        }
        private void ThrowIfDestroyed()
        {
            if (isDestroyed) throw new ObjectDisposedException(nameof(TcpNetworkSample));
        }

        private GameServerClient RequireGame()
        {
            RequireConnected();
            return game ?? throw new InvalidOperationException("Initialize the GameServer sample first.");
        }

        private NetworkManager RequireConnected()
        {
            var shared = NetworkManager.Instance;
            if (!shared.IsConnected) throw new InvalidOperationException("Connect and authenticate first.");
            return shared;
        }

        private void OnConnected() { Debug.Log("Connected and authenticated."); }
        private void OnReconnected() { Debug.Log("Reconnected and authenticated again."); }
        private void OnDisconnected(Exception reason) { Debug.Log("Disconnected: " + reason?.Message); }
        private void OnNetworkError(Exception error) { Debug.LogException(error); }
        private void OnServerNotice(ServerNotice message) { Debug.Log("Server notice: " + message.Text); }

        private void OnDestroy()
        {
            if (isDestroyed) return;
            isDestroyed = true;
            destroyed.Cancel(); requests.Cancel();
            UnsubscribeNotices();
            if (network != null)
            {
                network.Connected -= OnConnected;
                network.Reconnected -= OnReconnected;
                network.Disconnected -= OnDisconnected;
                network.Error -= OnNetworkError;
            }
            // Keep the global connection alive for other pages.
            requests.Dispose(); destroyed.Dispose();
        }
    }
}
#endif
