using System;
using System.Drawing;
using System.Threading;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture]
    public class TaskbarLayoutTests
    {
        private static readonly Rectangle Bar = new Rectangle(0, 1032, 1920, 48);
        private static readonly IntPtr Taskbar = new IntPtr(1);
        private static readonly DateTime Start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static TaskbarLayout Layout(DateTime time, int trayLeft = 1500)
        {
            TaskbarLayout layout = new TaskbarLayout { Handle = Taskbar, Bounds = Bar, CapturedAt = time, Reliable = true };
            layout.Occupied.Add(new Rectangle(0, Bar.Top, 500, Bar.Height));
            layout.Occupied.Add(new Rectangle(trayLeft, Bar.Top, Bar.Width - trayLeft, Bar.Height));
            return layout;
        }

        private static Rectangle Position(TaskbarLayout layout, Settings settings = null)
        {
            Assert.That(layout, Is.Not.Null, "Graphs must remain visible.");
            Rectangle bounds;
            Assert.That(Placement.TryFreeOverlay(layout.Bounds, layout.Occupied, 5, settings ?? new Settings { AlignRight = true, Offset = 0 }, 1, out bounds), Is.True);
            return bounds;
        }

        [Test]
        public void HiddenIconsPopupKeepsRightAlignedGraphsFixedThroughOpenAndClose()
        {
            TaskbarLayoutStability stability = new TaskbarLayoutStability();
            TaskbarLayout initial = Layout(Start);
            Rectangle expected = Position(stability.Select(initial, Taskbar, Bar, false, Start));
            TaskbarLayout expanded = Layout(Start.AddMilliseconds(100), 1470);
            Assert.That(Position(expanded), Is.Not.EqualTo(expected), "This reproduces the tray expansion that previously moved the graphs.");
            Assert.That(Position(stability.Select(expanded, Taskbar, Bar, true, expanded.CapturedAt)), Is.EqualTo(expected));
            Assert.That(Position(stability.Select(null, Taskbar, Bar, true, Start.AddSeconds(20))), Is.EqualTo(expected), "A popup can stay open longer than a normal layout's freshness limit.");

            DateTime closing = Start.AddSeconds(21);
            Assert.That(Position(stability.Select(Layout(closing, 1470), Taskbar, Bar, false, closing)), Is.EqualTo(expected));
            Assert.That(Position(stability.Select(Layout(closing.AddMilliseconds(200), 1485), Taskbar, Bar, false, closing.AddMilliseconds(200))), Is.EqualTo(expected));
            Assert.That(Position(stability.Select(Layout(closing.AddMilliseconds(400)), Taskbar, Bar, false, closing.AddMilliseconds(400))), Is.EqualTo(expected));
        }

        [Test]
        public void GenuineTaskbarChangesStillMoveGraphsOutOfOccupiedSpace()
        {
            TaskbarLayoutStability stability = new TaskbarLayoutStability();
            Rectangle before = Position(stability.Select(Layout(Start), Taskbar, Bar, false, Start));
            TaskbarLayout changed = Layout(Start.AddMilliseconds(400), 1400);
            Rectangle after = Position(stability.Select(changed, Taskbar, Bar, false, changed.CapturedAt));
            Assert.That(after.Right, Is.LessThan(before.Right));
            foreach (Rectangle occupied in changed.Occupied) Assert.That(after.IntersectsWith(occupied), Is.False);
        }

        [Test]
        public void NewIconsAddedDuringPopupAreHandledOnceItCloses()
        {
            TaskbarLayoutStability stability = new TaskbarLayoutStability();
            Rectangle before = Position(stability.Select(Layout(Start), Taskbar, Bar, false, Start));
            stability.Select(Layout(Start, 1470), Taskbar, Bar, true, Start);
            DateTime closing = Start.AddSeconds(2);
            stability.Select(Layout(closing, 1400), Taskbar, Bar, false, closing);
            TaskbarLayout changed = Layout(closing.AddMilliseconds(400), 1400);
            Assert.That(Position(stability.Select(changed, Taskbar, Bar, false, changed.CapturedAt)).Right, Is.LessThan(before.Right));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ResizedOrRecreatedTaskbarDiscardsFrozenPosition(bool resized)
        {
            TaskbarLayoutStability stability = new TaskbarLayoutStability();
            TaskbarLayout initial = Layout(Start);
            stability.Select(initial, Taskbar, Bar, false, Start);
            Rectangle bar = resized ? new Rectangle(0, 1032, 2560, 48) : Bar;
            IntPtr handle = resized ? Taskbar : new IntPtr(2);
            Assert.That(stability.Select(initial, handle, bar, true, Start), Is.Null);
            TaskbarLayout current = Layout(Start);
            current.Handle = handle; current.Bounds = bar;
            Assert.That(stability.Select(current, handle, bar, true, Start), Is.SameAs(current));
        }

        [Test]
        public void SlowLayoutReadsAfterClosingPopupKeepGraphsVisibleUntilASettledLayoutArrives()
        {
            TaskbarLayoutStability stability = new TaskbarLayoutStability();
            TaskbarLayout initial = Layout(Start);
            stability.Select(initial, Taskbar, Bar, false, Start);
            stability.Select(null, Taskbar, Bar, true, Start);
            DateTime closing = Start.AddSeconds(2);
            Assert.That(stability.Select(null, Taskbar, Bar, false, closing), Is.Not.Null);
            Assert.That(stability.Select(Layout(closing.AddMilliseconds(200), 1470), Taskbar, Bar, false, closing.AddMilliseconds(1600)), Is.SameAs(initial));
            Assert.That(stability.Select(null, Taskbar, Bar, false, closing.AddSeconds(30)), Is.SameAs(initial));
            TaskbarLayout recovered = Layout(closing.AddSeconds(31));
            Assert.That(stability.Select(recovered, Taskbar, Bar, false, recovered.CapturedAt), Is.SameAs(recovered));
        }

        [Test]
        public void MissingOrStaleLayoutRetainsAnExistingPositionButCannotInventOne()
        {
            TaskbarLayoutStability stability = new TaskbarLayoutStability();
            Assert.That(stability.Select(null, Taskbar, Bar, true, Start), Is.Null);
            Assert.That(stability.Select(null, Taskbar, Bar, false, Start), Is.Null);
            TaskbarLayout initial = Layout(Start.AddMilliseconds(400));
            Assert.That(stability.Select(initial, Taskbar, Bar, false, initial.CapturedAt), Is.SameAs(initial));
            Assert.That(stability.Select(initial, Taskbar, Bar, false, Start.AddSeconds(2)), Is.SameAs(initial));
            Assert.That(stability.Select(null, Taskbar, Bar, true, Start.AddSeconds(3)), Is.SameAs(initial));
            TaskbarLayout unreliable = Layout(Start.AddSeconds(4));
            unreliable.Reliable = false;
            Assert.That(stability.Select(unreliable, Taskbar, Bar, false, unreliable.CapturedAt), Is.SameAs(initial));
            Assert.That(stability.Select(null, new IntPtr(2), Bar, false, Start.AddSeconds(5)), Is.Null);
        }

        [Test]
        public void ContinuousExplorerNotificationsCannotStarveLayoutPublication()
        {
            TaskbarLayout initial = Layout(Start);
            using (ManualResetEvent begin = new ManualResetEvent(false))
            {
                TaskbarLayoutMonitor monitor = null;
                try
                {
                    monitor = new TaskbarLayoutMonitor(delegate
                    {
                        begin.WaitOne(5000);
                        // Explorer reports another event during every read.
                        monitor.RequestRefresh();
                        return initial;
                    });
                    begin.Set();
                    Assert.That(SpinWait.SpinUntil(() => monitor.Snapshot == initial, 2000), Is.True, "Repeated refresh requests must not discard every completed layout.");
                }
                finally { if (monitor != null) monitor.Dispose(); }
            }
        }

        [Test]
        public void LayoutRefreshRetainsSnapshotWhileExplorerIsBeingRead()
        {
            TaskbarLayout initial = Layout(Start), updated = Layout(Start.AddSeconds(1));
            using (ManualResetEvent reading = new ManualResetEvent(false))
            using (ManualResetEvent finish = new ManualResetEvent(false))
            {
                int reads = 0;
                using (TaskbarLayoutMonitor monitor = new TaskbarLayoutMonitor(delegate
                {
                    if (Interlocked.Increment(ref reads) == 1) return initial;
                    reading.Set();
                    finish.WaitOne(5000);
                    return updated;
                }))
                {
                    try
                    {
                        Assert.That(SpinWait.SpinUntil(() => monitor.Snapshot == initial, 3000), Is.True);
                        monitor.RequestRefresh();
                        Assert.That(reading.WaitOne(3000), Is.True);
                        Assert.That(monitor.Snapshot, Is.SameAs(initial), "A layout event alone must not hide the graphs.");
                        finish.Set();
                        Assert.That(SpinWait.SpinUntil(() => monitor.Snapshot == updated, 3000), Is.True);
                        monitor.RequestRefresh(true);
                        Assert.That(SpinWait.SpinUntil(() => monitor.Snapshot == updated, 3000), Is.True);
                    }
                    finally { finish.Set(); }
                }
            }
        }
    }
}
