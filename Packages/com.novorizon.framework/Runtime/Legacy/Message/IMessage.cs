namespace Game.Framework
{
    /// <summary>定义消息的调用契约。</summary>
    public interface IMessage : Event.IMessage { }
    /// <summary>提供Notify的实现。</summary>
    public class Notify : IMessage { public object Message { get; set; } }
}
