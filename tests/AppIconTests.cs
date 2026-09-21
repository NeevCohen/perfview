using System;
using System.Drawing;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture]
    public class AppIconTests
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int index, IntPtr large, IntPtr small, uint count);

        [Test]
        public void ExecutableContainsAnIconForWindowsShortcuts()
        {
            string executable = typeof(GraphPaint).Assembly.Location;
            Assert.That(ExtractIconEx(executable, -1, IntPtr.Zero, IntPtr.Zero, 0), Is.EqualTo(1u),
                "Windows needs a native icon resource in the executable, even when the tray draws an icon.");
        }

        [Test]
        public void TrayIconMatchesTheExecutableIcon()
        {
            using (Icon tray = GraphPaint.CreateIcon())
            using (Icon executable = Icon.ExtractAssociatedIcon(typeof(GraphPaint).Assembly.Location))
            using (Bitmap trayBitmap = tray.ToBitmap())
            using (Bitmap executableBitmap = executable.ToBitmap())
            {
                Assert.That(trayBitmap.Size, Is.EqualTo(executableBitmap.Size));
                for (int y = 0; y < trayBitmap.Height; y++)
                    for (int x = 0; x < trayBitmap.Width; x++)
                        Assert.That(trayBitmap.GetPixel(x, y), Is.EqualTo(executableBitmap.GetPixel(x, y)),
                            $"Tray and shell artwork differ at {x}, {y}.");
            }
        }
    }
}
