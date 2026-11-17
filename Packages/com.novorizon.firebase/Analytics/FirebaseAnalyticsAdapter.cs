using System;
using System.Collections.Generic;
using AnalyticsSdk = global::Firebase.Analytics.FirebaseAnalytics;
using Parameter = global::Firebase.Analytics.Parameter;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>Firebase 统计对应的适配器。将外部接口转换为当前模块约定。</summary>
    public sealed class FirebaseAnalyticsAdapter : IAnalytics
    {
        /// <summary>使用调用方提供的runtime创建 FirebaseAnalyticsAdapter。</summary>
        public FirebaseAnalyticsAdapter(FirebaseManager runtime)
        {
            FirebaseManager.MainThread();
            if (runtime == null || !runtime.IsInitialized)
            {
                throw new InvalidOperationException("Await Firebase initialization first.");
            }
        }

        /// <summary>设置采集开启。</summary>
        public void SetCollectionEnabled(bool enabled)
        {
            FirebaseManager.MainThread();
            AnalyticsSdk.SetAnalyticsCollectionEnabled(enabled);
        }

        /// <summary>设置用户标识。</summary>
        public void SetUserId(string userId)
        {
            FirebaseManager.MainThread();
            AnalyticsSdk.SetUserId(userId);
        }

        /// <summary>记录事件。</summary>
        public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null)
        {
            FirebaseManager.MainThread();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Event name is required.", nameof(name));
            }
            if (parameters == null)
            {
                AnalyticsSdk.LogEvent(name);
                return;
            }
            var values = new Parameter[parameters.Count];
            int index = 0;
            foreach (var pair in parameters)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException("Parameter names cannot be empty.", nameof(parameters));
                }
                switch (pair.Value)
                {
                    case string text:
                        values[index++] = new Parameter(pair.Key, text);
                        break;
                    case long number:
                        values[index++] = new Parameter(pair.Key, number);
                        break;
                    case int number:
                        values[index++] = new Parameter(pair.Key, (long)number);
                        break;
                    case bool boolean:
                        values[index++] = new Parameter(pair.Key, boolean ? 1L : 0L);
                        break;
                    case double number when !double.IsNaN(number) && !double.IsInfinity(number):
                        values[index++] = new Parameter(pair.Key, number);
                        break;
                    case float number when !float.IsNaN(number) && !float.IsInfinity(number):
                        values[index++] = new Parameter(pair.Key, (double)number);
                        break;
                    default:
                        throw new ArgumentException("Analytics values must be string, bool, integer or finite floating point.", nameof(parameters));
                }
            }
            AnalyticsSdk.LogEvent(name, values);
        }
    }
}
