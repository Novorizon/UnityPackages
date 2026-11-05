using Game.Framework.Timing;
using UnityEngine;

namespace Game.Framework
{
    internal sealed class FrameworkUpdateDriver : MonoBehaviour
    {
        private static FrameworkUpdateDriver current;
        internal static void Install()
        {
            TimerManager.Instance.UseClocks(() => Time.timeAsDouble, () => Time.realtimeSinceStartupAsDouble);
            current = FindObjectOfType<FrameworkUpdateDriver>(true);
            if (current == null) current = new GameObject("Novorizon Framework").AddComponent<FrameworkUpdateDriver>();
            current.enabled = true; DontDestroyOnLoad(current.gameObject);
        }
        internal static void Stop() { if (current != null) current.enabled = false; current = null; }
        private void Update() { TimerManager.Instance.Update(); }
    }
}
