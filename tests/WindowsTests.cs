using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture, Category("LiveCounters")]
    public class LiveCounterTests
    {
        private Sample first, live;

        [OneTimeSetUp]
        public void SampleWindowsCounters()
        {
            using (MetricsReader reader = new MetricsReader())
            {
                first = reader.Read();
                Thread.Sleep(1100);
                live = reader.Read();
            }
        }

        [Test]
        public void WarmupDoesNotFabricateUtilization()
        {
            Assert.That(first.Cpu, Is.NaN);
            Assert.That(first.Gpu, Is.NaN);
            Assert.That(first.TotalMemory, Is.GreaterThan(0));
        }

        [Test]
        public void CpuIsAValidPercentage() { Assert.That(live.Cpu, Is.InRange(0, 100)); }
        [Test]
        public void GpuIsAValidPercentage() { Assert.That(live.Gpu, Is.InRange(0, 100)); }
        [Test]
        public void MemoryIsValid()
        {
            Assert.That(live.Memory, Is.GreaterThan(0).And.LessThanOrEqualTo(100));
            Assert.That(live.TotalMemory, Is.GreaterThanOrEqualTo(live.UsedMemory));
        }
        [Test]
        public void DiskIsAValidPercentage() { Assert.That(live.Disk, Is.InRange(0, 100)); }
        [Test]
        public void NetworkRatesAreNonnegative()
        {
            Assert.That(live.Download, Is.GreaterThanOrEqualTo(0));
            Assert.That(live.Upload, Is.GreaterThanOrEqualTo(0));
        }
    }

    [TestFixture, Category("UI"), Apartment(ApartmentState.STA), NonParallelizable]
    public class WindowTests
    {
        private History preview;

        [OneTimeSetUp]
        public void PrepareWindowsAndHistory()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(TestContext.CurrentContext.WorkDirectory);
            preview = new History();
            DateTime start = DateTime.UtcNow.AddSeconds(-60);
            for (int i = 0; i <= 60; i++)
                preview.Add(new Sample { Time = start.AddSeconds(i), Cpu = 32 + 22 * Math.Sin(i * 0.5), Gpu = 65 + 22 * Math.Sin(i * 0.25), Memory = 56 + 2 * Math.Sin(i * 0.16), Disk = 12 + 11 * Math.Sin(i), Download = 180000 + 140000 * Math.Sin(i * 0.44), Upload = 18000, TotalMemory = 32UL * 1024 * 1024 * 1024, UsedMemory = 18UL * 1024 * 1024 * 1024 });
        }

        [TestCase("Dark", "taskbar")]
        [TestCase("Light", "taskbar")]
        [TestCase("Dark", "details")]
        [TestCase("Light", "details")]
        [TestCase("Dark", "settings")]
        [TestCase("Light", "settings")]
        public void WindowRendersInEachTheme(string theme, string window)
        {
            Palette palette = Palette.Create(theme);
            Control control;
            if (window == "taskbar") control = new MiniGraphs(preview, new Settings(), palette) { Size = new Size(440, 40) };
            else if (window == "details") control = new DetailsWindow(preview, palette);
            else control = new SettingsWindow(new Settings(), palette);
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, window + "-" + theme.ToLowerInvariant() + ".png");
            using (control)
            {
                Form form = control as Form;
                if (form != null) { form.Show(); Application.DoEvents(); }
                else control.CreateControl();
                using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
                {
                    control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(path, ImageFormat.Png);
                }
                if (form != null) form.Close();
            }
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(0));
            TestContext.AddTestAttachment(path);
        }

        [Test]
        public void SettingsAcceptsGpuAsTheOnlyEnabledGraph()
        {
            using (SettingsWindow window = new SettingsWindow(new Settings { ShowCpu = false, ShowGpu = true, ShowMemory = false, ShowDisk = false, ShowNetwork = false }, Palette.Create("Dark")))
            {
                window.Show();
                Application.DoEvents();
                window.AcceptButton.PerformClick();
                Assert.That(window.Result, Is.Not.Null);
                Assert.That(window.Result.Metrics, Is.EqualTo(new[] { Metric.Gpu }));
            }
        }

        [TestCase(true, 246)]
        [TestCase(false, 2784)]
        public void SwitchingAnchorMovesTowardChosenEdgeDespiteOldDragOffset(bool originallyRight, int expectedLeft)
        {
            using (SettingsWindow window = new SettingsWindow(new Settings { AlignRight = originallyRight, Offset = 1756 }, Palette.Create("Dark")))
            {
                window.Show();
                ComboBox anchor = window.Controls.OfType<ComboBox>().Single(combo => combo.Items.Contains("Left / top edge"));
                anchor.SelectedIndex = originallyRight ? 0 : 1;
                window.AcceptButton.PerformClick();
                Assert.That(window.Result.AlignRight, Is.EqualTo(!originallyRight));
                Rectangle bar = new Rectangle(0, 2088, 3840, 72), placement;
                Rectangle[] controls = { new Rectangle(0, 2088, 237, 72), new Rectangle(1454, 2088, 932, 72), new Rectangle(3453, 2088, 387, 72) };
                Assert.That(Placement.TryFreeOverlay(bar, controls, 5, window.Result, 1.5f, out placement), Is.True);
                Assert.That(placement.Left, Is.EqualTo(expectedLeft));
                foreach (Rectangle control in controls) Assert.That(placement.IntersectsWith(control), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SavingWithoutSwitchingAnchorKeepsCustomOffset(bool right)
        {
            using (SettingsWindow window = new SettingsWindow(new Settings { AlignRight = right, Offset = 1756 }, Palette.Create("Dark")))
            {
                window.Show();
                window.AcceptButton.PerformClick();
                Assert.That(window.Result.AlignRight, Is.EqualTo(right));
                Assert.That(window.Result.Offset, Is.EqualTo(1756));
            }
        }
    }

    [TestFixture, Category("Desktop"), Apartment(ApartmentState.MTA), NonParallelizable]
    [Explicit("Requires an interactive Explorer taskbar; select this fixture to inspect the desktop.")]
    public class DesktopTaskbarTests
    {
        [Test]
        public void LiveTaskbarOffersCollisionFreePlacement()
        {
            TaskbarLayout layout = TaskbarLayout.Capture();
            TestContext.WriteLine("Reliable: {0}; taskbar: {1}; protected areas: {2}", layout.Reliable, layout.Bounds, layout.Occupied.Count);
            foreach (string item in layout.Diagnostics) TestContext.WriteLine(item);
            Assert.That(layout.Reliable, Is.True);
            Settings preferences = Settings.Load(Settings.FilePath);
            Rectangle placement;
            if (Placement.TryFreeOverlay(layout.Bounds, layout.Occupied, preferences.Metrics.Count, preferences, Native.Scale(layout.Handle), out placement))
                foreach (Rectangle item in layout.Occupied) Assert.That(item.IntersectsWith(placement), Is.False);
        }

        [Test]
        public void RunningOverlayStaysClearOfTaskbarControls()
        {
            IntPtr previous = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
            int visible = 0;
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(250);
                    TaskbarLayout current = TaskbarLayout.Capture();
                    IntPtr overlay = Native.FindWindow(null, "Perfview Taskbar");
                    Native.Rect actual;
                    if (overlay == IntPtr.Zero || !Native.IsWindowVisible(overlay) || !Native.GetWindowRect(overlay, out actual)) continue;
                    Assert.That(current.Reliable, Is.True, "Overlay visible without verified taskbar layout");
                    foreach (Rectangle item in current.Occupied)
                        Assert.That(item.IntersectsWith(actual.Rectangle), Is.False, "Running overlay covers a taskbar control");
                    Assert.That(current.Bounds.Contains(actual.Rectangle), Is.True);
                    visible++;
                }
            }
            finally { if (previous != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(previous); }
            Assert.That(visible, Is.GreaterThan(0), "Running overlay was never visible during the desktop check");
        }
    }
}
