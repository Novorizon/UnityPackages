using ECS;

namespace ECS.Samples.QuickStart
{
    public sealed class DemoPosition : IComponent, IInitializeable
    {
        public float X;
        public void OnInitialized()
        {
            X = 0;
        }
    }

    public sealed class DemoVelocity : IComponent, IInitializeable
    {
        public float Speed;
        public void OnInitialized()
        {
            Speed = 0;
        }
    }

    // Keep each imported demo instance from moving another instance's entities.
    public sealed class DemoOwner : IComponent, IInitializeable
    {
        public long Id;
        public void OnInitialized()
        {
            Id = 0;
        }
    }
}
