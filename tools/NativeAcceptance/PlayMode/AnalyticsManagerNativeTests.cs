using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Analytics;
using Game.Framework.Analytics.Unity;
using Game.Framework.Attribution;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class AnalyticsManagerNativeTests
{
    [UnityTest]
    public IEnumerator InspectorTriggerUsesInjectedManagerAndRespectsCollectionGate()
    {
        var backend = new MemoryBackend();
        using (var manager = new AnalyticsManager(new[] { backend }, true))
        {
            var root = new GameObject("Analytics inspector acceptance");
            var trigger = root.AddComponent<AnalyticsEventTrigger>();
            trigger.Configure(manager);
            trigger.eventName = "shop_open";
            trigger.parameters = new[]
            {
                new AnalyticsParameter { key = "source", type = AnalyticsParameterType.Text, text = "home" },
                new AnalyticsParameter { key = "level", type = AnalyticsParameterType.Integer, integer = 7 }
            };
            manager.Start(false);
            root.SendMessage("Track");
            Assert.AreEqual(0, backend.Events);
            manager.SetCollectionEnabled(true);
            root.SendMessage("Track");
            Assert.AreEqual(1, backend.Events);
            Assert.AreEqual("shop_open", backend.Name);
            Assert.AreEqual(7L, backend.Properties["level"]);
            Assert.AreEqual("home", backend.Properties["source"]);
            Assert.AreEqual(TelemetryStatus.AcceptedBySdk, trigger.LastReport.Deliveries[0].Status);
            Object.Destroy(root);
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator WorkerAttributionCallbacksReturnToTheUnityThread()
    {
        var backend = new TestAttribution();
        using (var manager = new AttributionManager(backend, true))
        {
            bool received = false;
            manager.AttributionChanged += value =>
            {
                Assert.IsTrue(UnityThread.IsMainThread);
                Assert.AreEqual("campaign", value.Campaign);
                received = true;
            };
            manager.Start(true);
            var task = Task.Run(backend.Emit);
            yield return new WaitUntil(() => task.IsCompleted && received);
            Assert.IsFalse(task.IsFaulted);
        }
    }

    private sealed class MemoryBackend : AnalyticsBackend
    {
        public override string Id => "native-memory";
        internal int Events;
        internal string Name;
        internal IReadOnlyDictionary<string, object> Properties;
        protected override void OnInitialize(string initialUserId)
        {
        }
        protected override void OnSetCollectionEnabled(bool enabled)
        {
        }
        protected override void OnSetUserId(string userId)
        {
        }
        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
            Events++;
            Name = name;
            Properties = properties;
        }
    }

    private sealed class TestAttribution : AttributionBackend
    {
        public override string Id => "native-mmp";
        internal void Emit() => Publish(new AttributionSnapshot(Id, "network", "campaign", null, null, false));
        protected override void OnInitialize(string initialUserId)
        {
        }
        protected override void OnSetCollectionEnabled(bool enabled)
        {
        }
        protected override void OnSetUserId(string userId)
        {
        }
        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
        }
        protected override void OnLogRevenue(RevenueEvent revenue)
        {
        }
    }
}
