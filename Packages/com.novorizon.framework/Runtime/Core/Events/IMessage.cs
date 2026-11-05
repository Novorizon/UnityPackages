///------------------------------------
/// Author：Novorizon
/// Mail：novorizon@hotmail.com
/// Date：2023-04-11
/// Description：事件系统的消息类型只有实现 IMessage 才能被订阅/发送，避免误把普通业务类型当消息发出去
///------------------------------------
namespace Game.Framework.Event
{
    public interface IMessage { }

    public class Notification : IMessage
    {
        public object Message {  get; set; }
    }
}
