using Game.Framework.Timing;
using UnityEngine;

namespace Game.Framework
{
    internal sealed class FrameworkUpdateDriver : MonoBehaviour
    {
        private static FrameworkUpdateDriver current;
        private static readonly System.Func<double> scaledClock = () => Time.timeAsDouble;
        private static readonly System.Func<double> unscaledClock = () => Time.realtimeSinceStartupAsDouble;

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

        internal static void Stop()
        {
            if (current != null)
            {
                current.enabled = false;
            }

            current = null;
        }

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
