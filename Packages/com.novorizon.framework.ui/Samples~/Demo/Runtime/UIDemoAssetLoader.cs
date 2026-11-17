#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UI.Demo
{
    /// <summary>Demo-owned runtime templates. Each acquisition owns a lease, including delayed loads.</summary>
    /// <remarks>提供界面示例资源加载器的实现。</remarks>
    public sealed class UIDemoAssetLoader : IUIAssetLoader
    {
        /// <summary>表示界面示例资源加载器中的模板。</summary>
        sealed class Template
        {
            internal readonly GameObject Prefab;
            internal int Leases;
            /// <summary>使用调用方提供的prefab创建 Template。</summary>
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

        /// <summary>使用调用方提供的name、delayMilliseconds创建 UIDemoAssetLoader。</summary>
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

        /// <summary>登记界面示例资源加载器。</summary>
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

        /// <summary>异步加载预制件。</summary>
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

        /// <summary>释放预制件。</summary>
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

        /// <summary>归还当前租用的对象。</summary>
        void Return(Template template)
        {
            template.Leases--;
            OutstandingLeases--;
            ReleaseCount++;
            CompleteRetirement();
        }

        /// <summary>Stop new loads, but retain every template until all in-flight/live view leases return.</summary>
        /// <remarks>终止使用界面示例资源加载器。</remarks>
        public void Retire()
        {
            IsRetired = true;
            CompleteRetirement();
        }

        /// <summary>完成 Retirement。</summary>
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
