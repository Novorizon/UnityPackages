using UI;
using Xunit;

namespace Game.Framework.UI.Tests;
public sealed class UIMessageBusTests
{
    [Fact]
    public void SubscriptionChangesUseANewSnapshotAndPreserveCurrentPublishOrder()
    {
        var bus = new UIMessageBus();
        var calls = new List<string>();
        Action<int> second = value => calls.Add($"second:{value}");
        Action<int> third = value => calls.Add($"third:{value}");
        bus.Subscribe<int>(value =>
        {
            calls.Add($"first:{value}");
            bus.Unsubscribe(second);
            bus.Subscribe(third);
        });
        bus.Subscribe(second);
        bus.Subscribe(second); // Duplicate subscription remains idempotent.
        bus.Publish(1);
        bus.Publish(2);
        Assert.Equal(new[] { "first:1", "second:1", "first:2", "third:2" }, calls);
    }

    [Fact]
    public void NestedPublishSeesChangesWhileOuterPublishKeepsItsSnapshot()
    {
        var bus = new UIMessageBus();
        var calls = new List<string>();
        Action<int> second = value => calls.Add($"second:{value}");
        bus.Subscribe<int>(value =>
        {
            calls.Add($"first:{value}");
            if (value != 1)
            {
                return;
            }

            bus.Unsubscribe(second);
            bus.Publish(2);
        });
        bus.Subscribe(second);
        bus.Publish(1);
        Assert.Equal(new[] { "first:1", "first:2", "second:1" }, calls);
    }

    [Fact]
    public void ClearAndResubscribeInsideCallbackDoNotChangeCurrentSnapshot()
    {
        var bus = new UIMessageBus();
        var calls = new List<int>();
        bus.Subscribe<int>(_ =>
        {
            calls.Add(1);
            bus.Clear();
            bus.Subscribe<int>(_ => calls.Add(3));
        });
        bus.Subscribe<int>(_ => calls.Add(2));
        bus.Publish(0);
        bus.Publish(0);
        Assert.Equal(new[] { 1, 2, 3 }, calls);
    }

    [Fact]
    public void RemovingLastSubscriberAllowsCleanRegistration()
    {
        var bus = new UIMessageBus();
        int total = 0;
        Action<int> handler = value => total += value;
        bus.Subscribe(handler);
        bus.Unsubscribe(handler);
        bus.Publish(100);
        bus.Subscribe(handler);
        bus.Publish(2);
        Assert.Equal(2, total);
    }

    [Fact]
    public void SteadyStatePublishDoesNotAllocateWithEightSubscribers()
    {
        var bus = new UIMessageBus();
        var receivers = new Receiver[8];
        for (int i = 0; i < receivers.Length; i++)
        {
            receivers[i] = new Receiver();
            bus.Subscribe<int>(receivers[i].Receive);
        }

        for (int i = 0; i < 10_000; i++)
        {
            bus.Publish(i);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            bus.Publish(i);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.All(receivers, receiver => Assert.Equal(9_999, receiver.Last));
    }

    [Fact]
    public void ScopedSubscriptionsOwnTheirRegistrationsWithoutRemovingAnExistingHandler()
    {
        var bus = new UIMessageBus();
        int calls = 0;
        Action<int> handler = _ => calls++;
        bus.Subscribe(handler);
        using var first = bus.SubscribeScoped(handler);
        using var second = bus.SubscribeScoped(handler);
        bus.Publish(1);
        Assert.Equal(3, calls);
        first.Dispose();
        first.Dispose();
        bus.Publish(1);
        Assert.Equal(5, calls);
        second.Dispose();
        bus.Publish(1);
        Assert.Equal(6, calls);
    }

    [Fact]
    public void DisposingAScopedSubscriberSkipsItsCallbackInTheCurrentSnapshot()
    {
        var bus = new UIMessageBus();
        IDisposable? subscription = null;
        int calls = 0;
        bus.Subscribe<int>(_ => subscription!.Dispose());
        subscription = bus.SubscribeScoped<int>(_ => calls++);
        bus.Publish(0);
        Assert.Equal(0, calls);
    }

    sealed class Receiver
    {
        public int Last;
        public void Receive(int value) => Last = value;
    }
}