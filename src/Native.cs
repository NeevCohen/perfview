using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace Perfview
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect
        {
            public int Left, Top, Right, Bottom;
            public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct FileTime
        {
            public uint Low, High;
            public ulong Value { get { return ((ulong)High << 32) | Low; } }
        }
        [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus
        {
            public uint Length, Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
        [StructLayout(LayoutKind.Explicit)] internal struct CounterValue
        {
            [FieldOffset(0)] public uint Status;
            [FieldOffset(8)] public double Value;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct CounterItem
        {
            public IntPtr Name;
            public CounterValue Value;
        }
        [DllImport("kernel32.dll")] internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        internal static bool CanReadTemperatureDriver()
        {
            // Test the same device access used by LibreHardwareMonitor; an installed
            // driver can still be stopped, blocked, or inaccessible to this user.
            using (SafeFileHandle handle = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) return !handle.IsInvalid;
        }
        [DllImport("kernel32.dll")] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhOpenQuery(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] internal static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] internal static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
        [DllImport("pdh.dll")] internal static extern uint PdhCloseQuery(IntPtr query);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        internal static bool SetWindowOwner(IntPtr window, IntPtr owner)
        {
            if (GetWindow(window, 4) != owner) SetWindowLongPtr(window, -8, owner); // GW_OWNER / GWLP_HWNDPARENT
            return GetWindow(window, 4) == owner;
        }
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetCapture();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        internal delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        internal delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

        internal static float Scale(IntPtr window)
        {
            try { uint dpi = GetDpiForWindow(window); return dpi == 0 ? 1 : dpi / 96f; }
            catch (EntryPointNotFoundException) { return 1; }
        }

        internal static bool TryTaskbar(out Rectangle rect, out IntPtr handle)
        {
            handle = FindWindow("Shell_TrayWnd", null);
            Rect native;
            rect = Rectangle.Empty;
            if (handle == IntPtr.Zero || !IsWindowVisible(handle) || !GetWindowRect(handle, out native)) return false;
            rect = native.Rectangle;
            Rectangle visible = Rectangle.Intersect(rect, Screen.FromHandle(handle).Bounds);
            return visible.Width > 8 && visible.Height > 8;
        }

        internal static bool IsTrayOverflowOpen(IntPtr taskbar)
        {
            uint shellProcess;
            GetWindowThreadProcessId(taskbar, out shellProcess);
            foreach (string className in new[] { "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow" })
            {
                IntPtr popup = FindWindow(className, null);
                if (popup == IntPtr.Zero || !IsWindowVisible(popup)) continue;
                uint popupProcess;
                GetWindowThreadProcessId(popup, out popupProcess);
                Rect bounds;
                if (popupProcess != shellProcess || !GetWindowRect(popup, out bounds) || bounds.Rectangle.Width <= 0 || bounds.Rectangle.Height <= 0) continue;
                // Explorer can leave a dismissed XAML window visible but cloaked.
                int cloaked;
                if (DwmGetWindowAttribute(popup, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) continue; // DWMWA_CLOAKED
                return true;
            }
            return false;
        }

        internal static bool IsFullscreen(IntPtr taskbar, int ownProcess)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == taskbar || foreground == FindWindow("Progman", null) || foreground == FindWindow("WorkerW", null)) return false;
            uint process;
            GetWindowThreadProcessId(foreground, out process);
            if (process == ownProcess) return false;
            Rect rect;
            if (!GetWindowRect(foreground, out rect)) return false;
            Rectangle screen = Screen.FromHandle(taskbar).Bounds;
            return rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
        }
    }

    internal static class Placement
    {
        // Coordinates are physical pixels; settings are device-independent pixels.
        internal static Rectangle Overlay(Rectangle bar, int count, Settings settings, float scale)
        {
            int inset = Math.Max(2, (int)(4 * scale));
            bool horizontal = bar.Width >= bar.Height;
            int length = horizontal ? bar.Width : bar.Height;
            int cell = (int)((horizontal ? settings.CellWidth : 48) * scale);
            int extent = Math.Min(Math.Max(1, length - 2 * inset), count * cell);
            int offset = Math.Min(Math.Max(inset, (int)(settings.Offset * scale)), Math.Max(inset, length - extent - inset));
            int start = settings.AlignRight ? length - offset - extent : offset;
            return horizontal
                ? new Rectangle(bar.Left + start, bar.Top + inset, extent, Math.Max(1, bar.Height - 2 * inset))
                : new Rectangle(bar.Left + inset, bar.Top + start, Math.Max(1, bar.Width - 2 * inset), extent);
        }

        internal static bool TryFreeOverlay(Rectangle bar, IList<Rectangle> occupied, int count, Settings settings, float scale, out Rectangle result)
        {
            result = Rectangle.Empty;
            if (occupied == null || count <= 0 || scale <= 0 || bar.Width <= 0 || bar.Height <= 0) return false;
            bool horizontal = bar.Width >= bar.Height;
            int origin = horizontal ? bar.Left : bar.Top;
            int length = horizontal ? bar.Width : bar.Height;
            int inset = Math.Max(2, (int)Math.Ceiling(4 * scale));
            int clearance = Math.Max(2, (int)Math.Ceiling(6 * scale));
            List<Point> blocked = new List<Point>();
            foreach (Rectangle item in occupied)
            {
                Rectangle intersection = Rectangle.Intersect(bar, item);
                if (intersection.Width <= 0 || intersection.Height <= 0) continue;
                int start = (horizontal ? intersection.Left : intersection.Top) - origin - clearance;
                int end = (horizontal ? intersection.Right : intersection.Bottom) - origin + clearance;
                blocked.Add(new Point(Math.Max(inset, start), Math.Min(length - inset, end)));
            }
            blocked.Sort((left, right) => left.X.CompareTo(right.X));
            List<Point> gaps = new List<Point>();
            int cursor = inset;
            foreach (Point block in blocked)
            {
                if (block.X > cursor) gaps.Add(new Point(cursor, block.X));
                cursor = Math.Max(cursor, block.Y);
            }
            if (cursor < length - inset) gaps.Add(new Point(cursor, length - inset));
            int preferred = (int)Math.Ceiling(count * (horizontal ? settings.CellWidth : 48) * scale);
            int minimum = (int)Math.Ceiling(count * (horizontal ? 68 : 40) * scale);
            int bestExtent = 0, bestStart = 0;
            double bestDistance = double.MaxValue;
            foreach (Point gap in gaps)
            {
                int extent = Math.Min(preferred, gap.Y - gap.X);
                if (extent < minimum) continue;
                int desired = settings.AlignRight ? length - (int)(settings.Offset * scale) - extent : (int)(settings.Offset * scale);
                int start = Math.Max(gap.X, Math.Min(desired, gap.Y - extent));
                double distance = Math.Abs((double)start - desired);
                // Honor the preferred edge/position even when a farther gap could
                // show wider graphs; each candidate already meets the readable size.
                if (distance < bestDistance || (distance == bestDistance && extent > bestExtent))
                { bestStart = start; bestExtent = extent; bestDistance = distance; }
            }
            if (bestExtent == 0) return false;
            result = horizontal
                ? new Rectangle(bar.Left + bestStart, bar.Top + inset, bestExtent, Math.Max(1, bar.Height - 2 * inset))
                : new Rectangle(bar.Left + inset, bar.Top + bestStart, Math.Max(1, bar.Width - 2 * inset), bestExtent);
            return true;
        }

        internal static Point Popup(Rectangle anchor, Size size, Rectangle area)
        {
            int x = Math.Min(anchor.Left, area.Right - size.Width);
            int y = anchor.Top - size.Height - 10;
            if (y < area.Top) y = anchor.Bottom + 10;
            return new Point(Math.Max(area.Left, x), Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height)));
        }
    }
}


