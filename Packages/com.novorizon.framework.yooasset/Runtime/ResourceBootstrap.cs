using UnityEngine;
namespace Game.Resource
{
    internal static class ResourceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { ResourceManager.Instance.ResetForPlay(); }
    }
}
