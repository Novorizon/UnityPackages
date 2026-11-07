using Game.Framework.Diagnostics.Editor;
using NUnit.Framework;

public sealed class ContentAuditDiagnosticsTests
{
    private sealed class Probe
    {
        internal static int Reads;
        public static int Hidden
        {
            private get
            {
                Reads++;
                return 42;
            }
            set
            {
            }
        }
        public static int Visible => 7;
    }

    [Test]
    public void PublicPropertyWithPrivateGetterIsNeverInvokedByTheReadOnlyProbe()
    {
        Probe.Reads = 0;
        var metrics = PackageDiagnostics.Read(typeof(Probe), false, "Hidden", "Visible");
        Assert.That(Probe.Reads, Is.Zero);
        Assert.That(metrics.Length, Is.EqualTo(2));
        Assert.That(metrics[0].value, Does.StartWith("Error:"));
        Assert.That(metrics[1].value, Is.EqualTo("7"));
    }
}
