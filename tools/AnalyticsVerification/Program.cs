using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Analytics;
using Game.Framework.Attribution;

internal static class Program
{
    private static int passed;

    private static void Main()
    {
        Run("construction and denied startup never initialize SDKs", () =>
        {
            var backend = new Backend("denied");
            using var manager = Manager(backend);
            Equal(0, backend.Starts);
            Status(TelemetryStatus.Disabled, manager.Start(false));
            Status(TelemetryStatus.Disabled, manager.LogEvent("level_start"));
            Equal(0, backend.Starts);
            Equal(0, backend.Events.Count);
        });
        Run("initial account is applied on explicit consent startup", () =>
        {
            var backend = new Backend("identity");
            using var manager = Manager(backend);
            manager.SetUserId("game-user");
            manager.Start(false);
            manager.SetCollectionEnabled(true);
            Equal(1, backend.Starts);
            Equal("game-user", backend.Users[0]);
        });
        Run("no SDK starts before Start even if collection is configured", () =>
        {
            var backend = new Backend("prestart");
            using var manager = Manager(backend);
            manager.SetCollectionEnabled(true);
            Status(TelemetryStatus.NotStarted, manager.LogEvent("test_event"));
            Equal(0, backend.Starts);
            manager.Start(true);
            Equal(1, backend.Starts);
        });
        Run("consent revocation drops events without replay after resume", () =>
        {
            var backend = new Backend("consent");
            using var manager = Manager(backend);
            manager.Start(true);
            manager.LogEvent("before");
            manager.SetCollectionEnabled(false);
            Status(TelemetryStatus.Disabled, manager.LogEvent("discarded"));
            manager.SetCollectionEnabled(true);
            manager.LogEvent("after");
            Equal("before,after", string.Join(",", backend.Events));
        });
        Run("an already-started external SDK still needs this manager's explicit Start", () =>
        {
            using var backend = new Backend("external_started");
            backend.Initialize(null);
            using var manager = new AnalyticsManager(new[] { backend });
            manager.SetCollectionEnabled(true);
            Status(TelemetryStatus.NotStarted, manager.LogEvent("before_start"));
            manager.Start(true);
            Status(TelemetryStatus.AcceptedBySdk, manager.LogEvent("after_start"));
            Equal(1, backend.Events.Count);
        });
        Run("account changes while disabled stage identity for resume", () =>
        {
            var backend = new Backend("switch");
            using var manager = Manager(backend);
            manager.SetUserId("old");
            manager.Start(true);
            manager.SetCollectionEnabled(false);
            manager.SetUserId("new");
            manager.SetCollectionEnabled(true);
            Equal("new", backend.Users.Last());
            manager.SetUserId(null);
            Check(backend.Users.Last() == null);
        });
        Run("one failed provider cannot suppress another provider", () =>
        {
            var broken = new Backend("broken") { FailEvent = true };
            var healthy = new Backend("healthy");
            using var manager = new AnalyticsManager(new[] { broken, healthy }, true);
            manager.Start(true);
            var report = manager.LogEvent("fanout");
            Check(report.HasFailures);
            Equal(TelemetryStatus.Failed, report.Deliveries[0].Status);
            Equal(TelemetryStatus.AcceptedBySdk, report.Deliveries[1].Status);
            Equal(1, healthy.Events.Count);
            Equal("InvalidOperationException", report.Deliveries[0].ErrorType);
            Check(!report.Deliveries[0].ErrorType.Contains("sensitive"));
        });
        Run("initialization failure is isolated and not retried by every event", () =>
        {
            var broken = new Backend("failed_init") { FailStart = true };
            var healthy = new Backend("ready_init");
            using var manager = new AnalyticsManager(new[] { broken, healthy }, true);
            Check(manager.Start(true).HasFailures);
            var report = manager.LogEvent("still_works");
            Equal(TelemetryStatus.NotStarted, report.Deliveries[0].Status);
            Equal(1, healthy.Events.Count);
            Equal(1, broken.Starts);
        });
        Run("one live SDK owner cannot be replaced accidentally", () =>
        {
            using var first = new Backend("same_sdk");
            using var second = new Backend("same_sdk");
            first.Initialize(null);
            Throws<InvalidOperationException>(() => second.Initialize(null));
            first.Dispose();
            second.Initialize(null);
            Equal(1, second.Starts);
        });
        Run("duplicate provider IDs are rejected before dispatch", () =>
        {
            Throws<ArgumentException>(() => new AnalyticsManager(new[] { new Backend("same"), new Backend("same") }));
        });
        Run("super properties are snapshots and event properties take precedence", () =>
        {
            var backend = new Backend("properties");
            using var manager = Manager(backend);
            manager.Start(true);
            var defaults = new Dictionary<string, object> { ["build"] = "one", ["mode"] = "menu" };
            manager.SetSuperProperties(defaults);
            defaults["build"] = "changed";
            manager.LogEvent("level_start", new Dictionary<string, object> { ["mode"] = "battle" });
            Equal("one", backend.Payload["build"]);
            Equal("battle", backend.Payload["mode"]);
        });
        Run("a provider cannot mutate the payload delivered to other providers", () =>
        {
            var first = new Backend("mutating") { Mutate = true };
            var second = new Backend("intact");
            using var manager = new AnalyticsManager(new[] { first, second }, true);
            manager.Start(true);
            Check(manager.LogEvent("immutable", new Dictionary<string, object> { ["score"] = 12 }).HasFailures);
            Equal(12L, second.Payload["score"]);
        });
        Run("invalid payload is rejected before any provider receives it", () =>
        {
            var backend = new Backend("invalid");
            using var manager = Manager(backend);
            manager.Start(true);
            Throws<ArgumentException>(() => manager.LogEvent("invalid_name", new Dictionary<string, object> { ["value"] = double.NaN }));
            Throws<ArgumentException>(() => manager.LogEvent("invalid name"));
            Equal(0, backend.Events.Count);
        });
        Run("payload types cannot hide arbitrary objects or nested credentials", () =>
        {
            Throws<ArgumentException>(() => AnalyticsValues.Copy(new Dictionary<string, object> { ["nested"] = new Dictionary<string, object>() }));
            Throws<ArgumentException>(() => AnalyticsValues.Copy(new Dictionary<string, object> { ["object"] = new object() }));
            Throws<ArgumentException>(() => AnalyticsValues.Copy(new Dictionary<string, object> { ["value"] = null }));
        });
        Run("schema and property count limits are bounded", () =>
        {
            Throws<ArgumentException>(() => AnalyticsValues.Name("_reserved"));
            Throws<ArgumentException>(() => AnalyticsValues.Name(new string('a', 65)));
            Throws<ArgumentException>(() => AnalyticsValues.UserId(" "));
            var values = Enumerable.Range(0, 65).ToDictionary(n => "p" + n, n => (object)n);
            Throws<ArgumentException>(() => AnalyticsValues.Copy(values));
        });
        Run("profile append values are deeply copied", () =>
        {
            var values = new List<string> { "first" };
            var change = new UserPropertyChange(UserPropertyOperation.Append, new Dictionary<string, object> { ["tags"] = values });
            values.Add("second");
            Equal(1, ((IReadOnlyList<string>)change.Properties["tags"]).Count);
            Throws<NotSupportedException>(() => ((IList<string>)change.Properties["tags"]).Add("mutation"));
        });
        Run("profile arithmetic accepts only finite numeric deltas", () =>
        {
            Throws<ArgumentException>(() => new UserPropertyChange(UserPropertyOperation.Add, new Dictionary<string, object> { ["xp"] = "ten" }));
            Throws<ArgumentException>(() => new UserPropertyChange(UserPropertyOperation.Add, new Dictionary<string, object> { ["xp"] = double.PositiveInfinity }));
            Throws<ArgumentException>(() => new UserPropertyChange(UserPropertyOperation.Append, new Dictionary<string, object> { ["tags"] = "one" }));
        });
        Run("profile deletion cannot accidentally send unrelated attributes", () =>
        {
            Throws<ArgumentException>(() => new UserPropertyChange(UserPropertyOperation.DeleteProfile, new Dictionary<string, object> { ["name"] = "value" }));
        });
        Run("unsupported profile operations are reported per destination", () =>
        {
            var first = new Backend("profiles") { Profiles = true };
            var second = new Backend("no_profiles");
            using var manager = new AnalyticsManager(new[] { first, second }, true);
            manager.Start(true);
            var report = manager.ApplyUserProperties(new UserPropertyChange(UserPropertyOperation.Set, new Dictionary<string, object> { ["level"] = 5 }));
            Equal(TelemetryStatus.AcceptedBySdk, report.Deliveries[0].Status);
            Equal(TelemetryStatus.Unsupported, report.Deliveries[1].Status);
            Equal(1, first.ProfileCalls);
            Equal(0, second.ProfileCalls);
        });
        Run("disabled profiles and flush calls do not touch native SDKs", () =>
        {
            var backend = new Backend("disabled_profile") { Profiles = true };
            using var manager = Manager(backend);
            manager.Start(false);
            Status(TelemetryStatus.Disabled, manager.ApplyUserProperties(new UserPropertyChange(UserPropertyOperation.Set)));
            Status(TelemetryStatus.Disabled, manager.Flush());
            Equal(0, backend.ProfileCalls);
            Equal(0, backend.Flushes);
        });
        Run("supported explicit flush is not presented as network delivery", () =>
        {
            var backend = new Backend("flush") { Flushing = true };
            using var manager = Manager(backend);
            manager.Start(true);
            Status(TelemetryStatus.AcceptedBySdk, manager.Flush());
            Equal(1, backend.Flushes);
        });
        Run("number formatting is independent of device locale", () =>
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Equal("12.5", AnalyticsValues.Invariant(12.5d));
                Equal("9007199254740993", AnalyticsValues.Invariant(9007199254740993L));
                Equal("true", AnalyticsValues.Invariant(true));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        });
        Run("revenue requires a stable transaction and currency", () =>
        {
            Throws<ArgumentException>(() => new RevenueEvent("purchase", -1, "USD", "tx"));
            Throws<ArgumentException>(() => new RevenueEvent("purchase", 1, "usd", "tx"));
            Throws<ArgumentException>(() => new RevenueEvent("purchase", 1, "USD", ""));
        });
        Run("revenue only reaches explicitly capable destinations", () =>
        {
            var backend = new Mmp("revenue_mmp");
            using var manager = new AttributionManager(backend, true);
            manager.Start(false);
            Status(TelemetryStatus.Disabled, manager.LogRevenue(new RevenueEvent("purchase", 4.99m, "USD", "tx")));
            Equal(0, backend.Revenues);
            manager.SetCollectionEnabled(true);
            Status(TelemetryStatus.AcceptedBySdk, manager.LogRevenue(new RevenueEvent("purchase", 4.99m, "USD", "tx")));
            Equal(1, backend.Revenues);
        });
        Run("attribution callbacks are deferred until SDK initialization ends", () =>
        {
            var backend = new Mmp("deferred_mmp") { PublishDuringStart = true };
            using var manager = new AttributionManager(backend, true);
            int calls = 0;
            manager.AttributionChanged += _ => calls++;
            manager.Start(true);
            Equal(0, calls);
            UnityThread.Update();
            Equal(1, calls);
        });
        Run("queued attribution is suppressed after consent revocation", () =>
        {
            var backend = new Mmp("paused_mmp");
            using var manager = new AttributionManager(backend, true);
            int calls = 0;
            manager.AttributionChanged += _ => calls++;
            manager.Start(true);
            backend.Emit();
            manager.SetCollectionEnabled(false);
            UnityThread.Update();
            Equal(0, calls);
        });
        Run("queued attribution cannot notify a disposed manager", () =>
        {
            var backend = new Mmp("disposed_mmp");
            var manager = new AttributionManager(backend, true);
            int calls = 0;
            manager.AttributionChanged += _ => calls++;
            manager.Start(true);
            backend.Emit();
            manager.Dispose();
            UnityThread.Update();
            Equal(0, calls);
        });
        Run("consent revoke and immediate resume cannot replay an old queued attribution", () =>
        {
            var backend = new Mmp("resumed_mmp");
            using var manager = new AttributionManager(backend, true);
            int calls = 0;
            manager.AttributionChanged += _ => calls++;
            manager.Start(true);
            backend.Emit();
            manager.SetCollectionEnabled(false);
            manager.SetCollectionEnabled(true);
            UnityThread.Update();
            Equal(0, calls);
            backend.Emit();
            UnityThread.Update();
            Equal(1, calls);
        });
        Run("provider reentrancy cannot change the account halfway through fanout", () =>
        {
            var backend = new Backend("reentrancy");
            using var manager = Manager(backend);
            manager.Start(true);
            backend.DuringEvent = () => manager.SetUserId("unexpected");
            Check(manager.LogEvent("reentered").HasFailures);
        });
        Run("worker-thread calls are rejected before SDK interaction", () =>
        {
            var backend = new Backend("threads");
            using var manager = Manager(backend);
            manager.Start(true);
            Throws<InvalidOperationException>(() => Task.Run(() => manager.LogEvent("worker")).GetAwaiter().GetResult());
            Equal(0, backend.Events.Count);
        });
        Run("cleanup failure cannot prevent other owned backends from disposing", () =>
        {
            var first = new Backend("bad_cleanup") { FailDispose = true };
            var second = new Backend("good_cleanup");
            var manager = new AnalyticsManager(new[] { first, second }, true);
            manager.Start(true);
            Throws<AggregateException>(manager.Dispose);
            Equal(1, second.Disposals);
            using var replacement = new Backend("bad_cleanup");
            replacement.Initialize(null);
        });
        Run("caller-owned SDKs remain caller-owned when manager is disposed", () =>
        {
            using var backend = new Backend("external_owner");
            var manager = new AnalyticsManager(new[] { backend });
            manager.Start(true);
            manager.Dispose();
            Equal(0, backend.Disposals);
            backend.LogEvent("still_owned", null);
            Equal(1, backend.Events.Count);
        });
        Console.WriteLine($"PASS analytics/attribution: {passed} behavioral cases; test backends only, no live telemetry.");
    }

    private static AnalyticsManager Manager(Backend backend) => new AnalyticsManager(new[] { backend }, true);
    private static void Run(string name, Action action)
    {
        UnityThread.Reset();
        UnityThread.InitializeManual();
        try
        {
            action();
            passed++;
        }
        catch (Exception error)
        {
            throw new Exception(name, error);
        }
    }
    private static void Check(bool value)
    {
        if (!value)
        {
            throw new Exception("Assertion failed.");
        }
    }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual));
    private static void Status(TelemetryStatus status, TelemetryReport report) => Equal(status, report.Deliveries.Single().Status);
    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class Backend : AnalyticsBackend
    {
        public override string Id { get; }
        internal int Starts, ProfileCalls, Flushes, Disposals;
        internal bool FailStart, FailEvent, Mutate, Profiles, Flushing, FailDispose;
        internal readonly List<string> Users = new List<string>();
        internal readonly List<string> Events = new List<string>();
        internal IReadOnlyDictionary<string, object> Payload;
        internal Action DuringEvent;
        public override bool CanFlush => Flushing;
        internal Backend(string id) => Id = id;
        protected override void OnInitialize(string id)
        {
            Starts++;
            if (FailStart)
            {
                throw new InvalidOperationException("sensitive developer key must never appear in a report");
            }
            Users.Add(id);
        }
        protected override void OnSetCollectionEnabled(bool enabled)
        {
        }
        protected override void OnSetUserId(string id) => Users.Add(id);
        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
            DuringEvent?.Invoke();
            if (FailEvent)
            {
                throw new InvalidOperationException("sensitive event values");
            }
            if (Mutate)
            {
                ((IDictionary<string, object>)properties)["score"] = 999;
            }
            Events.Add(name);
            Payload = properties;
        }
        public override bool Supports(UserPropertyOperation operation) => Profiles;
        protected override void OnApplyUserProperties(UserPropertyChange change) => ProfileCalls++;
        protected override void OnFlush() => Flushes++;
        protected override void OnDispose()
        {
            Disposals++;
            if (FailDispose)
            {
                throw new InvalidOperationException("cleanup");
            }
        }
    }

    private sealed class Mmp : AttributionBackend
    {
        public override string Id { get; }
        internal bool PublishDuringStart;
        internal int Revenues;
        internal Mmp(string id) => Id = id;
        protected override void OnInitialize(string id)
        {
            if (PublishDuringStart)
            {
                Emit();
            }
        }
        internal void Emit() => Publish(new AttributionSnapshot(Id, "network", "campaign", null, null, false));
        protected override void OnSetCollectionEnabled(bool enabled)
        {
        }
        protected override void OnSetUserId(string id)
        {
        }
        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
        }
        protected override void OnLogRevenue(RevenueEvent revenue) => Revenues++;
    }
}
