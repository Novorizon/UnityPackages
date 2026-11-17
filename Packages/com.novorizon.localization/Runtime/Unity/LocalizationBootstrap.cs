using System;
using UnityEngine;

namespace Game.Framework.Localization.Unity
{
    /// <summary>Configured initialization owner. Put on the project entry, before opening localized UI.</summary>
    /// <remarks>提供本地化启动入口的实现。</remarks>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class LocalizationBootstrap : MonoBehaviour
    {
        [SerializeField] private LocalizationSettings settings;
        [SerializeField] private bool initializeOnAwake = true;
        [SerializeField] private bool releaseOnDestroy = true;
        private LocalizationManager owned;
        private bool stopped;

        public LocalizationSettings Settings
        {
            get => settings;
            set
            {
                if (owned != null || stopped)
                {
                    throw new InvalidOperationException("Configure settings before initializing this bootstrap.");
                }
                settings = value;
            }
        }
        public bool InitializeOnAwake { get => initializeOnAwake; set => initializeOnAwake = value; }
        public bool ReleaseOnDestroy { get => releaseOnDestroy; set => releaseOnDestroy = value; }
        public LocalizationManager Manager => owned;

        /// <summary>初始化本地化启动入口。</summary>
        public LocalizationManager Initialize()
        {
            if (stopped)
            {
                throw new ObjectDisposedException(nameof(LocalizationBootstrap));
            }
            if (owned != null)
            {
                if (!owned.IsInitialized)
                {
                    throw new InvalidOperationException("This bootstrap's service has already been released.");
                }
                return owned;
            }
            owned = LocalizationSetup.Initialize(settings);
            return owned;
        }

        /// <summary>关闭本地化启动入口。</summary>
        public void Shutdown()
        {
            if (stopped)
            {
                return;
            }
            stopped = true;
            var service = owned;
            owned = null;
            service?.Dispose();
        }

        /// <summary>Unity 创建组件时执行本组件的初始化逻辑。</summary>
        private void Awake()
        {
            if (initializeOnAwake)
            {
                Initialize();
            }
        }

        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        private void OnDestroy()
        {
            if (releaseOnDestroy)
            {
                Shutdown();
            }
            else
            {
                owned = null;
                stopped = true;
            }
        }
    }
}
