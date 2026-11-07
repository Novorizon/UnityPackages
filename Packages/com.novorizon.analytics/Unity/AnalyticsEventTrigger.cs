using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Framework.Analytics.Unity
{
    public enum AnalyticsParameterType
    {
        Text,
        Integer,
        Number,
        Boolean
    }

    [Serializable]
    public sealed class AnalyticsParameter
    {
        public string key;
        public AnalyticsParameterType type;
        public string text;
        public long integer;
        public double number;
        public bool boolean;

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
    public sealed class AnalyticsEventTrigger : MonoBehaviour
    {
        public string eventName = "button_click";
        public AnalyticsParameter[] parameters = Array.Empty<AnalyticsParameter>();
        private AnalyticsManager manager;
        public TelemetryReport LastReport { get; private set; }

        public void Configure(AnalyticsManager value)
        {
            AnalyticsBackend.MainThread();
            manager = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void Track()
        {
            Emit();
        }

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
