using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Game.Framework.Diagnostics.Editor
{
    /// <summary>提供诊断指标的实现。</summary>
    [Serializable]
    public sealed class DiagnosticMetric
    {
        public string name;
        public string value;
    }

    /// <summary>诊断对应的快照。保存采样时刻的数据，供其他流程读取。</summary>
    [Serializable]
    public sealed class DiagnosticSnapshot
    {
        public string capturedUtc;
        public DiagnosticMetric[] metrics;
        public DiagnosticLease[] leases = Array.Empty<DiagnosticLease>();
    }

    /// <summary>诊断对应的租约。记录资源的使用权，由持有者在使用结束时归还。</summary>
    [Serializable]
    public sealed class DiagnosticLease
    {
        public string sessionId;
        public long leaseId;
        public string owner;
        public long scopeId;
        public int sceneHandle;
        public string sceneName;
        public string location;
        public string assetType;
        public string state;
        public string creationStack;
        public string source = "Independent acquisition";
    }

    /// <summary>Editor-main-thread sampling of public, read-only package APIs. Never accesses Instance.</summary>
    /// <remarks>提供包诊断的实现。</remarks>
    public static class PackageDiagnostics
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        /// <summary>采集包诊断。</summary>
        public static DiagnosticSnapshot Capture()
        {
            var metrics = new List<DiagnosticMetric>();
            CaptureService(metrics, "Game.Framework.UnityThread", false, "PendingCount");
            CaptureService(metrics, "Game.Framework.Timing.TimerManager", true, "PendingCount");
            CaptureService(metrics, "Game.Framework.Pooling.GameObjectPool", false, "PoolCount", "TotalActiveCount", "TotalInactiveCount");
            CaptureService(metrics, "Game.Framework.Pooling.ReferencePool", false, "RegisteredSharedPoolCount");
            CaptureService(metrics, "Game.Framework.Network.NetworkManager", true, "State", "PendingRequestCount", "PendingSendCount", "PendingSendBytes");
            CaptureService(metrics, "Game.Framework.Scenes.SceneManager", true, "LoadedCount");
            CaptureService(metrics, "Game.Framework.Audio.AudioManager", true, "PlayingCount", "PooledCount", "IsPaused");
            CaptureService(metrics, "UI.UIManager", true, "IsInitialized", "ActiveViewCount", "CachedViewCount", "Pages.Count", "Popups.Count", "Overlays.IsBlocking");
            CaptureService(metrics, "Game.Resource.ResourceManager", true, "OwnedAssetCount");
            CaptureService(metrics, "GpuAnimatorManager", true, "Count");
            CaptureService(metrics, "Game.Framework.Localization.LocalizationManager", true, "IsInitialized", "Language", "KeyCount");
            var leases = CaptureOwnership(metrics);
            return new DiagnosticSnapshot
            {
                capturedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                metrics = metrics.ToArray(),
                leases = leases
            };
        }

        /// <summary>采集所有权。</summary>
        private static DiagnosticLease[] CaptureOwnership(List<DiagnosticMetric> metrics)
        {
            Type type = FindType("Game.Resource.ResourceManager");
            if (type == null)
            {
                return Array.Empty<DiagnosticLease>();
            }
            try
            {
                var accessor = type.GetMethod("TryGetInstance", PublicStatic, null, new[] { type.MakeByRefType() }, null);
                if (accessor == null || accessor.ReturnType != typeof(bool))
                {
                    Add(metrics, "Resource ownership", "No read-only singleton accessor");
                    return Array.Empty<DiagnosticLease>();
                }
                var arguments = new object[] { null };
                if (!(bool)accessor.Invoke(null, arguments))
                {
                    return Array.Empty<DiagnosticLease>();
                }
                var capture = type.GetMethod("CaptureLeaseDiagnostics", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (capture == null)
                {
                    Add(metrics, "Resource ownership", "Adapter does not provide lease diagnostics");
                    return Array.Empty<DiagnosticLease>();
                }
                object captured = capture.Invoke(arguments[0], null);
                var leases = new List<DiagnosticLease>(captured is ICollection collection ? collection.Count : 0);
                if (captured is IEnumerable records)
                {
                    Type recordType = null;
                    LeaseReader reader = null;
                    foreach (object record in records)
                    {
                        Type typeOfRecord = record.GetType();
                        if (recordType != typeOfRecord)
                        {
                            recordType = typeOfRecord;
                            reader = new LeaseReader(recordType);
                        }
                        leases.Add(reader.Read(record));
                    }
                }
                Add(metrics, "Resource ownership", "Independent acquisitions only; not native refcounts or memory bytes");
                return leases.ToArray();
            }
            catch (Exception error)
            {
                Add(metrics, "Resource ownership", "Error: " + error.GetBaseException().Message);
                return Array.Empty<DiagnosticLease>();
            }
        }

        /// <summary>表示包诊断中的租约读取器。</summary>
        private sealed class LeaseReader
        {
            private static readonly string[] names =
            {
                "SessionId", "LeaseId", "Owner", "ScopeId", "SceneHandle", "SceneName", "Location", "AssetType", "State", "CreationStack"
            };
            private readonly PropertyInfo[] properties = new PropertyInfo[names.Length];

            /// <summary>使用调用方提供的type创建 LeaseReader。</summary>
            internal LeaseReader(Type type)
            {
                for (int index = 0; index < properties.Length; index++)
                {
                    properties[index] = ReadableProperty(type, names[index], BindingFlags.Public | BindingFlags.Instance);
                }
            }

            /// <summary>读取传入的 record。</summary>
            internal DiagnosticLease Read(object record)
            {
                return new DiagnosticLease
                {
                    sessionId = (string)properties[0].GetValue(record),
                    leaseId = (long)properties[1].GetValue(record),
                    owner = (string)properties[2].GetValue(record),
                    scopeId = (long)properties[3].GetValue(record),
                    sceneHandle = (int)properties[4].GetValue(record),
                    sceneName = (string)properties[5].GetValue(record),
                    location = (string)properties[6].GetValue(record),
                    assetType = (string)properties[7].GetValue(record),
                    state = (string)properties[8].GetValue(record),
                    creationStack = (string)properties[9].GetValue(record)
                };
            }
        }

        /// <summary>根据传入参数取得Readable 属性。</summary>
        private static PropertyInfo ReadableProperty(Type type, string name, BindingFlags flags)
        {
            var property = type.GetProperty(name, flags);
            if (property == null || property.GetGetMethod(false) == null || property.GetIndexParameters().Length != 0)
            {
                throw new MissingMemberException(type.Name, name);
            }
            return property;
        }

        /// <summary>查找类型。</summary>
        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        /// <summary>采集服务。</summary>
        private static void CaptureService(List<DiagnosticMetric> metrics, string typeName, bool singleton, params string[] properties)
        {
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);
                if (type != null)
                {
                    break;
                }
            }

            if (type == null)
            {
                Add(metrics, typeName, "Package not loaded");
                return;
            }

            foreach (var metric in Read(type, singleton, properties))
            {
                metrics.Add(metric);
            }
        }

        /// <summary>Only TryGetInstance is permitted for singleton acquisition. Errors remain visible as metrics.</summary>
        /// <remarks>读取传入的 type、singleton。</remarks>
        public static DiagnosticMetric[] Read(Type type, bool singleton, params string[] paths)
        {
            if (type == null || paths == null)
            {
                throw new ArgumentNullException(type == null ? nameof(type) : nameof(paths));
            }

            var metrics = new List<DiagnosticMetric>();
            object instance = null;
            try
            {
                if (singleton)
                {
                    var accessor = type.GetMethod("TryGetInstance", PublicStatic, null, new[] { type.MakeByRefType() }, null);
                    if (accessor == null || accessor.ReturnType != typeof(bool))
                    {
                        Add(metrics, type.Name, "No read-only singleton accessor");
                        return metrics.ToArray();
                    }

                    var arguments = new object[] { null };
                    if (!(bool)accessor.Invoke(null, arguments))
                    {
                        Add(metrics, type.Name, "Not created / released");
                        return metrics.ToArray();
                    }

                    instance = arguments[0];
                }
            }
            catch (Exception error)
            {
                Add(metrics, type.Name, "Error: " + error.GetBaseException().Message);
                return metrics.ToArray();
            }

            foreach (string path in paths)
            {
                try
                {
                    object value = instance;
                    Type owner = type;
                    bool isStatic = !singleton;
                    foreach (string part in path.Split('.'))
                    {
                        var property = ReadableProperty(owner, part, isStatic ? PublicStatic : BindingFlags.Public | BindingFlags.Instance);

                        value = property.GetValue(value);
                        if (value == null)
                        {
                            break;
                        }

                        owner = value.GetType();
                        isStatic = false;
                    }

                    Add(metrics, type.Name + "." + path, value == null ? "Unavailable" : Convert.ToString(value, CultureInfo.InvariantCulture));
                }
                catch (Exception error)
                {
                    Add(metrics, type.Name + "." + path, "Error: " + error.GetBaseException().Message);
                }
            }

            return metrics.ToArray();
        }

        /// <summary>添加包诊断。</summary>
        private static void Add(List<DiagnosticMetric> metrics, string name, string value)
        {
            metrics.Add(new DiagnosticMetric { name = name, value = value });
        }
    }
}
