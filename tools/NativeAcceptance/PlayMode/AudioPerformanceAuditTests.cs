using System;
using System.Collections;
using Game.Framework;
using Game.Framework.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class AudioPerformanceAuditTests
{
    [Serializable]
    private sealed class PerformanceMetric
    {
        public string name;
        public string scope;
        public int samples;
        public int operationsPerSample;
        public double medianMicrosecondsPerOperation;
        public double p95MicrosecondsPerOperation;
        public long allocatedBytes;
        public double allocatedBytesPerOperation;
    }

    [UnityTest]
    public IEnumerator RepeatedFadeConfigurationValidatesCurvesWithoutBoxing()
    {
        bool ownThread = !UnityThread.IsInitialized;
        if (ownThread)
        {
            UnityThread.Initialize();
        }
        bool ownService = !AudioManager.TryGetInstance(out var audio);
        audio = audio != null ? audio : AudioManager.Instance;
        bool automatic = audio.AutomaticUpdate;
        audio.AutomaticUpdate = false;
        var clip = AudioClip.Create("Audit silent loop", 256, 1, 22050, false);
        AudioHandle voice = default;
        try
        {
            voice = audio.Play(clip, loop: true);
            Assert.That(voice.FadeTo(.5f, 1, AudioFadeCurve.Linear), Is.True);
            Assert.That(voice.FadeTo(.5f, 1, AudioFadeCurve.SmoothStep), Is.True);
            Assert.That(voice.FadeTo(.5f, 1, AudioFadeCurve.EqualPower), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => voice.FadeTo(.5f, 1, (AudioFadeCurve)(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => voice.FadeTo(.5f, 1, (AudioFadeCurve)3));
            for (int i = 0; i < 1024; i++)
            {
                voice.FadeTo(.5f, 1, AudioFadeCurve.EqualPower);
            }
            const int samples = 31;
            const int operations = 256;
            var times = new double[samples];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int sample = 0; sample < samples; sample++)
            {
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < operations; i++)
                {
                    voice.FadeTo(.5f, 1, AudioFadeCurve.EqualPower);
                }
                times[sample] = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000000d / System.Diagnostics.Stopwatch.Frequency / operations;
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Array.Sort(times);
            TestContext.Out.WriteLine("NOVORIZON_PERF " + JsonUtility.ToJson(new PerformanceMetric
            {
                name = "audio.fade.setup",
                scope = "Unity Editor Mono; one owned looping silent AudioClip; fade setup only",
                samples = samples,
                operationsPerSample = operations,
                medianMicrosecondsPerOperation = times[samples / 2],
                p95MicrosecondsPerOperation = times[(int)Math.Ceiling(samples * .95) - 1],
                allocatedBytes = bytes,
                allocatedBytesPerOperation = bytes / (double)(samples * operations)
            }));
            Assert.That(bytes, Is.Zero);
            Assert.That(voice.IsValid, Is.True);
        }
        finally
        {
            voice.Stop();
            audio.AutomaticUpdate = automatic;
            if (ownService)
            {
                audio.Dispose();
            }
            Object.Destroy(clip);
            if (ownThread)
            {
                UnityThread.Reset();
            }
        }
        yield return null;
    }
}
