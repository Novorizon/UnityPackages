
using Unity.Mathematics;

namespace ECS
{
    public class Position : IComponent, IInitializeable
    {
        public float3 Value;

        public void OnInitialized()
        {
            Value = 0;
        }
    }
}
