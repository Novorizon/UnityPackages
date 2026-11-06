#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UI.Demo
{
    /// <summary>Demo-owned runtime templates. Each acquisition owns a lease, including delayed loads.</summary>
    public sealed class UIDemoAssetLoader : IUIAssetLoader
    {
        sealed class Template
        {
            internal readonly GameObject Prefab;
            internal int Leases;
            internal Template(GameObject prefab)
            {
                Prefab = prefab;
            }
        }

        readonly Dictionary<string, Template> templates = new();
        readonly GameObject container;
        readonly int delayMilliseconds;
        public int OutstandingLeases { get; private set; }
        public int AcquireCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public bool IsRetired { get; private set; }
        public bool IsDisposed { get; private set; }

        public UIDemoAssetLoader(string name, int delayMilliseconds = 0)
        {
            if (delayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
            }

            this.delayMilliseconds = delayMilliseconds;
            container = new GameObject(name + " Templates");
            container.SetActive(false);
            Object.DontDestroyOnLoad(container);
        }

        public void Register(string key, GameObject prefab)
        {
            if (IsRetired)
            {
                throw new ObjectDisposedException(nameof(UIDemoAssetLoader));
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A key is required.", nameof(key));
            }

            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (templates.ContainsKey(key))
            {
                throw new ArgumentException("Duplicate demo key: " + key, nameof(key));
            }

            prefab.SetActive(false);
            prefab.transform.SetParent(container.transform, false);
            foreach (Template existing in templates.Values)
            {
                if (!ReferenceEquals(existing.Prefab, prefab))
                {
                    continue;
                }

                templates.Add(key, existing);
                return;
            }

            templates.Add(key, new Template(prefab));
        }

        public async Task<GameObject?> LoadPrefabAsync(string prefabPath)
        {
            if (IsRetired)
            {
                throw new ObjectDisposedException(nameof(UIDemoAssetLoader));
            }

            if (!templates.TryGetValue(prefabPath, out Template? template))
            {
                return null;
            }

            // Reserve before awaiting: Retire cannot destroy an in-flight request's source.
            template.Leases++;
            OutstandingLeases++;
            AcquireCount++;
            try
            {
                if (delayMilliseconds != 0)
                {
                    await Task.Delay(delayMilliseconds);
                }

                return template.Prefab;
            }
            catch
            {
                Return(template);
                throw;
            }
        }

        public void ReleasePrefab(GameObject prefab)
        {
            foreach (Template template in templates.Values)
            {
                if (!ReferenceEquals(template.Prefab, prefab))
                {
                    continue;
                }

                if (template.Leases <= 0)
                {
                    throw new InvalidOperationException("The demo template lease was already returned.");
                }

                Return(template);
                return;
            }

            throw new InvalidOperationException("This prefab belongs to another demo loader.");
        }

        void Return(Template template)
        {
            template.Leases--;
            OutstandingLeases--;
            ReleaseCount++;
            CompleteRetirement();
        }

        /// <summary>Stop new loads, but retain every template until all in-flight/live view leases return.</summary>
        public void Retire()
        {
            IsRetired = true;
            CompleteRetirement();
        }

        void CompleteRetirement()
        {
            if (!IsRetired || IsDisposed || OutstandingLeases != 0)
            {
                return;
            }

            IsDisposed = true;
            Object.Destroy(container);
            templates.Clear();
        }
    }
}
