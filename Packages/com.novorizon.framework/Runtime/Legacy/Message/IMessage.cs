namespace Game.Framework
{
    public interface IMessage : Event.IMessage { }
    public class Notify : IMessage { public object Message { get; set; } }
}
