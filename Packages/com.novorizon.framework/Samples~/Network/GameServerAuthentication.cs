using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Samples.Messages;

namespace Game.Framework.Network.Samples
{
    // Owns only the current game's login protocol and identity, not connection management.
    internal sealed class GameServerAuthentication
    {
        private sealed class AuthenticatedPlayer
        {
            internal long ConnectionId;
            internal long PlayerId;
        }

        private readonly Func<CancellationToken, Task<LoginCredentials>> credentialsProvider;
        private AuthenticatedPlayer player;

        internal GameServerAuthentication(Func<CancellationToken, Task<LoginCredentials>> credentialsProvider)
        {
            this.credentialsProvider = credentialsProvider ?? throw new ArgumentNullException(nameof(credentialsProvider));
        }

        internal async Task AuthenticateAsync(NetworkManager.AuthenticationContext context, CancellationToken ct)
        {
            // Every reconnect obtains fresh credentials. The provider may run on an I/O thread.
            var credentials = await credentialsProvider(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (credentials == null) throw new InvalidOperationException("Credential provider returned null.");

            LoginResponse response;
            try
            {
                response = await context.RequestAsync((int)MessageId.Login,
                    new LoginRequest { DeviceId = credentials.DeviceId, AccessToken = credentials.AccessToken },
                    LoginResponse.Parser, ct).ConfigureAwait(false);
            }
            catch (RemoteRequestException error) when (error.ResultCode == (int)GameServerResultCode.InvalidCredentials ||
                error.ResultCode == (int)GameServerResultCode.NotAuthenticated)
            {
                throw new ConnectionRejectedException("GameServer rejected the account credentials.", error);
            }
            if (response.PlayerId <= 0 || response.DeviceId != credentials.DeviceId)
                throw new NetworkProtocolException("Login response identity mismatch.");

            var next = new AuthenticatedPlayer { ConnectionId = context.ConnectionId, PlayerId = response.PlayerId };
            while (true)
            {
                var previous = Volatile.Read(ref player);
                if (previous != null && previous.ConnectionId > next.ConnectionId) break;
                if (ReferenceEquals(Interlocked.CompareExchange(ref player, next, previous), previous)) break;
            }
        }

        internal long GetPlayerId(NetworkManager session)
        {
            var value = Volatile.Read(ref player);
            return value != null && session != null && session.IsConnected && value.ConnectionId == session.ConnectionId ? value.PlayerId : 0;
        }
    }
}
