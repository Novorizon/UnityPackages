using System;
using System.Linq;
using Game.Framework.Diagnostics.Editor;
using Game.Framework.Localization;
using Game.Resource;
using NUnit.Framework;
using UI;
using UnityEngine;

public sealed class NativeDiagnosticsAcceptanceTests
{
    [Test]
    public void CaptureReportsUncreatedServicesWithoutCreatingThem()
    {
        Assert.That(ResourceManager.TryGetInstance(out _), Is.False, "Run acceptance with no resource service created.");
        Assert.That(LocalizationManager.TryGetInstance(out _), Is.False, "Run acceptance with no localization service created.");
        Assert.That(UIManager.TryGetInstance(out _), Is.False, "Run acceptance with no UI service created.");

        var snapshot = PackageDiagnostics.Capture();

        Assert.That(ResourceManager.TryGetInstance(out _), Is.False);
        Assert.That(LocalizationManager.TryGetInstance(out _), Is.False);
        Assert.That(UIManager.TryGetInstance(out _), Is.False);
        Assert.That(snapshot.leases, Is.Empty);
        foreach (string service in new[] { nameof(ResourceManager), nameof(LocalizationManager), nameof(UIManager) })
        {
            Assert.That(
                snapshot.metrics.Any(metric => metric.name == service && metric.value == "Not created / released"),
                Is.True,
                service + " must be observed through its existing read-only accessor.");
        }
    }

    [Test]
    public void OwnershipAndBaselineChangesSurviveNativeJsonUtilityRoundTrip()
    {
        var baseline = new DiagnosticSnapshot
        {
            capturedUtc = "2026-11-07T00:00:00.0000000Z",
            metrics = Array.Empty<DiagnosticMetric>(),
            leases = new[] { Lease("Loading") }
        };
        var current = new DiagnosticSnapshot
        {
            capturedUtc = "2026-11-07T00:00:01.0000000Z",
            metrics = Array.Empty<DiagnosticMetric>(),
            leases = new[] { Lease("Releasing") }
        };
        var export = new DiagnosticExport
        {
            snapshot = current,
            baseline = baseline,
            difference = DiagnosticSnapshotComparer.Compare(baseline, current)
        };

        string json = JsonUtility.ToJson(export);
        foreach (string field in new[] { "snapshot", "baseline", "difference", "owner", "scopeId", "sceneHandle", "creationStack", "before", "after" })
        {
            StringAssert.Contains("\"" + field + "\":", json);
        }
        var restored = JsonUtility.FromJson<DiagnosticExport>(json);

        Assert.That(restored.snapshot.capturedUtc, Is.EqualTo(current.capturedUtc));
        Assert.That(restored.baseline.capturedUtc, Is.EqualTo(baseline.capturedUtc));
        Assert.That(restored.snapshot.leases, Has.Length.EqualTo(1));
        var row = restored.snapshot.leases[0];
        Assert.That(row.sessionId, Is.EqualTo("editor-session"));
        Assert.That(row.leaseId, Is.EqualTo(17));
        Assert.That(row.owner, Is.EqualTo("Battle/HUD"));
        Assert.That(row.scopeId, Is.EqualTo(3));
        Assert.That(row.sceneHandle, Is.EqualTo(42));
        Assert.That(row.sceneName, Is.EqualTo("Battle"));
        Assert.That(row.location, Is.EqualTo("portraits/hero"));
        Assert.That(row.assetType, Is.EqualTo("UnityEngine.Sprite"));
        Assert.That(row.state, Is.EqualTo("Releasing"));
        Assert.That(row.creationStack, Is.EqualTo("at StoryPortrait.Open()"));
        Assert.That(row.source, Is.EqualTo("Independent acquisition"));
        Assert.That(restored.difference.baselineUtc, Is.EqualTo(baseline.capturedUtc));
        Assert.That(restored.difference.capturedUtc, Is.EqualTo(current.capturedUtc));
        Assert.That(restored.difference.leases, Has.Length.EqualTo(1));
        Assert.That(restored.difference.leases[0].kind, Is.EqualTo("Changed"));
        Assert.That(restored.difference.leases[0].before.owner, Is.EqualTo("Battle/HUD"));
        Assert.That(restored.difference.leases[0].before.state, Is.EqualTo("Loading"));
        Assert.That(restored.difference.leases[0].after.state, Is.EqualTo("Releasing"));
    }

    private static DiagnosticLease Lease(string state)
    {
        return new DiagnosticLease
        {
            sessionId = "editor-session",
            leaseId = 17,
            owner = "Battle/HUD",
            scopeId = 3,
            sceneHandle = 42,
            sceneName = "Battle",
            location = "portraits/hero",
            assetType = "UnityEngine.Sprite",
            state = state,
            creationStack = "at StoryPortrait.Open()"
        };
    }
}
