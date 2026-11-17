
using Unity.Mathematics;

namespace ECS
{
    /// <summary>提供旋转的实现。</summary>
    public class Rotation : IComponent, IInitializeable
    {
        public quaternion Value;

        /// <summary>响应已初始化。</summary>
        public void OnInitialized()
        {
            Value = quaternion.identity;
        }
    }
}
