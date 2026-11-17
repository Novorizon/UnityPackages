using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Game.Framework.Analytics
{
    /// <summary>遥测不支持对应的异常。报告此操作特有的异常情况。</summary>
    public sealed class TelemetryUnsupportedException : NotSupportedException
    {
        /// <summary>使用调用方提供的message创建 TelemetryUnsupportedException。</summary>
        public TelemetryUnsupportedException(string message) : base(message)
        {
        }
    }

    /// <summary>定义遥测状态可选的枚举值。</summary>
    public enum TelemetryStatus
    {
        AcceptedBySdk,
        Disabled,
        NotStarted,
        Unsupported,
        Failed
    }

    /// <summary>提供遥测发货的实现。</summary>
    public sealed class TelemetryDelivery
    {
        public string Provider { get; }
        public TelemetryStatus Status { get; }
        /// <summary>Exception type only. Never contains event values, native response bodies or SDK keys.</summary>
        public string ErrorType { get; }

        /// <summary>使用调用方提供的provider、status、error创建 TelemetryDelivery。</summary>
        internal TelemetryDelivery(string provider, TelemetryStatus status, Exception error = null)
        {
            Provider = provider;
            Status = status;
            ErrorType = error?.GetType().Name;
        }
    }

    /// <summary>提供遥测报告的实现。</summary>
    public sealed class TelemetryReport
    {
        public IReadOnlyList<TelemetryDelivery> Deliveries { get; }
        public bool HasFailures { get; }

        /// <summary>使用调用方提供的deliveries创建 TelemetryReport。</summary>
        internal TelemetryReport(List<TelemetryDelivery> deliveries)
        {
            Deliveries = deliveries.AsReadOnly();
            foreach (var delivery in deliveries)
            {
                HasFailures |= delivery.Status == TelemetryStatus.Failed;
            }
        }
    }

    /// <summary>定义用户属性操作可选的枚举值。</summary>
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

    /// <summary>提供用户属性变更的实现。</summary>
    public sealed class UserPropertyChange
    {
        public UserPropertyOperation Operation { get; }
        public IReadOnlyDictionary<string, object> Properties { get; }

        /// <summary>使用调用方提供的operation、properties创建 UserPropertyChange。</summary>
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

    /// <summary>定义统计后端的调用契约。</summary>
    public interface IAnalyticsBackend : IDisposable
    {
        string Id { get; }
        bool IsStarted { get; }
        /// <summary>Requests SDK startup locally; completion does not establish server ingestion.</summary>
        /// <remarks>初始化统计后端。</remarks>
        void Initialize(string initialUserId);
        /// <summary>设置采集开启。</summary>
        void SetCollectionEnabled(bool enabled);
        /// <summary>设置用户标识。</summary>
        void SetUserId(string userId);
        /// <summary>记录事件。</summary>
        void LogEvent(string name, IReadOnlyDictionary<string, object> properties);
        /// <summary>检查是否支持统计后端。</summary>
        bool Supports(UserPropertyOperation operation);
        /// <summary>应用用户属性。</summary>
        void ApplyUserProperties(UserPropertyChange change);
        bool CanFlush { get; }
        /// <summary>刷新缓冲区统计后端。</summary>
        void Flush();
    }

    /// <summary>Statistical revenue only. It never verifies receipts or grants game inventory.</summary>
    /// <remarks>收入对应的事件。携带通知订阅者所需的事件数据。</remarks>
    public sealed class RevenueEvent
    {
        public string EventName { get; }
        public decimal Amount { get; }
        public string Currency { get; }
        public string TransactionId { get; }
        public IReadOnlyDictionary<string, object> Properties { get; }

        /// <summary>使用调用方提供的eventName、amount、currency创建 RevenueEvent。</summary>
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

    /// <summary>定义收入统计的调用契约。</summary>
    public interface IRevenueAnalytics
    {
        /// <summary>记录收入。</summary>
        void LogRevenue(RevenueEvent revenue);
    }

    /// <summary>Portable scalar events; provider-specific limits are additionally enforced by each adapter.</summary>
    /// <remarks>提供统计值的实现。</remarks>
    public static class AnalyticsValues
    {
        /// <summary>调用 string.IsNullOrEmpty 完成名称。</summary>
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

        /// <summary>返回Letter的判断结果。</summary>
        private static bool Letter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        /// <summary>根据传入参数取得用户标识。</summary>
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

        /// <summary>复制统计值。</summary>
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

        /// <summary>根据传入参数取得Invariant。</summary>
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
