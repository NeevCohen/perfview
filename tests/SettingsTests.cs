using System;
using System.IO;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture]
    public class SettingsTests
    {
        private string directory, settingsPath;

        [SetUp]
        public void CreateTemporaryDirectory()
        {
            directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            settingsPath = Path.Combine(directory, "settings.xml");
        }

        [TearDown]
        public void RemoveTemporaryDirectory()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void InvalidSettingsRecoverToUsableValues()
        {
            Settings settings = new Settings { ShowCpu = false, ShowGpu = false, ShowMemory = false, ShowDisk = false, ShowNetwork = false, Offset = -20, CellWidth = 2, Interval = 40, Theme = "invalid" };
            settings.Normalize();
            Assert.That(settings.Metrics, Is.EqualTo(new[] { Metric.Cpu }));
            Assert.That(settings.Offset, Is.Zero);
            Assert.That(settings.CellWidth, Is.EqualTo(68));
            Assert.That(settings.Interval, Is.EqualTo(1000));
            Assert.That(settings.Theme, Is.EqualTo("System"));
        }

        [Test]
        public void SavingReplacesExistingSettingsAndRoundTrips()
        {
            Settings settings = new Settings { AlignRight = true, Offset = 247, Theme = "Light", Interval = 500 };
            settings.Save(settingsPath);
            settings.Offset = 312;
            settings.Save(settingsPath);
            Settings loaded = Settings.Load(settingsPath);
            Assert.That(loaded.Offset, Is.EqualTo(312));
            Assert.That(loaded.AlignRight, Is.True);
            Assert.That(loaded.Theme, Is.EqualTo("Light"));
            Assert.That(loaded.Interval, Is.EqualTo(500));
        }

        [Test]
        public void DisabledGpuVisibilityPersists()
        {
            new Settings { ShowGpu = false }.Save(settingsPath);
            Assert.That(Settings.Load(settingsPath).ShowGpu, Is.False);
        }

        [Test]
        public void LegacySettingsGainGpuWithoutLosingPreferences()
        {
            File.WriteAllText(settingsPath, "<Settings><Offset>1934</Offset><CellWidth>88</CellWidth><ShowDisk>false</ShowDisk></Settings>");
            Settings upgraded = Settings.Load(settingsPath);
            Assert.That(upgraded.ShowGpu, Is.True);
            Assert.That(upgraded.Offset, Is.EqualTo(1934));
            Assert.That(upgraded.CellWidth, Is.EqualTo(88));
            Assert.That(upgraded.ShowDisk, Is.False);
        }

        [Test]
        public void CorruptSettingsRecoverWithoutStartupFailure()
        {
            File.WriteAllText(settingsPath, "not xml");
            Assert.That(Settings.Load(settingsPath).ShowCpu, Is.True);
        }

        [Test]
        public void TemperatureOptInPersistsAndLegacySettingsRemainOff()
        {
            File.WriteAllText(settingsPath, "<Settings><Theme>Dark</Theme></Settings>");
            Assert.That(Settings.Load(settingsPath).ShowTemperatures, Is.False);
            new Settings { ShowTemperatures = true }.Save(settingsPath);
            Assert.That(Settings.Load(settingsPath).ShowTemperatures, Is.True);
            new Settings { ShowTemperatures = false }.Save(settingsPath);
            Assert.That(Settings.Load(settingsPath).ShowTemperatures, Is.False);
        }
    }
}
