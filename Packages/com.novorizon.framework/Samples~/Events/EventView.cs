using Game.Framework.Event;
using System;
using UnityEngine;

namespace Game.Examples
{

    public enum MessageTopic
    {
        None,
        IapFinish
    }
    public sealed class EventView : MonoBehaviour
    {
        readonly Subscriber subscriber = new Subscriber();
        ////或者
        //ISubscription sub1 ;
        //ISubscription sub2;


        void OnEnable()
        {
            ISubscription sub1 = EventManager.Instance.Subscribe(MessageTopic.IapFinish, OnToast);
            ISubscription sub2 = EventManager.Instance.Subscribe(MessageTopic.IapFinish, (Action<Notification>)OnNotify);
            subscriber.Add(sub1);
            subscriber.Add(sub2);
        }

        void OnDisable()
        {
            subscriber.Clear();
            //或者
            //sub1.Dispose();
            //sub2.Dispose();
        }

        void OnToast()
        {
        }
        void OnNotify(Notification notification)
        {
            string a = notification.Message as string;
            Debug.Log(a);
        }
    }
}
