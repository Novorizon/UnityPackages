using ECS;

namespace ECS.Samples.QuickStart
{
    /// <summary>提供示例位置的实现。</summary>
    public sealed class DemoPosition : IComponent, IInitializeable
    {
        public float X;
        /// <summary>响应已初始化。</summary>
        public void OnInitialized()
        {
            X = 0;
        }
    }

    /// <summary>提供示例 Velocity的实现。</summary>
    public sealed class DemoVelocity : IComponent, IInitializeable
    {
        public float Speed;
        /// <summary>响应已初始化。</summary>
        public void OnInitialized()
        {
            Speed = 0;
        }
    }

    // Keep each imported demo instance from moving another instance's entities.
    /// <summary>提供示例持有方的实现。</summary>
    public sealed class DemoOwner : IComponent, IInitializeable
    {
        public long Id;
        /// <summary>响应已初始化。</summary>
        public void OnInitialized()
        {
            Id = 0;
        }
    }
}
