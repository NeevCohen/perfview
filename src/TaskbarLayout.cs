using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace Perfview
{
    internal sealed class TaskbarLayout
    {
        internal IntPtr Handle;
        internal Rectangle Bounds;
        internal DateTime CapturedAt;
        internal bool Reliable;
        internal readonly List<Rectangle> Occupied = new List<Rectangle>();
        internal readonly List<string> Diagnostics = new List<string>();

        internal static TaskbarLayout Capture()
        {
            // UI Automation always returns physical screen pixels. Thread-pool
            // threads can otherwise inherit a different DPI context from WinForms.
            IntPtr previous = IntPtr.Zero;
            try
            {
                try { previous = Native.SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { }
                return CapturePhysical();
            }
            finally { if (previous != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(previous); }
        }

        private static TaskbarLayout CapturePhysical()
        {
            TaskbarLayout layout = new TaskbarLayout();
            if (!Native.TryTaskbar(out layout.Bounds, out layout.Handle)) return layout;
            try
            {
                bool hasTray = false;
                Native.EnumWindowCallback enumerate = delegate(IntPtr child, IntPtr parameter)
                {
                    if (!Native.IsWindowVisible(child)) return true;
                    StringBuilder name = new StringBuilder(256);
                    Native.GetClassName(child, name, name.Capacity);
                    string kind = name.ToString();
                    bool tray = kind == "TrayNotifyWnd";
                    bool protectedWindow = tray || kind == "TrayClockWClass" || kind == "TrayShowDesktopButtonWClass" || kind == "Start";
                    Native.Rect bounds;
                    if (protectedWindow && Native.GetWindowRect(child, out bounds))
                    {
                        Rectangle rect = Rectangle.Intersect(layout.Bounds, bounds.Rectangle);
                        if (rect.Width > 0 && rect.Height > 0)
                        {
                            layout.Occupied.Add(rect);
                            layout.Diagnostics.Add(kind + " " + rect);
                            if (tray) hasTray = true;
                        }
                    }
                    return true;
                };
                Native.EnumChildWindows(layout.Handle, enumerate, IntPtr.Zero);

                // Read only the taskbar subtree. A cache request batches cross-process
                // property reads; no UI Automation work runs on the painting thread.
                AutomationElement root = AutomationElement.FromHandle(layout.Handle);
                layout.Diagnostics.Add("UIA taskbar: " + root.Current.BoundingRectangle);
                int actions = 0;
                CacheRequest cache = new CacheRequest();
                {
                    cache.TreeScope = TreeScope.Element;
                    cache.Add(AutomationElement.BoundingRectangleProperty);
                    cache.Add(AutomationElement.ControlTypeProperty);
                    cache.Add(AutomationElement.IsOffscreenProperty);
                    cache.Add(AutomationElement.IsKeyboardFocusableProperty);
                    cache.Add(AutomationElement.AutomationIdProperty);
                    cache.Add(AutomationElement.ClassNameProperty);
                    using (cache.Activate())
                    {
                        AutomationElementCollection elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                        foreach (AutomationElement element in elements)
                        {
                            AutomationElement.AutomationElementInformation info = element.Cached;
                            if (info.IsOffscreen) continue;
                            ControlType type = info.ControlType;
                            bool interactive = type == ControlType.Button || type == ControlType.SplitButton || type == ControlType.ListItem || type == ControlType.TabItem || type == ControlType.MenuItem || type == ControlType.CheckBox || type == ControlType.RadioButton || type == ControlType.Hyperlink || type == ControlType.Edit || type == ControlType.ComboBox || type == ControlType.Slider;
                            if (!interactive && !(info.IsKeyboardFocusable && type == ControlType.Custom)) continue;
                            System.Windows.Rect native = info.BoundingRectangle;
                            if (native.IsEmpty || double.IsInfinity(native.Width) || double.IsInfinity(native.Height)) continue;
                            Rectangle rect = Rectangle.FromLTRB((int)Math.Floor(native.Left), (int)Math.Floor(native.Top), (int)Math.Ceiling(native.Right), (int)Math.Ceiling(native.Bottom));
                            rect = Rectangle.Intersect(layout.Bounds, rect);
                            if (rect.Width <= 0 || rect.Height <= 0) continue;
                            layout.Occupied.Add(rect);
                            layout.Diagnostics.Add(type.ProgrammaticName + " / " + info.AutomationId + " / " + info.ClassName + " " + rect);
                            actions++;
                            if (info.AutomationId.StartsWith("SystemTray.", StringComparison.Ordinal)) hasTray = true;
                        }
                    }
                }
                // With incomplete discovery, do not guess which pixels are free.
                Native.Rect current;
                layout.Reliable = actions > 0 && hasTray && Native.GetWindowRect(layout.Handle, out current) && current.Rectangle == layout.Bounds;
                layout.CapturedAt = DateTime.UtcNow;
            }
            catch (Exception error)
            {
                layout.Diagnostics.Add(error.GetType().Name + ": " + error.Message);
                System.Diagnostics.Trace.TraceWarning("Taskbar layout unavailable: " + error.Message);
            }
            return layout;
        }
    }

    internal sealed class TaskbarLayoutMonitor : IDisposable
    {
        private readonly System.Threading.Timer timer;
        private int reading, generation;
        private readonly object snapshotGate = new object();
        private volatile bool disposed;
        private TaskbarLayout snapshot;

        internal TaskbarLayoutMonitor() { timer = new System.Threading.Timer(Read, null, 0, 400); }
        internal TaskbarLayout Snapshot { get { return Interlocked.CompareExchange(ref snapshot, null, null); } }
        internal void Invalidate()
        {
            lock (snapshotGate)
            {
                generation++;
                Interlocked.Exchange(ref snapshot, null);
            }
            if (!disposed) ThreadPool.QueueUserWorkItem(Read);
        }
        private void Read(object state)
        {
            if (disposed || Interlocked.CompareExchange(ref reading, 1, 0) != 0) return;
            try
            {
                int version = generation;
                TaskbarLayout layout = TaskbarLayout.Capture();
                lock (snapshotGate) { if (!disposed && version == generation) Interlocked.Exchange(ref snapshot, layout); }
            }
            finally { Interlocked.Exchange(ref reading, 0); }
        }
        public void Dispose() { disposed = true; timer.Dispose(); Interlocked.Exchange(ref snapshot, null); }
    }
}
