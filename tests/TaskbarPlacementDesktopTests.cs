using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture, Category("Desktop"), Apartment(ApartmentState.STA), NonParallelizable]
    public class TaskbarPlacementDesktopTests
    {
        [Test, Explicit("Requires an interactive Windows desktop with Perfview closed and free taskbar space.")]
        public void OwnedGraphsStayFixedAcrossRepeatedInProcessLayoutScans()
        {
            Assert.That(Native.FindWindow(null, "Perfview Taskbar"), Is.EqualTo(IntPtr.Zero));
            IntPtr previousDpi = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "placement-trace.txt");
            int moves = 0;
            try
            {
                using (StreamWriter output = new StreamWriter(path) { AutoFlush = true })
                using (TaskbarWindow overlay = new TaskbarWindow(new History(), new Settings { AlignRight = true, Offset = 31 }, Palette.Create("Dark")))
                {
                    TaskbarLayoutMonitor monitor = (TaskbarLayoutMonitor)typeof(TaskbarWindow).GetField("layoutMonitor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(overlay);
                    overlay.Show();
                    Stopwatch elapsed = Stopwatch.StartNew();
                    TaskbarLayout previous = null;
                    Rectangle? initialBounds = null;
                    while (elapsed.Elapsed.TotalSeconds < 8)
                    {
                        Application.DoEvents();
                        TaskbarLayout snapshot = monitor.Snapshot;
                        if (snapshot != null && snapshot != previous)
                        {
                            output.WriteLine(elapsed.Elapsed.TotalSeconds.ToString("F2") + " visible=" + overlay.Visible + " bounds=" + overlay.Bounds + " reliable=" + snapshot.Reliable);
                            foreach (string diagnostic in snapshot.Diagnostics) output.WriteLine(diagnostic);
                            previous = snapshot;
                        }
                        if (elapsed.Elapsed.TotalSeconds > 2 && overlay.Visible && overlay.Opacity == 1)
                        {
                            if (!initialBounds.HasValue) initialBounds = overlay.Bounds;
                            if (overlay.Bounds != initialBounds.Value) moves++;
                        }
                        Thread.Sleep(20);
                    }
                    Assert.That(initialBounds.HasValue, Is.True, "Graphs never reached a visible position.");
                }
            }
            finally
            {
                Native.SetThreadDpiAwarenessContext(previousDpi);
                TestContext.AddTestAttachment(path);
                TestContext.WriteLine(File.ReadAllText(path));
            }
            Assert.That(moves, Is.Zero, "Graphs moved despite no change to Explorer's taskbar buttons.");
        }
    }
}
