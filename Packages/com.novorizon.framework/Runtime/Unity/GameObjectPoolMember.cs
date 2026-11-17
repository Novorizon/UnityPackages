using UnityEngine;
namespace Game.Framework.Pooling
{
    /// <summary>提供GameObject 对象池成员的实现。</summary>
    internal sealed class GameObjectPoolMember : MonoBehaviour
    {
        internal GameObjectPool Owner;
        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        private void OnDestroy() { var owner = Owner; Owner = null; owner?.NotifyDestroyed(gameObject); }
    }
}
