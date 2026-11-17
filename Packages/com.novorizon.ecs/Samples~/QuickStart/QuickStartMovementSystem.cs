namespace ECS.Samples.QuickStart
{
    /// <summary>提供快速入门 Movement 系统的实现。</summary>
    public sealed class QuickStartMovementSystem : ComponentSystem<DemoOwner, DemoPosition, DemoVelocity>
    {
        internal long OwnerId;
        /// <summary>响应更新。</summary>
        protected override void OnUpdate(int index, Entity entity, DemoOwner owner, DemoPosition position, DemoVelocity velocity)
        {
            if (owner.Id == OwnerId)
            {
                position.X += velocity.Speed * World.TimePerFrame;
            }
        }

        /// <summary>响应 Recycle。</summary>
        public override void OnRecycle()
        {
            OwnerId = 0;
            base.OnRecycle();
        }
    }
}
