using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Auth
{
    /// <summary>定义身份厂商适配器可选的枚举值。</summary>
    public enum IdentityProvider
    {
        Apple,
        Google,
        Facebook
    }

    /// <summary>定义身份令牌种类可选的枚举值。</summary>
    public enum IdentityTokenKind
    {
        IdToken,
        AccessToken
    }

    /// <summary>Short-lived provider proof. Never log or persist its secret fields.</summary>
    /// <remarks>提供身份凭据的实现。</remarks>
    public sealed class IdentityProof : IDisposable
    {
        private string token;
        private string rawNonce;
        private string accessToken;
        public IdentityProvider Provider { get; }
        public IdentityTokenKind Kind { get; }
        public string Token => token ?? throw new ObjectDisposedException(nameof(IdentityProof));
        public string RawNonce => token != null ? rawNonce : throw new ObjectDisposedException(nameof(IdentityProof));
        public string AccessToken => token != null ? accessToken : throw new ObjectDisposedException(nameof(IdentityProof));

        /// <summary>使用调用方提供的provider、kind、token创建 IdentityProof。</summary>
        public IdentityProof(
            IdentityProvider provider,
            IdentityTokenKind kind,
            string token,
            string rawNonce = null,
            string accessToken = null)
        {
            if (!Enum.IsDefined(typeof(IdentityProvider), provider) || !Enum.IsDefined(typeof(IdentityTokenKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(provider));
            }
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("A provider token is required.", nameof(token));
            }
            Provider = provider;
            Kind = kind;
            this.token = token;
            this.rawNonce = rawNonce;
            this.accessToken = accessToken;
        }

        /// <summary>结束身份凭据对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            token = rawNonce = accessToken = null;
        }

        /// <summary>格式化当前值，供显示或诊断使用。</summary>
        public override string ToString() => Provider + " proof (redacted)";
    }

    /// <summary>提供认证用户的实现。</summary>
    public sealed class AuthUser
    {
        public string Id { get; }
        public bool IsAnonymous { get; }
        public IReadOnlyList<string> LinkedProviders { get; }

        /// <summary>使用调用方提供的id、anonymous、linkedProviders创建 AuthUser。</summary>
        public AuthUser(string id, bool anonymous, IEnumerable<string> linkedProviders)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("The verified backend user ID is required.", nameof(id));
            }
            Id = id;
            IsAnonymous = anonymous;
            LinkedProviders = new List<string>(linkedProviders ?? Array.Empty<string>()).AsReadOnly();
        }
    }

    /// <summary>定义身份厂商适配器的调用契约。</summary>
    public interface IIdentityProvider : IDisposable
    {
        IdentityProvider Provider { get; }
        /// <summary>异步取得身份厂商适配器。</summary>
        Task<IdentityProof> AcquireAsync(CancellationToken cancellationToken);
    }

    /// <summary>The backend verifies proofs and owns identity conflicts. It must not merge by email.</summary>
    /// <remarks>定义认证后端的调用契约。</remarks>
    public interface IAuthBackend : IDisposable
    {
        AuthUser CurrentUser { get; }
        /// <summary>根据传入参数取得SignIn 以匿名身份 Async。</summary>
        Task<AuthUser> SignInAnonymouslyAsync(CancellationToken cancellationToken);
        /// <summary>根据传入参数取得SignInAsync。</summary>
        Task<AuthUser> SignInAsync(IdentityProof proof, CancellationToken cancellationToken);
        /// <summary>异步关联认证后端。</summary>
        Task<AuthUser> LinkAsync(string expectedUserId, IdentityProof proof, CancellationToken cancellationToken);
        /// <summary>异步解除关联认证后端。</summary>
        Task<AuthUser> UnlinkAsync(string expectedUserId, string providerId, CancellationToken cancellationToken);
        /// <summary>异步获取会话令牌。</summary>
        Task<string> GetSessionTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
        /// <summary>结束当前登录会话。</summary>
        void SignOut();
    }
}
