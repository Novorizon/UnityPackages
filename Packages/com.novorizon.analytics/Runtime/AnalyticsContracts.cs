using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Game.Framework.Analytics
{
    public sealed class TelemetryUnsupportedException : NotSupportedException
    {
        public TelemetryUnsupportedException(string message) : base(message)
        {
        }
    }

    public enum TelemetryStatus
    {
        AcceptedBySdk,
        Disabled,
        NotStarted,
        Unsupported,
        Failed
    }

    public sealed class TelemetryDelivery
    {
        public string Provider { get; }
        public TelemetryStatus Status { get; }
        /// <summary>Exception type only. Never contains event values, native response bodies or SDK keys.</summary>
        public string ErrorType { get; }

        internal TelemetryDelivery(string provider, TelemetryStatus status, Exception error = null)
        {
            Provider = provider;
            Status = status;
            ErrorType = error?.GetType().Name;
        }
    }

    public sealed class TelemetryReport
    {
        public IReadOnlyList<TelemetryDelivery> Deliveries { get; }
        public bool HasFailures { get; }

        internal TelemetryReport(List<TelemetryDelivery> deliveries)
        {
            Deliveries = deliveries.AsReadOnly();
            foreach (var delivery in deliveries)
            {
                HasFailures |= delivery.Status == TelemetryStatus.Failed;
            }
        }
    }

    public enum UserPropertyOperation
    {
        Set,
        SetOnce,
        Add,
        Unset,
        Append,
        AppendUnique,
        DeleteProfile
    }

    public sealed class UserPropertyChange
    {
        public UserPropertyOperation Operation { get; }
        public IReadOnlyDictionary<string, object> Properties { get; }

        public UserPropertyChange(UserPropertyOperation operation, IReadOnlyDictionary<string, object> properties = null)
        {
            if (!Enum.IsDefined(typeof(UserPropertyOperation), operation))
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }
            Operation = operation;
            Properties = AnalyticsValues.Copy(properties, allowLists: operation == UserPropertyOperation.Append || operation == UserPropertyOperation.AppendUnique, allowNull: operation == UserPropertyOperation.Unset);
            if (operation == UserPropertyOperation.DeleteProfile && Properties.Count != 0)
            {
                throw new ArgumentException("DeleteProfile does not accept properties.", nameof(properties));
            }
            foreach (var pair in Properties)
            {
                if (operation == UserPropertyOperation.Add && !(pair.Value is long) && !(pair.Value is double))
                {
                    throw new ArgumentException("User Add accepts finite numbers only.", nameof(properties));
                }
                if ((operation == UserPropertyOperation.Append || operation == UserPropertyOperation.AppendUnique) && !(pair.Value is IReadOnlyList<string>))
                {
                    throw new ArgumentException("Append accepts string lists only.", nameof(properties));
                }
            }
        }
    }

    public interface IAnalyticsBackend : IDisposable
    {
        string Id { get; }
        bool IsStarted { get; }
        /// <summary>Requests SDK startup locally; completion does not establish server ingestion.</summary>
        void Initialize(string initialUserId);
        void SetCollectionEnabled(bool enabled);
        void SetUserId(string userId);
        void LogEvent(string name, IReadOnlyDictionary<string, object> properties);
        bool Supports(UserPropertyOperation operation);
        void ApplyUserProperties(UserPropertyChange change);
        bool CanFlush { get; }
        void Flush();
    }

    /// <summary>Statistical revenue only. It never verifies receipts or grants game inventory.</summary>
    public sealed class RevenueEvent
    {
        public string EventName { get; }
        public decimal Amount { get; }
        public string Currency { get; }
        public string TransactionId { get; }
        public IReadOnlyDictionary<string, object> Properties { get; }

        public RevenueEvent(string eventName, decimal amount, string currency, string transactionId, IReadOnlyDictionary<string, object> properties = null)
        {
            AnalyticsValues.Name(eventName);
            if (amount < 0 || currency == null || currency.Length != 3 || string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 256)
            {
                throw new ArgumentException("Revenue needs a nonnegative amount, ISO currency and stable transaction ID.");
            }
            foreach (char c in currency)
            {
                if (c < 'A' || c > 'Z')
                {
                    throw new ArgumentException("Use a three-letter uppercase ISO currency.", nameof(currency));
                }
            }
            EventName = eventName;
            Amount = amount;
            Currency = currency;
            TransactionId = transactionId;
            Properties = AnalyticsValues.Copy(properties);
        }
    }

    public interface IRevenueAnalytics
    {
        void LogRevenue(RevenueEvent revenue);
    }

    /// <summary>Portable scalar events; provider-specific limits are additionally enforced by each adapter.</summary>
    public static class AnalyticsValues
    {
        public static void Name(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !Letter(value[0]))
            {
                throw new ArgumentException("Telemetry names must start with an ASCII letter and contain 1-64 letters, digits or underscores.");
            }
            foreach (char c in value)
            {
                if (!Letter(c) && (c < '0' || c > '9') && c != '_')
                {
                    throw new ArgumentException("Telemetry names contain letters, digits or underscores only.");
                }
            }
        }

        private static bool Letter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        public static string UserId(string value)
        {
            if (value == null)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
            {
                throw new ArgumentException("Use null to log out, or a nonempty stable game user ID of at most 1024 characters.");
            }
            return value;
        }

        public static IReadOnlyDictionary<string, object> Copy(IReadOnlyDictionary<string, object> source, bool allowLists = false, bool allowNull = false)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (source != null)
            {
                if (source.Count > 64)
                {
                    throw new ArgumentException("A telemetry payload supports at most 64 properties.", nameof(source));
                }
                foreach (var pair in source)
                {
                    Name(pair.Key);
                    object value;
                    switch (pair.Value)
                    {
                        case null when allowNull:
                            value = null;
                            break;
                        case string text when text.Length <= 4096:
                            value = text;
                            break;
                        case bool boolean:
                            value = boolean;
                            break;
                        case int number:
                            value = (long)number;
                            break;
                        case long number:
                            value = number;
                            break;
                        case float number when !float.IsNaN(number) && !float.IsInfinity(number):
                            value = (double)number;
                            break;
                        case double number when !double.IsNaN(number) && !double.IsInfinity(number):
                            value = number;
                            break;
                        case IEnumerable<string> list when allowLists:
                            var values = new List<string>();
                            foreach (string item in list)
                            {
                                if (item == null || item.Length > 4096 || values.Count == 64)
                                {
                                    throw new ArgumentException("String lists support at most 64 non-null values of at most 4096 characters.");
                                }
                                values.Add(item);
                            }
                            value = values.AsReadOnly();
                            break;
                        default:
                            throw new ArgumentException("Telemetry values must be string, bool, integer or finite floating point; profile append accepts string lists.");
                    }
                    result.Add(pair.Key, value);
                }
            }
            return new ReadOnlyDictionary<string, object>(result);
        }

        public static string Invariant(object value)
        {
            switch (value)
            {
                case string text:
                    return text;
                case bool boolean:
                    return boolean ? "true" : "false";
                case long number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case double number when !double.IsNaN(number) && !double.IsInfinity(number):
                    return number.ToString("R", CultureInfo.InvariantCulture);
                default:
                    throw new ArgumentException("A scalar telemetry value is required.", nameof(value));
            }
        }
    }
}
