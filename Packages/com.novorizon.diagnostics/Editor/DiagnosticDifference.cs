using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Framework.Diagnostics.Editor
{
    [Serializable]
    public sealed class DiagnosticMetricChange
    {
        public string name;
        public string kind;
        public string before;
        public string after;
    }

    [Serializable]
    public sealed class DiagnosticLeaseChange
    {
        public string kind;
        public DiagnosticLease before;
        public DiagnosticLease after;
    }

    [Serializable]
    public sealed class DiagnosticDifference
    {
        public string baselineUtc;
        public string capturedUtc;
        public DiagnosticMetricChange[] metrics;
        public DiagnosticLeaseChange[] leases;
    }

    [Serializable]
    public sealed class DiagnosticExport
    {
        public DiagnosticSnapshot snapshot;
        public DiagnosticSnapshot baseline;
        public DiagnosticDifference difference;
    }

    /// <summary>Compares captured values only; never samples services or mutates resources.</summary>
    public static class DiagnosticSnapshotComparer
    {
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
            var oldLeases = Leases(baseline.leases);
            var newLeases = Leases(current.leases);
            var leaseChanges = new List<DiagnosticLeaseChange>();
            foreach (string key in Union(oldLeases.Keys, newLeases.Keys))
            {
                bool hadBefore = oldLeases.TryGetValue(key, out DiagnosticLease before);
                bool hasAfter = newLeases.TryGetValue(key, out DiagnosticLease after);
                if (hadBefore && hasAfter && Same(before, after))
                {
                    continue;
                }
                leaseChanges.Add(new DiagnosticLeaseChange
                {
                    kind = !hadBefore ? "Added" : !hasAfter ? "Removed" : "Changed",
                    before = before,
                    after = after
                });
            }
            return new DiagnosticDifference
            {
                baselineUtc = baseline.capturedUtc,
                capturedUtc = current.capturedUtc,
                metrics = metricChanges.ToArray(),
                leases = leaseChanges.ToArray()
            };
        }

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

        private static Dictionary<string, DiagnosticLease> Leases(DiagnosticLease[] leases)
        {
            var result = new Dictionary<string, DiagnosticLease>(StringComparer.Ordinal);
            foreach (var lease in leases ?? Array.Empty<DiagnosticLease>())
            {
                if (lease != null)
                {
                    result[(lease.sessionId ?? "") + "/" + lease.leaseId.ToString(CultureInfo.InvariantCulture)] = lease;
                }
            }
            return result;
        }

        private static SortedSet<string> Union(IEnumerable<string> left, IEnumerable<string> right)
        {
            var keys = new SortedSet<string>(left, StringComparer.Ordinal);
            keys.UnionWith(right);
            return keys;
        }

        private static bool Same(DiagnosticLease left, DiagnosticLease right)
        {
            return left.owner == right.owner && left.scopeId == right.scopeId &&
                left.sceneHandle == right.sceneHandle && left.sceneName == right.sceneName &&
                left.location == right.location && left.assetType == right.assetType &&
                left.state == right.state && left.creationStack == right.creationStack && left.source == right.source;
        }
    }
}
