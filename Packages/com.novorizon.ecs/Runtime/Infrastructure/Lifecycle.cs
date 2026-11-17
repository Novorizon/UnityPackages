namespace ECS
{
    /// <summary>定义IInitializeable的调用契约。</summary>
    public interface IInitializeable
    {
        /// <summary>响应已初始化。</summary>
        void OnInitialized();
    }

    /// <summary>定义IRecycleable的调用契约。</summary>
    public interface IRecycleable
    {
        /// <summary>响应 Recycle。</summary>
        void OnRecycle();
    }
}
