
using Unity.Mathematics;

namespace ECS
{
    /// <summary>提供位置的实现。</summary>
    public class Position : IComponent, IInitializeable
    {
        public float3 Value;

        /// <summary>响应已初始化。</summary>
        public void OnInitialized()
        {
            Value = 0;
        }
    }
}
