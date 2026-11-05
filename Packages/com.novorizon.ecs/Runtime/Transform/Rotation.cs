
using Unity.Mathematics;

namespace ECS
{
    public class Rotation : IComponent, IInitializeable
    {
        public quaternion Value;

        public void OnInitialized()
        {
            Value = quaternion.identity;
        }
    }
}
