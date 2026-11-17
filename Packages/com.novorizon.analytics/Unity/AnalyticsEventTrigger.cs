using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Framework.Analytics.Unity
{
    /// <summary>定义统计参数类型可选的枚举值。</summary>
    public enum AnalyticsParameterType
    {
        Text,
        Integer,
        Number,
        Boolean
    }

    /// <summary>提供统计参数的实现。</summary>
    [Serializable]
    public sealed class AnalyticsParameter
    {
        public string key;
        public AnalyticsParameterType type;
        public string text;
        public long integer;
        public double number;
        public bool boolean;

        /// <summary>取得值。</summary>
        internal object Value()
        {
            switch (type)
            {
                case AnalyticsParameterType.Text:
                    return text ?? "";
                case AnalyticsParameterType.Integer:
                    return integer;
                case AnalyticsParameterType.Number:
                    return number;
                case AnalyticsParameterType.Boolean:
                    return boolean;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }

    /// <summary>Wire Track to Button/UnityEvent. The startup layer injects the project's AnalyticsManager.</summary>
    /// <remarks>提供统计事件 Trigger的实现。</remarks>
    public sealed class AnalyticsEventTrigger : MonoBehaviour
    {
        public string eventName = "button_click";
        public AnalyticsParameter[] parameters = Array.Empty<AnalyticsParameter>();
        private AnalyticsManager manager;
        public TelemetryReport LastReport { get; private set; }

        /// <summary>配置统计事件 Trigger。</summary>
        public void Configure(AnalyticsManager value)
        {
            AnalyticsBackend.MainThread();
            manager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>跟踪统计事件 Trigger。</summary>
        public void Track()
        {
            Emit();
        }

        /// <summary>生成统计事件 Trigger。</summary>
        public TelemetryReport Emit()
        {
            if (manager == null)
            {
                throw new InvalidOperationException("Inject the project's AnalyticsManager before triggering telemetry.");
            }
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var parameter in parameters ?? Array.Empty<AnalyticsParameter>())
            {
                if (parameter == null)
                {
                    throw new InvalidOperationException("Analytics parameters cannot be null.");
                }
                AnalyticsValues.Name(parameter.key);
                values.Add(parameter.key, parameter.Value());
            }
            LastReport = manager.LogEvent(eventName, values);
            return LastReport;
        }
    }
}
