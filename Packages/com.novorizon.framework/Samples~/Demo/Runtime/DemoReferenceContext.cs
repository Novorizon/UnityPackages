#nullable enable
using Game.Framework.Pooling;
using UnityEngine;

namespace Game.Framework.Samples.Demo
{
    /// <summary>示例引用对应的上下文。携带一次操作所需的上下文。</summary>
    public sealed class DemoReferenceContext : IReferencePoolable
    {
        public GameObject? Target;
        public int Amount;
        /// <summary>恢复该类型约定的初始状态。</summary>
        public void Reset()
        {
            Target = null;
            Amount = 0;
        }
    }
}
