using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Firebase
{
    public interface IAnalytics
    {
        void SetCollectionEnabled(bool enabled);
        void SetUserId(string userId);
        void LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null);
    }

    public interface ICrashReporter
    {
        void SetCollectionEnabled(bool enabled);
        void SetUserId(string userId);
        void SetKey(string key, string value);
        void Log(string message);
        void Report(Exception exception);
    }

    public interface IRemoteConfiguration
    {
        Task SetDefaultsAsync(IReadOnlyDictionary<string, object> defaults);
        Task<bool> FetchAndActivateAsync(TimeSpan minimumCacheAge, CancellationToken cancellationToken);
        string GetString(string key);
        long GetInt64(string key);
        double GetDouble(string key);
        bool GetBoolean(string key);
    }

    public sealed class PushMessage
    {
        public string Id { get; }
        public string Title { get; }
        public string Body { get; }
        public IReadOnlyDictionary<string, string> Data { get; }

        public PushMessage(string id, string title, string body, IDictionary<string, string> data)
        {
            Id = id;
            Title = title;
            Body = body;
            Data = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(data ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        }
    }

    public interface IPushMessaging : IDisposable
    {
        /// <summary>Firebase installation ID for the current device, not an authentication/session token.</summary>
        event Action<string> InstallationRegistered;
        event Action<PushMessage> MessageReceived;
        Task RequestPermissionAsync(CancellationToken cancellationToken);
        Task RegisterAsync(CancellationToken cancellationToken);
        Task UnregisterAsync(CancellationToken cancellationToken);
        Task SubscribeAsync(string topic, CancellationToken cancellationToken);
        Task UnsubscribeAsync(string topic, CancellationToken cancellationToken);
    }
}
