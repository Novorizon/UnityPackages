using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Firebase
{
    /// <summary>定义统计的调用契约。</summary>
    public interface IAnalytics
    {
        /// <summary>设置采集开启。</summary>
        void SetCollectionEnabled(bool enabled);
        /// <summary>设置用户标识。</summary>
        void SetUserId(string userId);
        /// <summary>记录事件。</summary>
        void LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null);
    }

    /// <summary>定义崩溃上报器的调用契约。</summary>
    public interface ICrashReporter
    {
        /// <summary>设置采集开启。</summary>
        void SetCollectionEnabled(bool enabled);
        /// <summary>设置用户标识。</summary>
        void SetUserId(string userId);
        /// <summary>设置键。</summary>
        void SetKey(string key, string value);
        /// <summary>记录崩溃上报器。</summary>
        void Log(string message);
        /// <summary>上报崩溃上报器。</summary>
        void Report(Exception exception);
    }

    /// <summary>定义远程配置的调用契约。</summary>
    public interface IRemoteConfiguration
    {
        /// <summary>异步设置 Defaults。</summary>
        Task SetDefaultsAsync(IReadOnlyDictionary<string, object> defaults);
        /// <summary>异步拉取 AndActivate。</summary>
        Task<bool> FetchAndActivateAsync(TimeSpan minimumCacheAge, CancellationToken cancellationToken);
        /// <summary>获取字符串。</summary>
        string GetString(string key);
        /// <summary>获取整数64。</summary>
        long GetInt64(string key);
        /// <summary>获取双精度浮点数。</summary>
        double GetDouble(string key);
        /// <summary>获取布尔值。</summary>
        bool GetBoolean(string key);
    }

    /// <summary>提供Push 消息的实现。</summary>
    public sealed class PushMessage
    {
        public string Id { get; }
        public string Title { get; }
        public string Body { get; }
        public IReadOnlyDictionary<string, string> Data { get; }

        /// <summary>使用调用方提供的id、title、body创建 PushMessage。</summary>
        public PushMessage(string id, string title, string body, IDictionary<string, string> data)
        {
            Id = id;
            Title = title;
            Body = body;
            Data = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(data ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        }
    }

    /// <summary>定义IPush 推送的调用契约。</summary>
    public interface IPushMessaging : IDisposable
    {
        /// <summary>Firebase installation ID for the current device, not an authentication/session token.</summary>
        event Action<string> InstallationRegistered;
        event Action<PushMessage> MessageReceived;
        /// <summary>根据传入参数取得请求权限 Async。</summary>
        Task RequestPermissionAsync(CancellationToken cancellationToken);
        /// <summary>异步登记 IPush 推送。</summary>
        Task RegisterAsync(CancellationToken cancellationToken);
        /// <summary>异步撤销登记 IPush 推送。</summary>
        Task UnregisterAsync(CancellationToken cancellationToken);
        /// <summary>异步订阅 IPush 推送。</summary>
        Task SubscribeAsync(string topic, CancellationToken cancellationToken);
        /// <summary>异步取消订阅 IPush 推送。</summary>
        Task UnsubscribeAsync(string topic, CancellationToken cancellationToken);
    }
}
