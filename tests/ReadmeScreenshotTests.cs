using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture, Category("Desktop"), Apartment(ApartmentState.STA), NonParallelizable]
    public class ReadmeScreenshotTests
    {
        [Test, Explicit("Captures a minute of live hardware readings and renders the README screenshots.")]
        public void RenderCurrentWindowsWithTemperaturesEnabled()
        {
            History history = new History();
            using (MetricsReader metrics = new MetricsReader())
            using (HardwareTemperatureSource temperatures = new HardwareTemperatureSource())
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                do
                {
                    Sample sample = metrics.Read();
                    sample.Temperatures = temperatures.Read();
                    history.Add(sample);
                    Thread.Sleep(1000);
                } while (elapsed.Elapsed.TotalSeconds < 61);
            }
            Settings settings = new Settings { Theme = "Dark", ShowTemperatures = true, AlignRight = true, Offset = 31 };
            Palette palette = Palette.Create(settings.Theme);
            IntPtr previousDpi = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                using (Icon icon = GraphPaint.CreateIcon())
                using (MiniGraphs graphs = new MiniGraphs(history, settings, palette) { Size = new Size(660, 60) })
                using (DetailsWindow details = new DetailsWindow(history, palette, true) { Icon = icon })
                using (SettingsWindow preferences = new SettingsWindow(settings, palette) { Icon = icon })
                {
                    graphs.CreateControl();
                    Save(graphs, "taskbar-dark.png");
                    details.UpdateSample(history.Latest, false, settings.Interval);
                    Save(details, "details-dark.png");
                    Save(preferences, "settings-dark.png");
                }
            }
            finally { Native.SetThreadDpiAwarenessContext(previousDpi); }
            TestContext.WriteLine(GraphPaint.Summary(history.Latest, true));
        }

        private static void Save(Control control, string name)
        {
            Form window = control as Form;
            if (window != null)
            {
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(100, 100);
                window.Show();
                Application.DoEvents();
            }
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, name);
            using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                if (window == null) bitmap.Save(path, ImageFormat.Png);
                else
                {
                    // DrawToBitmap paints a legacy nonclient frame. Export only
                    // the actual app content, including its native child controls.
                    Point client = window.PointToScreen(Point.Empty);
                    client.Offset(-window.Left, -window.Top);
                    using (Bitmap content = bitmap.Clone(new Rectangle(client, window.ClientSize), bitmap.PixelFormat)) content.Save(path, ImageFormat.Png);
                }
            }
            if (window != null) window.Hide();
            TestContext.AddTestAttachment(path);
        }
    }
}
