using System.Linq;
using Game.Framework.Diagnostics.Editor;
using Game.Framework.Localization;
using Game.Resource;
using NUnit.Framework;
using UI;
using UnityEngine;
using YooAsset;

public sealed class NativeDiagnosticsAcceptanceTests
{
    [Test]
    public void CaptureReportsUncreatedServicesWithoutCreatingThem()
    {
        Assert.That(YooAssets.Initialized, Is.False, "Run acceptance with no resource service created.");
        Assert.That(LocalizationManager.TryGetInstance(out _), Is.False, "Run acceptance with no localization service created.");
        Assert.That(UIManager.TryGetInstance(out _), Is.False, "Run acceptance with no UI service created.");

        var snapshot = PackageDiagnostics.Capture();

        Assert.That(YooAssets.Initialized, Is.False);
        Assert.That(LocalizationManager.TryGetInstance(out _), Is.False);
        Assert.That(UIManager.TryGetInstance(out _), Is.False);
        Assert.That(snapshot.metrics.Any(metric => metric.name == "ResourceManager.IsReady" && metric.value == "False"), Is.True);
        foreach (string service in new[] { nameof(LocalizationManager), nameof(UIManager) })
        {
            Assert.That(
                snapshot.metrics.Any(metric => metric.name == service && metric.value == "Not created / released"),
                Is.True,
                service + " must be observed through its existing read-only accessor.");
        }
    }

    [Test]
    public void BaselineChangesSurviveNativeJsonUtilityRoundTrip()
    {
        var baseline = new DiagnosticSnapshot
        {
            capturedUtc = "2026-11-07T00:00:00.0000000Z",
            metrics = new[] { new DiagnosticMetric { name = "ResourceManager.IsReady", value = "False" } }
        };
        var current = new DiagnosticSnapshot
        {
            capturedUtc = "2026-11-07T00:00:01.0000000Z",
            metrics = new[] { new DiagnosticMetric { name = "ResourceManager.IsReady", value = "True" } }
        };
        var export = new DiagnosticExport
        {
            snapshot = current,
            baseline = baseline,
            difference = DiagnosticSnapshotComparer.Compare(baseline, current)
        };

        string json = JsonUtility.ToJson(export);
        var restored = JsonUtility.FromJson<DiagnosticExport>(json);

        Assert.That(restored.snapshot.capturedUtc, Is.EqualTo(current.capturedUtc));
        Assert.That(restored.baseline.capturedUtc, Is.EqualTo(baseline.capturedUtc));
        Assert.That(restored.snapshot.metrics[0].name, Is.EqualTo("ResourceManager.IsReady"));
        Assert.That(restored.snapshot.metrics[0].value, Is.EqualTo("True"));
        Assert.That(restored.difference.baselineUtc, Is.EqualTo(baseline.capturedUtc));
        Assert.That(restored.difference.capturedUtc, Is.EqualTo(current.capturedUtc));
        Assert.That(restored.difference.metrics, Has.Length.EqualTo(1));
        Assert.That(restored.difference.metrics[0].kind, Is.EqualTo("Changed"));
        Assert.That(restored.difference.metrics[0].before, Is.EqualTo("False"));
        Assert.That(restored.difference.metrics[0].after, Is.EqualTo("True"));
    }
}
