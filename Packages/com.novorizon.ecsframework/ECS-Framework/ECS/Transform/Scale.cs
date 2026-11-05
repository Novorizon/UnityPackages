
using Unity.Mathematics;

namespace ECS
{
    public class Scale : IComponent, IInitializeable
    {
        public float3 Value;

        public void OnInitialized()
        {
            Value = 1;
        }
    }
}
