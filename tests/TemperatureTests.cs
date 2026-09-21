using System;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture]
    public class TemperatureTests
    {
        internal static TemperatureSnapshot Snapshot(params TemperatureReading[] readings)
        { return new TemperatureSnapshot(DateTime.UtcNow, readings, ""); }

        [Test]
        public void MissingAndInvalidValuesDoNotBecomeZeroOrHideValidSensors()
        {
            TemperatureReading[] readings = { new TemperatureReading("cpu", "CPU", "Package", 65, Metric.Cpu), new TemperatureReading("gpu", "GPU", "Hot spot", 108.5, Metric.Gpu), new TemperatureReading("missing", "RAM", "Missing", null, Metric.Memory), new TemperatureReading("invalid", "Disk", "Invalid", double.PositiveInfinity, Metric.Disk) };
            TemperatureSnapshot snapshot = Snapshot(readings);
            readings[0] = new TemperatureReading("cpu", "CPU", "Package", 200, Metric.Cpu);
            Assert.That(snapshot.ForComponent(Metric.Cpu), Is.EqualTo(65), "Published snapshots must not share a mutable sensor array");
            Assert.That(snapshot.ForComponent(Metric.Gpu), Is.EqualTo(108.5));
            Assert.That(snapshot.ForComponent(Metric.Memory), Is.NaN);
            Assert.That(snapshot.ForComponent(Metric.Disk), Is.NaN);
            Assert.That(snapshot.Readings[2].Celsius, Is.NaN);
            Assert.That(snapshot.Readings[3].Celsius, Is.NaN);
            Assert.That(Snapshot().ForComponent(Metric.Cpu), Is.NaN);
            Assert.That(TemperatureReading.Text(double.NaN), Is.EqualTo("\u2014"));
        }

        [Test]
        public void StaleReadingsExpireWithoutMutatingHistory()
        {
            TemperatureSnapshot snapshot = Snapshot(new TemperatureReading("cpu", "CPU", "Package", 65, Metric.Cpu));
            Assert.That(snapshot.FreshAt(snapshot.Time.AddSeconds(9)).ForComponent(Metric.Cpu), Is.EqualTo(65));
            Assert.That(snapshot.FreshAt(snapshot.Time.AddSeconds(11)).ForComponent(Metric.Cpu), Is.NaN);
            Assert.That(snapshot.ForComponent(Metric.Cpu), Is.EqualTo(65));
        }

        [Test]
        public void MissingDriverAccessCannotProduceZeroDegreeCpuReadings()
        {
            Assert.That(HardwareTemperatureSource.SensorValue(0, HardwareType.Cpu, false), Is.Null);
            Assert.That(HardwareTemperatureSource.SensorValue(42, HardwareType.SuperIO, false), Is.Null);
            Assert.That(HardwareTemperatureSource.SensorValue(0, HardwareType.Cpu, true), Is.EqualTo(0));
            Assert.That(HardwareTemperatureSource.SensorValue(55, HardwareType.GpuNvidia, false), Is.EqualTo(55));
        }

        [Test]
        public void GraphHeadersPairEachUsageWithItsOwnTemperatureAndKeepTheUsageScale()
        {
            History history = new History();
            Sample sample = new Sample { Cpu = 35, Gpu = 72, Memory = 50, Download = 1024, Upload = 0, Temperatures = Snapshot(new TemperatureReading("cpu", "CPU", "Package", 65, Metric.Cpu), new TemperatureReading("gpu", "GPU", "GPU Core", 108, Metric.Gpu)) };
            history.Add(sample);
            Assert.That(sample.Value(Metric.Gpu), Is.EqualTo(72));
            Assert.That(history.Maximum(Metric.Gpu), Is.EqualTo(100));
            Assert.That(GraphPaint.Value(sample, Metric.Cpu, true), Is.EqualTo("35%  65\u00b0C"));
            Assert.That(GraphPaint.Value(sample, Metric.Gpu, true), Is.EqualTo("72%  108\u00b0C"));
            Assert.That(GraphPaint.Value(sample, Metric.Memory, true), Is.EqualTo("50%"));
            Assert.That(GraphPaint.Value(sample, Metric.Memory, true, true), Is.EqualTo("50%"));
            Assert.That(GraphPaint.Value(sample, Metric.Gpu, false), Is.EqualTo("72%"));
            Assert.That(GraphPaint.Value(sample, Metric.Network, true), Is.EqualTo(MetricMath.RateText(1024)));
            Assert.That(GraphPaint.Value(sample, Metric.Network, true, true), Is.EqualTo(MetricMath.RateText(1024).Replace(" ", "")));
        }

        [Test]
        public void SelectsTheWarmestGpuCoreAcrossDevicesWithoutUsingTheirHotSpotsOrVram()
        {
            TemperatureSnapshot snapshot = Snapshot(
                new TemperatureReading("gpu0core", "GPU", "GPU Core", 40, Metric.Gpu, "gpu0"),
                new TemperatureReading("gpu0spot", "GPU", "GPU Hot Spot", 70, Metric.Gpu, "gpu0"),
                new TemperatureReading("gpu1core", "GPU", "GPU Core", 60, Metric.Gpu, "gpu1"),
                new TemperatureReading("gpu1mem", "GPU", "GPU Memory Junction", 110, Metric.Gpu, "gpu1"),
                new TemperatureReading("ram", "RAM", "DIMM", 42, Metric.Memory),
                new TemperatureReading("cpu", "CPU", "Package", 80, Metric.Cpu));
            Assert.That(snapshot.ForComponent(Metric.Gpu), Is.EqualTo(60));
            Assert.That(snapshot.ForComponent(Metric.Memory), Is.EqualTo(42));
            Assert.That(snapshot.ForComponent(Metric.Cpu), Is.EqualTo(80));
            Assert.That(snapshot.ForComponent(Metric.Disk), Is.NaN);
        }

        [Test]
        public void PrefersCpuPackageAndDriveCompositeAndFallsBackToAvailableSensors()
        {
            TemperatureSnapshot snapshot = Snapshot(
                new TemperatureReading("package", "CPU", "CPU Package", 65, Metric.Cpu, "cpu"),
                new TemperatureReading("core", "CPU", "Core #1", 75, Metric.Cpu, "cpu"),
                new TemperatureReading("ssd1", "SSD", "Composite Temperature", 40, Metric.Disk, "ssd"),
                new TemperatureReading("ssd2", "SSD", "Temperature 2", 60, Metric.Disk, "ssd"),
                new TemperatureReading("gpucore", "GPU", "GPU Core", null, Metric.Gpu, "gpu"),
                new TemperatureReading("gpuspot", "GPU", "GPU Hot Spot", 85, Metric.Gpu, "gpu"));
            Assert.That(snapshot.ForComponent(Metric.Cpu), Is.EqualTo(65));
            Assert.That(snapshot.ForComponent(Metric.Disk), Is.EqualTo(40));
            Assert.That(snapshot.ForComponent(Metric.Gpu), Is.EqualTo(85));
        }

        [TestCase(HardwareType.Cpu, "Cpu")]
        [TestCase(HardwareType.GpuAmd, "Gpu")]
        [TestCase(HardwareType.GpuNvidia, "Gpu")]
        [TestCase(HardwareType.GpuIntel, "Gpu")]
        [TestCase(HardwareType.Memory, "Memory")]
        [TestCase(HardwareType.Storage, "Disk")]
        [TestCase(HardwareType.Network, "Network")]
        [TestCase(HardwareType.Motherboard, null)]
        public void MapsHardwareTypesToMatchingGraphs(HardwareType type, string expected)
        {
            Metric? component = HardwareTemperatureSource.ComponentFor(type);
            Assert.That(component.HasValue ? component.Value.ToString() : null, Is.EqualTo(expected));
        }

        private sealed class FakeSource : ITemperatureSource
        {
            internal int Reads, Disposals;
            internal Action OnRead;
            public TemperatureSnapshot Read()
            {
                Reads++;
                if (OnRead != null) OnRead();
                return Snapshot(new TemperatureReading("cpu", "CPU", "Package", 65, Metric.Cpu));
            }
            public void Dispose() { Disposals++; }
        }

        [Test]
        public void DisabledMonitoringNeverOpensHardwareAndReleasesItOnDisable()
        {
            FakeSource source = new FakeSource();
            int opened = 0;
            using (TemperatureService service = new TemperatureService(false, 1000, delegate { opened++; return source; }, false))
            {
                service.Refresh();
                Assert.That(opened, Is.Zero);
                service.SetEnabled(true); service.Refresh();
                Assert.That(service.Latest.ForComponent(Metric.Cpu), Is.EqualTo(65));
                service.SetEnabled(false);
                Assert.That(service.Latest.ForComponent(Metric.Cpu), Is.NaN);
                service.Refresh();
                Assert.That(source.Disposals, Is.EqualTo(1));
                Assert.That(source.Reads, Is.EqualTo(1));
                service.SetEnabled(true); service.Refresh();
                Assert.That(opened, Is.EqualTo(2));
            }
            Assert.That(source.Disposals, Is.EqualTo(2));
        }

        [Test]
        public void SwitchingOffDuringAReadDiscardsItsResult()
        {
            FakeSource source = new FakeSource();
            using (TemperatureService service = new TemperatureService(true, 1000, delegate { return source; }, false))
            {
                source.OnRead = delegate { service.SetEnabled(false); };
                service.Refresh();
                Assert.That(service.Latest, Is.SameAs(TemperatureSnapshot.Disabled));
            }
        }

        [Test]
        public void FailuresClearValuesAndBackOffUntilOptionIsRetried()
        {
            FakeSource source = new FakeSource();
            using (TemperatureService service = new TemperatureService(true, 1000, delegate { return source; }, false))
            {
                service.Refresh();
                source.OnRead = delegate { throw new InvalidOperationException("Device disconnected"); };
                Assert.DoesNotThrow((Action)service.Refresh);
                Assert.That(service.Latest.ForComponent(Metric.Cpu), Is.NaN);
                Assert.That(service.Latest.Status, Does.Contain("unavailable"));
                service.Refresh();
                Assert.That(source.Reads, Is.EqualTo(2));
                source.OnRead = null;
                service.SetEnabled(false); service.SetEnabled(true); service.Refresh();
                Assert.That(service.Latest.ForComponent(Metric.Cpu), Is.EqualTo(65));
            }
        }

        [Test, Category("LiveCounters")]
        public void LiveTemperatureDiscoveryCompletesAndReportsOnlyValidOrMissingReadings()
        {
            string script = Path.Combine(TestContext.CurrentContext.TestDirectory, "Read-TemperatureProbe.ps1");
            string app = Path.GetDirectoryName(typeof(Settings).Assembly.Location);
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            using (Process process = Process.Start(new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -AppDirectory \"" + app + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                System.Threading.Tasks.Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(); Assert.Fail("Hardware discovery did not finish within 60 seconds."); }
                TestContext.WriteLine(output.Result);
                Assert.That(process.ExitCode, Is.Zero, error.Result);
                Assert.That(output.Result, Does.Contain("Temperature sensors:"));
            }
        }
    }

    [TestFixture, Category("UI"), Apartment(ApartmentState.STA), NonParallelizable]
    public class TemperatureWindowTests
    {
        private static History Preview()
        {
            History history = new History();
            for (int i = 0; i <= 60; i++) history.Add(new Sample { Time = DateTime.UtcNow.AddSeconds(i - 60), Cpu = 35 + 8 * Math.Sin(i), Gpu = 72 + 8 * Math.Sin(i / 2.0), Memory = 50, Disk = 6, Download = 102400, Upload = 0, Temperatures = TemperatureTests.Snapshot(new TemperatureReading("cpu", "CPU", "Package", 65, Metric.Cpu), new TemperatureReading("gpu", "GPU", "GPU Core", 49, Metric.Gpu), new TemperatureReading("disk", "SSD", "Temperature", 40, Metric.Disk)) });
            return history;
        }

        [TestCase(68, "Dark", false, 1f)]
        [TestCase(88, "Dark", false, 1f)]
        [TestCase(160, "Light", false, 1f)]
        [TestCase(68, "Dark", true, 1f)]
        [TestCase(88, "Dark", true, 1f)]
        [TestCase(68, "Light", true, 1.5f)]
        [TestCase(88, "Dark", true, 2f)]
        public void TaskbarHeadersKeepReadingsOnOneLineAtSupportedWidths(int width, string theme, bool longReadings, float scale)
        {
            History history = Preview();
            if (longReadings)
            {
                history.Latest.Cpu = history.Latest.Gpu = history.Latest.Memory = history.Latest.Disk = 100;
                history.Latest.Download = 1023 * 1024 * 1024.0;
                history.Latest.Temperatures = TemperatureTests.Snapshot(Enum.GetValues(typeof(Metric)).Cast<Metric>().Select(metric => new TemperatureReading(metric.ToString(), metric.ToString(), "Temperature", 108, metric)).ToArray());
            }
            Palette palette = Palette.Create(theme);
            using (MiniGraphs graphs = new MiniGraphs(history, new Settings { ShowTemperatures = true }, palette) { Size = new Size((int)(width * 5 * scale), (int)(40 * scale)) })
            using (Bitmap bitmap = new Bitmap(graphs.Width, graphs.Height))
            {
                graphs.CreateControl(); graphs.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "taskbar-temperatures-" + width + (longReadings ? "-long-" + scale : "") + ".png");
                bitmap.Save(path, ImageFormat.Png); TestContext.AddTestAttachment(path);
                for (int cell = 0; cell < 5; cell++)
                {
                    int pixels = 0;
                    for (int x = (int)(cell * width * scale); x < (int)((cell + 1) * width * scale); x++)
                        for (int y = 0; y < bitmap.Height; y++)
                        {
                            Color pixel = bitmap.GetPixel(x, y);
                            if (Math.Abs(pixel.R - palette.Text.R) > 15 || Math.Abs(pixel.G - palette.Text.G) > 15 || Math.Abs(pixel.B - palette.Text.B) > 15) continue;
                            pixels++;
                            Assert.That(y, Is.InRange((int)(3 * scale), (int)(18 * scale) - 1), "Reading for " + (Metric)cell + " must stay on the single header line, above the graph.");
                        }
                    Assert.That(pixels, Is.GreaterThan(5), "Reading for " + (Metric)cell + " must remain visible.");
                }
            }
        }

        [Test]
        public void MissingTemperatureKeepsOnlyUsageInGraphHeadersAndAccessibleText()
        {
            History history = new History();
            Sample sample = new Sample { Cpu = 35, Temperatures = new TemperatureSnapshot(DateTime.UtcNow, new TemperatureReading[0], "Some temperature unavailable: install PawnIO, then restart Perfview as administrator.") };
            history.Add(sample);
            using (DetailsWindow window = new DetailsWindow(history, Palette.Create("Dark"), true))
            {
                window.UpdateSample(sample, false, 1000);
                string description = window.Controls.OfType<DetailGraphs>().Single().AccessibleDescription;
                Assert.That(description, Does.Contain("CPU 35% |"));
                Assert.That(description, Does.Not.Contain("\u00b0C"));
                Assert.That(window.Controls.OfType<LinkLabel>().Single().Text, Does.Contain("PawnIO"));
            }
        }

        [Test]
        public void TemperatureOptionDoesNotAddAGraphOrChangeTheSelectedComponents()
        {
            using (SettingsWindow window = new SettingsWindow(new Settings { ShowCpu = false, ShowGpu = true, ShowMemory = false, ShowDisk = false, ShowNetwork = false, ShowTemperatures = false }, Palette.Create("Dark")))
            {
                window.Show();
                window.Controls.OfType<CheckBox>().Single(check => check.Text.StartsWith("Show temperature")).Checked = true;
                window.AcceptButton.PerformClick();
                Assert.That(window.Result, Is.Not.Null);
                Assert.That(window.Result.ShowTemperatures, Is.True);
                Assert.That(window.Result.Metrics, Is.EqualTo(new[] { Metric.Gpu }));
            }
        }

        [Test]
        public void TemperaturesAloneStillRequireAUsageGraph()
        {
            Settings settings = new Settings { ShowCpu = false, ShowGpu = false, ShowMemory = false, ShowDisk = false, ShowNetwork = false, ShowTemperatures = true };
            settings.Normalize();
            Assert.That(settings.Metrics, Is.EqualTo(new[] { Metric.Cpu }));
        }

        [TestCase("Dark")]
        [TestCase("Light")]
        public void DetailsShowsTemperaturesDirectlyInTheExistingGraphs(string theme)
        {
            History history = Preview();
            Sample sample = history.Latest;
            using (DetailsWindow window = new DetailsWindow(history, Palette.Create(theme), true))
            {
                window.UpdateSample(sample, false, 1000);
                window.Show();
                Application.DoEvents();
                DetailGraphs graphs = window.Controls.OfType<DetailGraphs>().Single();
                Assert.That(graphs.Visible, Is.True);
                Assert.That(graphs.AccessibleDescription, Does.Contain("GPU " + GraphPaint.Value(sample, Metric.Gpu, true)));
                Assert.That(window.Controls.OfType<Button>().Any(button => button.Text == "Temperatures"), Is.False);
                string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "component-temperatures-" + theme.ToLowerInvariant() + ".png");
                using (Bitmap bitmap = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path, ImageFormat.Png);
                }
                TestContext.AddTestAttachment(path);
            }
        }
    }
}
