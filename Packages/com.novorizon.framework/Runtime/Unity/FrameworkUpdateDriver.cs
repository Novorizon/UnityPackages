using Game.Framework.Timing;
using UnityEngine;

namespace Game.Framework
{
    /// <summary>提供框架更新驱动的实现。</summary>
    internal sealed class FrameworkUpdateDriver : MonoBehaviour
    {
        private static FrameworkUpdateDriver current;
        private static readonly System.Func<double> scaledClock = () => Time.timeAsDouble;
        private static readonly System.Func<double> unscaledClock = () => Time.realtimeSinceStartupAsDouble;

        /// <summary>安装框架更新驱动。</summary>
        internal static void Install()
        {
            if (TimerManager.TryGetInstance(out var timers))
            {
                timers.EnsureClocks(scaledClock, unscaledClock);
            }

            current = FindObjectOfType<FrameworkUpdateDriver>(true);
            if (current == null)
            {
                current = new GameObject("Novorizon Framework").AddComponent<FrameworkUpdateDriver>();
            }

            current.enabled = true;
            DontDestroyOnLoad(current.gameObject);
        }

        /// <summary>停止框架更新驱动。</summary>
        internal static void Stop()
        {
            if (current != null)
            {
                current.enabled = false;
            }

            current = null;
        }

        /// <summary>推进该对象的一次更新。</summary>
        private void Update()
        {
            if (TimerManager.TryGetInstance(out var timers))
            {
                timers.EnsureClocks(scaledClock, unscaledClock);
                timers.Update();
            }
        }
    }
}
