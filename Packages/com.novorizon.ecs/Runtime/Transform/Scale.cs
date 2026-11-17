
using Unity.Mathematics;

namespace ECS
{
    /// <summary>提供缩放的实现。</summary>
    public class Scale : IComponent, IInitializeable
    {
        public float3 Value;

        /// <summary>响应已初始化。</summary>
        public void OnInitialized()
        {
            Value = 1;
        }
    }
}
