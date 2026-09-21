using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture, Category("UI"), Apartment(ApartmentState.STA), NonParallelizable]
    public class WindowOwnershipTests
    {
        private sealed class TestWindow : Form
        {
            internal TestWindow()
            {
                ShowInTaskbar = false; TopMost = true;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(-10000, -10000, 100, 40);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        [Test]
        public void RaisingAnotherThreadsOwnerCannotCoverTheOverlayAndClosingItReleasesOwnership()
        {
            using (ManualResetEvent ready = new ManualResetEvent(false))
            {
                TestWindow owner = null;
                IntPtr ownerHandle = IntPtr.Zero;
                Thread ownerThread = new Thread(delegate()
                {
                    using (TestWindow window = new TestWindow())
                    {
                        owner = window;
                        window.Shown += delegate { ownerHandle = window.Handle; ready.Set(); };
                        Application.Run(window);
                    }
                });
                ownerThread.SetApartmentState(ApartmentState.STA);
                ownerThread.IsBackground = true;
                ownerThread.Start();
                try
                {
                    Assert.That(ready.WaitOne(3000), Is.True);
                    using (TestWindow overlay = new TestWindow())
                    {
                        overlay.Show();
                        IntPtr overlayHandle = overlay.Handle;
                        Assert.That(Native.SetWindowOwner(overlayHandle, ownerHandle), Is.True);
                        Rectangle original = overlay.Bounds;
                        for (int i = 0; i < 20; i++)
                        {
                            Assert.That(Native.SetWindowPos(ownerHandle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10), Is.True);
                            for (IntPtr above = Native.GetWindow(overlayHandle, 3); above != IntPtr.Zero; above = Native.GetWindow(above, 3))
                                Assert.That(above, Is.Not.EqualTo(ownerHandle), "The owner covered the overlay before any timer or event handler could run.");
                            Assert.That(overlay.Bounds, Is.EqualTo(original));
                        }
                        owner.BeginInvoke((Action)owner.Close);
                        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                        while (ownerThread.IsAlive && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
                        Assert.That(ownerThread.IsAlive, Is.False);
                        Assert.That(Native.IsWindow(overlayHandle), Is.True, "An Explorer restart must not destroy the overlay's window on another thread.");
                        Assert.That(Native.GetWindow(overlayHandle, 4), Is.EqualTo(IntPtr.Zero));
                        using (TestWindow replacement = new TestWindow())
                        {
                            replacement.Show();
                            Assert.That(Native.SetWindowOwner(overlayHandle, replacement.Handle), Is.True);
                            Native.SetWindowOwner(overlayHandle, IntPtr.Zero);
                        }
                    }
                }
                finally
                {
                    if (ownerThread.IsAlive && owner != null && owner.IsHandleCreated) owner.BeginInvoke((Action)owner.Close);
                }
            }
        }
    }
}
