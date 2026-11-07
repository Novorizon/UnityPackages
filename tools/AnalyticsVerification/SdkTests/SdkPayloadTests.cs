using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Game.Framework;
using Game.Framework.Analytics;
using Game.Framework.Analytics.ThinkingData;
using Game.Framework.Attribution;
using Game.Framework.Attribution.Adjust;
using Game.Framework.Attribution.AppsFlyer;
using NUnit.Framework;
using ThinkingData.Analytics.Utils;
using GaBackend = Game.Framework.Analytics.GameAnalytics.GameAnalyticsBackend;

public sealed class SdkPayloadTests
{
    [Test]
    public void AdjustUsesRealRevenueAndDeduplicationFields()
    {
        var backend = new AdjustAttributionBackend("fixture_app_token", new Dictionary<string, string> { ["purchase"] = "abc123" });
        var revenue = new RevenueEvent("purchase", 4.99m, "USD", "transaction", new Dictionary<string, object> { ["level"] = 9 });
        var method = typeof(AdjustAttributionBackend).GetMethod("BuildRevenueEvent", BindingFlags.Instance | BindingFlags.NonPublic);
        var value = (AdjustSdk.AdjustEvent)method.Invoke(backend, new object[] { revenue });
        Assert.AreEqual("abc123", value.EventToken);
        Assert.AreEqual(4.99d, value.Revenue);
        Assert.AreEqual("USD", value.Currency);
        Assert.AreEqual("transaction", value.DeduplicationId);
        Assert.AreEqual("level", value.CallbackParameters[0]);
        Assert.AreEqual("9", value.CallbackParameters[1]);
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(backend, new object[] { new RevenueEvent("unmapped", 1, "USD", "tx") }));
        Assert.IsInstanceOf<TelemetryUnsupportedException>(error.InnerException);
    }

    [Test]
    public void AppsFlyerRevenueUsesInvariantAuthoritativeTypedValues()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var revenue = new RevenueEvent("purchase", 4.99m, "USD", "transaction", new Dictionary<string, object>
            {
                ["af_revenue"] = "wrong",
                ["af_currency"] = "wrong",
                ["big_number"] = 9007199254740993L
            });
            var method = typeof(AppsFlyerAttributionBackend).GetMethod("RevenueValues", BindingFlags.Static | BindingFlags.NonPublic);
            var values = (Dictionary<string, string>)method.Invoke(null, new object[] { revenue });
            Assert.AreEqual("4.99", values["af_revenue"]);
            Assert.AreEqual("USD", values["af_currency"]);
            Assert.AreEqual("transaction", values["af_order_id"]);
            Assert.AreEqual("9007199254740993", values["big_number"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void ThinkingDataRejectsValuesTheRealSdkWouldDiscard()
    {
        TDLog.EnableLog(false);
        var method = typeof(ThinkingDataAnalyticsBackend).GetMethod("ValidateNames", BindingFlags.Static | BindingFlags.NonPublic);
        method.Invoke(null, new object[] { "level_complete", new Dictionary<string, object> { ["score"] = 10L } });
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { new string('a', 51), new Dictionary<string, object>() }));
        Assert.IsInstanceOf<ArgumentException>(error.InnerException);
        error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { "event_name", new Dictionary<string, object> { ["text"] = new string('文', 900) } }));
        Assert.IsInstanceOf<ArgumentException>(error.InnerException);
    }

    [Test]
    public void RealSdkAdaptersRemainPassiveBeforeCollectionIsEnabled()
    {
        UnityThread.Initialize();
        var thinking = new ThinkingDataAnalyticsBackend("fixture", "https://example.invalid");
        var game = new GaBackend();
        var appsflyer = new AppsFlyerAttributionBackend("fixture-key");
        var adjust = new AdjustAttributionBackend("fixture-token", new Dictionary<string, string>());
        using (var analytics = new AnalyticsManager(new IAnalyticsBackend[] { thinking, game }, true))
        using (var af = new AttributionManager(appsflyer, true))
        using (var ad = new AttributionManager(adjust, true))
        {
            analytics.Start(false);
            af.Start(false);
            ad.Start(false);
            Assert.IsFalse(thinking.IsStarted);
            Assert.IsFalse(game.IsStarted);
            Assert.IsFalse(appsflyer.IsStarted);
            Assert.IsFalse(adjust.IsStarted);
        }
    }
}
