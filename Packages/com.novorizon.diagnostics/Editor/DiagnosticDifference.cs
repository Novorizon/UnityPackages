using System;
using System.Collections.Generic;

namespace Game.Framework.Diagnostics.Editor
{
    /// <summary>提供诊断指标变更的实现。</summary>
    [Serializable]
    public sealed class DiagnosticMetricChange
    {
        public string name;
        public string kind;
        public string before;
        public string after;
    }

    /// <summary>提供诊断差异的实现。</summary>
    [Serializable]
    public sealed class DiagnosticDifference
    {
        public string baselineUtc;
        public string capturedUtc;
        public DiagnosticMetricChange[] metrics;
    }

    /// <summary>提供诊断导出数据的实现。</summary>
    [Serializable]
    public sealed class DiagnosticExport
    {
        public DiagnosticSnapshot snapshot;
        public DiagnosticSnapshot baseline;
        public DiagnosticDifference difference;
    }

    /// <summary>Compares captured values only; never samples services or mutates resources.</summary>
    /// <remarks>对比两次诊断快照，定位指标增减和变化。</remarks>
    public static class DiagnosticSnapshotComparer
    {
        /// <summary>以键匹配前后两次采样，输出对应指标的差异。</summary>
        public static DiagnosticDifference Compare(DiagnosticSnapshot baseline, DiagnosticSnapshot current)
        {
            if (baseline == null || current == null)
            {
                throw new ArgumentNullException(baseline == null ? nameof(baseline) : nameof(current));
            }
            var oldMetrics = Metrics(baseline.metrics);
            var newMetrics = Metrics(current.metrics);
            var metricChanges = new List<DiagnosticMetricChange>();
            foreach (string key in Union(oldMetrics.Keys, newMetrics.Keys))
            {
                bool hadBefore = oldMetrics.TryGetValue(key, out string before);
                bool hasAfter = newMetrics.TryGetValue(key, out string after);
                if (hadBefore && hasAfter && before == after)
                {
                    continue;
                }
                metricChanges.Add(new DiagnosticMetricChange
                {
                    name = key,
                    kind = !hadBefore ? "Added" : !hasAfter ? "Removed" : "Changed",
                    before = before,
                    after = after
                });
            }
            return new DiagnosticDifference
            {
                baselineUtc = baseline.capturedUtc,
                capturedUtc = current.capturedUtc,
                metrics = metricChanges.ToArray()
            };
        }

        /// <summary>根据传入参数取得指标。</summary>
        private static Dictionary<string, string> Metrics(DiagnosticMetric[] metrics)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var metric in metrics ?? Array.Empty<DiagnosticMetric>())
            {
                if (metric != null && metric.name != null)
                {
                    result[metric.name] = metric.value;
                }
            }
            return result;
        }

        /// <summary>根据传入参数取得并集。</summary>
        private static SortedSet<string> Union(IEnumerable<string> left, IEnumerable<string> right)
        {
            var keys = new SortedSet<string>(left, StringComparer.Ordinal);
            keys.UnionWith(right);
            return keys;
        }
    }
}
