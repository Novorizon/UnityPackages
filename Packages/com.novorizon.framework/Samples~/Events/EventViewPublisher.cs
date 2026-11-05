using Game.Framework;
using Game.Framework.Event;
using System.Threading.Tasks;

namespace Game.Examples
{
    public static class EventViewPublisher
    {
        public static async Task SimulateAsync(long playerId)
        {
            await Task.Delay(100);

            //Notification notification = new Notification();
            EventManager.Instance.Notify(MessageTopic.IapFinish);
        }
    }
}
