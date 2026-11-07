using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Game.Framework.Diagnostics.Editor
{
    [Serializable]
    public sealed class DiagnosticMetric
    {
        public string name;
        public string value;
    }

    [Serializable]
    public sealed class DiagnosticSnapshot
    {
        public string capturedUtc;
        public DiagnosticMetric[] metrics;
    }

    /// <summary>Editor-main-thread sampling of public, read-only package APIs. Never accesses Instance.</summary>
    public static class PackageDiagnostics
    {
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

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
            return new DiagnosticSnapshot
            {
                capturedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                metrics = metrics.ToArray()
            };
        }

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
                        var property = owner.GetProperty(part, isStatic ? PublicStatic : BindingFlags.Public | BindingFlags.Instance);
                        if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0)
                        {
                            throw new MissingMemberException(owner.Name, part);
                        }

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

        private static void Add(List<DiagnosticMetric> metrics, string name, string value)
        {
            metrics.Add(new DiagnosticMetric { name = name, value = value });
        }
    }
}
