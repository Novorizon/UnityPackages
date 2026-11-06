namespace ECS.Samples.QuickStart
{
    public sealed class QuickStartMovementSystem : ComponentSystem<DemoOwner, DemoPosition, DemoVelocity>
    {
        internal long OwnerId;
        protected override void OnUpdate(int index, Entity entity, DemoOwner owner, DemoPosition position, DemoVelocity velocity)
        {
            if (owner.Id == OwnerId)
            {
                position.X += velocity.Speed * World.TimePerFrame;
            }
        }

        public override void OnRecycle()
        {
            OwnerId = 0;
            base.OnRecycle();
        }
    }
}
