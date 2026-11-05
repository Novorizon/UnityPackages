using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Dispatching;
using Game.Framework.Network.Samples.Messages;
using Google.Protobuf;

namespace Game.Framework.Network.Samples
{
    // Optional GameServer protocol example. The framework itself has no account or gameplay API.
    public sealed class GameServerClient : Singleton<GameServerClient>, IDisposable, IAsyncDisposable
    {
        private NetworkManager network;
        private GameServerAuthentication authentication;
        private LoginCredentialStore suppliedCredentials;
        private readonly SemaphoreSlim loginGate = new SemaphoreSlim(1, 1);
        private readonly bool isolated;

        public GameServerClient() { }

        internal GameServerClient(NetworkManager network, GameServerAuthentication authentication)
        {
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            isolated = true;
        }

        public NetworkManager Network { get { Initialize(); return network; } }
        public long PlayerId => authentication?.GetPlayerId(network) ?? 0;

        public void Initialize()
        {
            if (isolated)
            {
                if (network == null) throw new ObjectDisposedException(nameof(GameServerClient));
                return;
            }
            var shared = NetworkManager.Instance;
            if (ReferenceEquals(network, shared)) return;
            var credentials = new LoginCredentialStore();
            var login = new GameServerAuthentication(credentials.GetAsync);
            shared.Initialize(login.AuthenticateAsync);
            network = shared;
            authentication = login;
            suppliedCredentials = credentials;
        }

        // Configure once at application startup when credentials must be refreshed on reconnect.
        public void Configure(TcpClientOptions options, Func<CancellationToken, Task<LoginCredentials>> credentialsProvider,
            NetworkClientOptions clientOptions = null, INetworkDispatcher dispatcher = null)
        {
            if (isolated) throw new InvalidOperationException("This sample client already owns an explicit connection.");
            var login = new GameServerAuthentication(credentialsProvider);
            NetworkManager.Configure(options, clientOptions, dispatcher, login.AuthenticateAsync);
            network = NetworkManager.Instance;
            authentication = login;
            suppliedCredentials = null;
        }

        public Task ConnectAsync(CancellationToken ct = default) => Network.ConnectAsync(ct);

        public async Task ConnectAsync(string deviceId, string accessToken, CancellationToken ct = default)
        {
            var current = Network;
            if (suppliedCredentials == null)
                throw new InvalidOperationException("A custom credential provider is installed; use ConnectAsync(ct).");
            var credentials = new LoginCredentials(deviceId, accessToken);
            await loginGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await current.DisconnectAsync().ConfigureAwait(false);
                suppliedCredentials.Set(credentials);
                await current.ConnectAsync(ct).ConfigureAwait(false);
            }
            finally { loginGate.Release(); }
        }

        public Task DisconnectAsync() => network?.DisconnectAsync() ?? Task.CompletedTask;

        public Task<ProgressResponse> GetProgressAsync(CancellationToken ct = default) =>
            Network.RequestAsync<ProgressResponse>(MessageId.ProgressRead, new ProgressReadRequest(), ct);

        public Task<ProgressResponse> SetProgressAsync(int progress, long expectedVersion, CancellationToken ct = default) =>
            Network.RequestAsync<ProgressResponse>(MessageId.ProgressWrite,
                new ProgressWriteRequest { Progress = progress, ExpectedVersion = expectedVersion }, ct);

        public Task<SaveResponse> GetSaveAsync(CancellationToken ct = default) =>
            Network.RequestAsync<SaveResponse>(MessageId.SaveRead, new SaveReadRequest(), ct);

        public async Task SetSaveAsync(byte[] data, CancellationToken ct = default)
        {
            await Network.RequestAsync<SaveResponse>(MessageId.SaveWrite,
                new SaveWriteRequest { Data = ByteString.CopyFrom(data ?? throw new ArgumentNullException(nameof(data))) }, ct).ConfigureAwait(false);
        }

        public IDisposable SubscribeNotice(Action<ServerNotice> handler) => Network.Subscribe(MessageId.ServerNotice, handler);

        public Task<FacebookBindResponse> BindFacebookAsync(string accessToken, string expectedUserId, CancellationToken ct = default) =>
            Network.RequestAsync<FacebookBindResponse>(MessageId.FacebookBind,
                new FacebookBindRequest
                {
                    AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken)),
                    ExpectedUserId = expectedUserId ?? ""
                }, ct);

        public void Dispose()
        {
            var previous = network;
            network = null; authentication = null; suppliedCredentials = null;
            previous?.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            var previous = network;
            Dispose();
            return previous != null ? previous.DisposeAsync() : default;
        }
    }

    internal sealed class LoginCredentialStore
    {
        private LoginCredentials current;
        internal void Set(LoginCredentials value) { Volatile.Write(ref current, value); }
        internal Task<LoginCredentials> GetAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Volatile.Read(ref current) ?? throw new InvalidOperationException("Supply sample account credentials before connecting."));
        }
    }
}
