using System;
using System.Collections.Generic;
using System.Drawing;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture]
    public class PlacementTests
    {
        public static IEnumerable<TestCaseData> OverlayCases()
        {
            Rectangle[] bars = { new Rectangle(0, 1032, 1920, 48), new Rectangle(-1920, 0, 1920, 60), new Rectangle(0, 0, 60, 1080), new Rectangle(1860, 0, 60, 1080), new Rectangle(0, 0, 280, 48) };
            foreach (Rectangle bar in bars)
                foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                    foreach (bool right in new[] { false, true })
                        yield return new TestCaseData(bar, scale, right);
        }

        [TestCaseSource(nameof(OverlayCases))]
        public void OverlayStaysInsideTaskbar(Rectangle bar, float scale, bool right)
        {
            Rectangle bounds = Placement.Overlay(bar, 5, new Settings { Offset = 9999, AlignRight = right }, scale);
            Assert.That(bar.Contains(bounds), Is.True, "Overlay escaped taskbar: " + bounds);
            Assert.That(bounds.Width, Is.GreaterThan(0));
            Assert.That(bounds.Height, Is.GreaterThan(0));
        }

        [Test]
        public void WiderGpuPopupRemainsInsideWorkingArea()
        {
            Size size = new Size(954, 540);
            Rectangle workingArea = new Rectangle(0, 48, 1920, 1032);
            Point popup = Placement.Popup(new Rectangle(1800, 0, 120, 48), size, workingArea);
            Assert.That(workingArea.Contains(new Rectangle(popup, size)), Is.True);
        }

        [TestCase(false, 506)]
        [TestCase(true, 1054)]
        public void SnapsToNearestGapWithClearance(bool alignRight, int expectedLeft)
        {
            Rectangle bar = new Rectangle(0, 1032, 1920, 48), result;
            Rectangle[] icons = { new Rectangle(0, 1032, 500, 48), new Rectangle(1500, 1032, 420, 48) };
            Assert.That(Placement.TryFreeOverlay(bar, icons, 5, new Settings { Offset = 100, AlignRight = alignRight }, 1, out result), Is.True);
            Assert.That(result.X, Is.EqualTo(expectedLeft));
            Assert.That(result.Width, Is.EqualTo(440));
        }

        [Test]
        public void NewlyOccupiedGapCausesRelocation()
        {
            Rectangle bar = new Rectangle(0, 1032, 1920, 48), result;
            Rectangle[] icons = { new Rectangle(0, 1032, 500, 48), new Rectangle(1500, 1032, 420, 48), new Rectangle(506, 1032, 500, 48) };
            Assert.That(Placement.TryFreeOverlay(bar, icons, 5, new Settings { Offset = 506 }, 1, out result), Is.True);
            Assert.That(result.X, Is.EqualTo(1012));
        }

        [Test]
        public void CompactsGraphsToFitReadableGap()
        {
            Rectangle bar = new Rectangle(0, 1032, 1920, 48), result;
            Rectangle[] icons = { new Rectangle(0, 1032, 600, 48), new Rectangle(1000, 1032, 920, 48) };
            Assert.That(Placement.TryFreeOverlay(bar, icons, 5, new Settings(), 1, out result), Is.True);
            Assert.That(result.Width, Is.EqualTo(388));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void AnchorPrefersNearerReadableGapOverLargerOppositeGap(bool right, bool vertical)
        {
            // The chosen edge has room for readable, slightly narrower graphs.
            // A wider gap across the middle buttons must not override the anchor.
            int blockedStart = right ? 1300 : 400;
            Rectangle bar = vertical ? new Rectangle(-60, -100, 60, 2000) : new Rectangle(-2000, 1000, 2000, 60);
            Rectangle blocked = vertical ? new Rectangle(bar.Left, bar.Top + blockedStart, 60, 300) : new Rectangle(bar.Left + blockedStart, bar.Top, 300, 60);
            Rectangle result;
            Settings settings = new Settings { AlignRight = right, Offset = 0 };
            Assert.That(Placement.TryFreeOverlay(bar, new[] { blocked }, vertical ? 9 : 5, settings, 1, out result), Is.True);
            int edge = vertical ? (right ? result.Bottom : result.Top) : (right ? result.Right : result.Left);
            Assert.That(edge, Is.EqualTo(vertical ? (right ? bar.Bottom - 4 : bar.Top + 4) : (right ? bar.Right - 4 : bar.Left + 4)));
            Assert.That(result.IntersectsWith(blocked), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FullOrUnknownLayoutHidesOverlay(bool known)
        {
            Rectangle bar = new Rectangle(0, 1032, 1920, 48), result;
            Assert.That(Placement.TryFreeOverlay(bar, known ? new[] { bar } : null, 5, new Settings(), 1, out result), Is.False);
            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public void CrowdedLayoutsNeverOverlapProtectedControls()
        {
            Random random = new Random(42);
            for (int test = 0; test < 1000; test++)
            {
                bool horizontal = test % 2 == 0;
                float scale = new[] { 1f, 1.25f, 1.5f, 2f }[test % 4];
                Rectangle bar = horizontal ? new Rectangle(-1920, 1000, 1920, 60) : new Rectangle(-60, -500, 60, 1080);
                Rectangle[] blocked = new Rectangle[6];
                for (int i = 0; i < blocked.Length; i++)
                    blocked[i] = horizontal ? new Rectangle(bar.Left + random.Next(1920), 1000, random.Next(30, 400), 60) : new Rectangle(-60, -500 + random.Next(1080), 60, random.Next(30, 160));
                Rectangle result;
                if (!Placement.TryFreeOverlay(bar, blocked, 1 + test % 5, new Settings { Offset = random.Next(2500), AlignRight = test % 3 == 0 }, scale, out result)) continue;
                Assert.That(bar.Contains(result), Is.True, "Layout " + test);
                foreach (Rectangle block in blocked)
                    Assert.That(result.IntersectsWith(block), Is.False, "Layout " + test + ": " + result + " intersects " + block);
            }
        }
    }
}
