using UnityEngine;
namespace Game.Framework.Pooling
{
    internal sealed class GameObjectPoolMember : MonoBehaviour
    {
        internal GameObjectPool Owner;
        private void OnDestroy() { var owner = Owner; Owner = null; owner?.NotifyDestroyed(gameObject); }
    }
}
