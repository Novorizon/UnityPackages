namespace ECS
{
    public interface IInitializeable
    {
        void OnInitialized();
    }

    public interface IRecycleable
    {
        void OnRecycle();
    }
}
