using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture, Category("Desktop"), Apartment(ApartmentState.MTA), NonParallelizable]
    public class TaskbarPopupTraceTests
    {
        [Test, Explicit("Read-only trace while the user opens and closes the hidden-icons popup.")]
        public void TraceRunningOverlayDuringHiddenIconsInteraction()
        { TraceInteraction(false); }

        [Test, Explicit("Read-only regression check while the user opens and closes the hidden-icons popup.")]
        public void RunningOverlayRemainsVisibleAndFixedDuringHiddenIconsInteraction()
        { TraceInteraction(true); }

        private static void TraceInteraction(bool verify)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "popup-trace.txt");
            IntPtr previousDpi = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
            bool opened = false;
            int hiddenFrames = 0, coveredFrames = 0, movedFrames = 0;
            try
            {
                using (StreamWriter output = new StreamWriter(path) { AutoFlush = true })
                using (TaskbarLayoutMonitor monitor = new TaskbarLayoutMonitor())
                {
                    Stopwatch elapsed = Stopwatch.StartNew();
                    string previous = null;
                    Rectangle? initialBounds = null;
                    double closedAt = double.PositiveInfinity;
                    while (elapsed.Elapsed.TotalSeconds < 180 && elapsed.Elapsed.TotalSeconds < closedAt + 4)
                    {
                        Rectangle bar;
                        IntPtr taskbar;
                        bool hasBar = Native.TryTaskbar(out bar, out taskbar);
                        IntPtr overlay = Native.FindWindow(null, "Perfview Taskbar");
                        IntPtr foreground = Native.GetForegroundWindow();
                        StringBuilder kind = new StringBuilder(256);
                        Native.GetClassName(foreground, kind, kind.Capacity);
                        Native.Rect bounds;
                        Native.GetWindowRect(overlay, out bounds);
                        bool coveredByTaskbar = false;
                        for (IntPtr above = Native.GetWindow(overlay, 3); above != IntPtr.Zero; above = Native.GetWindow(above, 3))
                            if (above == taskbar) { coveredByTaskbar = true; break; }
                        bool overflow = Native.IsTrayOverflowOpen(taskbar);
                        if (overflow) { opened = true; closedAt = double.PositiveInfinity; }
                        else if (opened && double.IsPositiveInfinity(closedAt)) closedAt = elapsed.Elapsed.TotalSeconds;
                        bool visible = Native.IsWindowVisible(overlay);
                        if (!initialBounds.HasValue && !opened && visible && !coveredByTaskbar && hasBar && bar.Contains(bounds.Rectangle)) initialBounds = bounds.Rectangle;
                        if (opened)
                        {
                            if (!visible) hiddenFrames++;
                            if (coveredByTaskbar) coveredFrames++;
                            if (initialBounds.HasValue && bounds.Rectangle != initialBounds.Value) movedFrames++;
                        }
                        TaskbarLayout layout = monitor.Snapshot;
                        string state = "visible=" + visible + " coveredByTaskbar=" + coveredByTaskbar + " ownedByTaskbar=" + (Native.GetWindow(overlay, 4) == taskbar) + " overflow=" + overflow + " fullscreen=" + Native.IsFullscreen(taskbar, Process.GetCurrentProcess().Id) + " foreground=" + kind + " bar=" + hasBar + ":" + bar + " overlay=" + bounds.Rectangle + " layout=" + (layout == null ? "none" : layout.Reliable + ":" + Math.Round((DateTime.UtcNow - layout.CapturedAt).TotalSeconds, 0) + "s");
                        if (state != previous) { output.WriteLine(elapsed.Elapsed.TotalSeconds.ToString("F2") + " " + state); previous = state; }
                        Thread.Sleep(50);
                    }
                    output.WriteLine("Observed popup open: " + opened);
                    output.WriteLine("Hidden frames: " + hiddenFrames + "; covered frames: " + coveredFrames + "; moved frames: " + movedFrames);
                }
            }
            finally { Native.SetThreadDpiAwarenessContext(previousDpi); }
            TestContext.AddTestAttachment(path);
            TestContext.WriteLine(File.ReadAllText(path));
            if (verify)
            {
                Assert.That(opened, Is.True, "The hidden-icons popup was not opened during the check.");
                Assert.That(hiddenFrames, Is.Zero, "Perfview hid the graphs during the popup interaction.");
                Assert.That(coveredFrames, Is.Zero, "Explorer covered the graphs even though the window was marked visible.");
                Assert.That(movedFrames, Is.Zero, "The graphs changed position during the popup interaction.");
            }
        }
    }
}
