using System;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture]
    public class MetricMathTests
    {
        [Test]
        public void CpuExcludesIdleTimeFromKernelAndUserDeltas()
        {
            Assert.That(MetricMath.Cpu(150, 400, 100, 200), Is.EqualTo(75));
        }

        [TestCase(100UL, 200UL, 100UL, 200UL)]
        [TestCase(10UL, 20UL, 100UL, 200UL)]
        public void InvalidCpuIntervalsAreUnavailable(ulong idle, ulong total, ulong oldIdle, ulong oldTotal)
        {
            Assert.That(MetricMath.Cpu(idle, total, oldIdle, oldTotal), Is.NaN);
        }

        [Test]
        public void NetworkRatesUseElapsedTime() { Assert.That(MetricMath.Rate(2048, 1024, 0.5), Is.EqualTo(2048)); }

        [Test]
        public void CounterResetIsUnavailable() { Assert.That(MetricMath.Rate(5, 100, 1), Is.NaN); }

        [TestCase(112, 100)]
        [TestCase(-3, 0)]
        public void PercentagesAreBounded(double value, double expected)
        {
            Assert.That(MetricMath.Percent(value), Is.EqualTo(expected));
        }

        [Test]
        public void NetworkLabelsRetainRateUnits() { Assert.That(MetricMath.RateText(2048), Does.EndWith("KB/s")); }
    }

    [TestFixture]
    public class GpuUtilizationTests
    {
        private GpuUtilization gpu;

        [SetUp]
        public void AddTwoProcessesOnTheSameEngine()
        {
            gpu = new GpuUtilization();
            gpu.Add("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 40, 0);
            gpu.Add("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 35, 1);
        }

        [Test]
        public void AddsProcessesUsingTheSameEngine() { Assert.That(gpu.Value, Is.EqualTo(75)); }

        [TestCase("pid_3_luid_0x0_0x1_phys_0_eng_1_engtype_Copy")]
        [TestCase("pid_4_luid_0x0_0x2_phys_0_eng_0_engtype_3D")]
        [TestCase("pid_5_luid_0x0_0x1_phys_1_eng_0_engtype_3D")]
        public void KeepsAdaptersPhysicalGpusAndEnginesSeparate(string instance)
        {
            gpu.Add(instance, 60, 0);
            Assert.That(gpu.Value, Is.EqualTo(75));
        }

        [Test]
        public void KeepsDifferentIndexesOfTheSameEngineTypeSeparate()
        {
            gpu.Add("pid_3_luid_0x0_0x1_phys_0_eng_1_engtype_Copy", 60, 0);
            gpu.Add("pid_6_luid_0x0_0x1_phys_0_eng_2_engtype_Copy", 70, 0);
            Assert.That(gpu.Value, Is.EqualTo(75));
        }

        [Test]
        public void SwitchesToTheBusiestAdapter()
        {
            gpu.Add("pid_4_luid_0x0_0x2_phys_0_eng_0_engtype_3D", 55, 0);
            gpu.Add("pid_7_luid_0x0_0x2_phys_0_eng_0_engtype_3D", 35, 0);
            Assert.That(gpu.Value, Is.EqualTo(90));
        }

        [TestCase("pid_8_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 200, 0xC0000BC6u)]
        [TestCase("unrecognized", 100, 0u)]
        [TestCase("pid_9_luid_0x0_0x1_phys_0_eng_0_engtype_3D", double.NaN, 0u)]
        [TestCase("pid_9_luid_0x0_0x1_phys_0_eng_0_engtype_3D", double.PositiveInfinity, 0u)]
        public void IgnoresInvalidOrDisappearingInstances(string instance, double value, uint status)
        {
            gpu.Add(instance, value, status);
            Assert.That(gpu.Value, Is.EqualTo(75));
        }

        [Test]
        public void BoundsUtilizationAtOneHundredPercent()
        {
            gpu.Add("pid_10_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 40, 0);
            Assert.That(gpu.Value, Is.EqualTo(100));
        }

        [Test]
        public void MissingSamplesAreUnavailable() { Assert.That(new GpuUtilization().Value, Is.NaN); }

        [Test]
        public void ValidIdleEngineReportsZero()
        {
            GpuUtilization idle = new GpuUtilization();
            idle.Add("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 0, 0);
            Assert.That(idle.Value, Is.Zero);
        }
    }

    [TestFixture]
    public class HistoryTests
    {
        private History history;

        [SetUp]
        public void PopulateNinetySecondsOfSamples()
        {
            history = new History();
            DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i <= 180; i++)
                history.Add(new Sample { Time = start.AddSeconds(i * 0.5), Cpu = i % 100, Download = 2000, Upload = 1000 });
        }

        [Test]
        public void RetainsSixtySecondsAtHalfSecondSampling()
        {
            Assert.That(history.Samples.Count, Is.EqualTo(121));
            Assert.That((history.Latest.Time - history.Samples[0].Time).TotalSeconds, Is.EqualTo(60));
        }

        [Test]
        public void AutoScalesNetworkAndFixesPercentageScales()
        {
            Assert.That(history.Maximum(Metric.Network), Is.EqualTo(4096));
            Assert.That(history.Maximum(Metric.Cpu), Is.EqualTo(100));
            Assert.That(history.Maximum(Metric.Gpu), Is.EqualTo(100));
            Assert.That(new Sample { Gpu = 65 }.Value(Metric.Gpu), Is.EqualTo(65));
        }

        [Test]
        public void ResumingRemovesStaleHistory()
        {
            history.Add(new Sample { Time = history.Latest.Time.AddMinutes(5), Cpu = 30 });
            Assert.That(history.Samples.Count, Is.EqualTo(1));
        }
    }
}
