using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UIA = Interop.UIAutomationClient;

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

                int actions = 0;
                // The Framework client creates rebar proxies that remain rooted in
                // native UIA after each scan. Use the native client, cache only the
                // properties we need, and release all scan-owned COM references.
                UIA.IUIAutomation automation = null;
                UIA.IUIAutomationElement root = null;
                UIA.IUIAutomationCacheRequest cache = null;
                UIA.IUIAutomationCondition ownProcess = null, otherProcess = null;
                UIA.IUIAutomationElementArray elements = null;
                try
                {
                    automation = new UIA.CUIAutomation();
                    root = automation.ElementFromHandle(layout.Handle);
                    cache = automation.CreateCacheRequest();
                    cache.TreeScope = UIA.TreeScope.TreeScope_Element;
                    cache.AutomationElementMode = UIA.AutomationElementMode.AutomationElementMode_None;
                    cache.AddProperty(UIA.UIA_PropertyIds.UIA_BoundingRectanglePropertyId);
                    cache.AddProperty(UIA.UIA_PropertyIds.UIA_ControlTypePropertyId);
                    cache.AddProperty(UIA.UIA_PropertyIds.UIA_IsOffscreenPropertyId);
                    cache.AddProperty(UIA.UIA_PropertyIds.UIA_IsKeyboardFocusablePropertyId);
                    cache.AddProperty(UIA.UIA_PropertyIds.UIA_AutomationIdPropertyId);
                    cache.AddProperty(UIA.UIA_PropertyIds.UIA_ClassNamePropertyId);
                    // The overlay is owned by the taskbar; exclude our own graphs.
                    using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
                        ownProcess = automation.CreatePropertyCondition(UIA.UIA_PropertyIds.UIA_ProcessIdPropertyId, process.Id);
                    otherProcess = automation.CreateNotCondition(ownProcess);
                    elements = root.FindAllBuildCache(UIA.TreeScope.TreeScope_Descendants, otherProcess, cache);
                    for (int i = 0; i < elements.Length; i++)
                    {
                        UIA.IUIAutomationElement element = elements.GetElement(i);
                        try
                        {
                            if (element.CachedIsOffscreen != 0) continue;
                            int type = element.CachedControlType;
                            bool interactive = type == UIA.UIA_ControlTypeIds.UIA_ButtonControlTypeId || type == UIA.UIA_ControlTypeIds.UIA_SplitButtonControlTypeId ||
                                type == UIA.UIA_ControlTypeIds.UIA_ListItemControlTypeId || type == UIA.UIA_ControlTypeIds.UIA_TabItemControlTypeId ||
                                type == UIA.UIA_ControlTypeIds.UIA_MenuItemControlTypeId || type == UIA.UIA_ControlTypeIds.UIA_CheckBoxControlTypeId ||
                                type == UIA.UIA_ControlTypeIds.UIA_RadioButtonControlTypeId || type == UIA.UIA_ControlTypeIds.UIA_HyperlinkControlTypeId ||
                                type == UIA.UIA_ControlTypeIds.UIA_EditControlTypeId || type == UIA.UIA_ControlTypeIds.UIA_ComboBoxControlTypeId ||
                                type == UIA.UIA_ControlTypeIds.UIA_SliderControlTypeId;
                            if (!interactive && !(element.CachedIsKeyboardFocusable != 0 && type == UIA.UIA_ControlTypeIds.UIA_CustomControlTypeId)) continue;
                            UIA.tagRECT native = element.CachedBoundingRectangle;
                            Rectangle rect = Rectangle.FromLTRB(native.left, native.top, native.right, native.bottom);
                            rect = Rectangle.Intersect(layout.Bounds, rect);
                            if (rect.Width <= 0 || rect.Height <= 0) continue;
                            layout.Occupied.Add(rect);
                            string automationId = element.CachedAutomationId ?? "";
                            layout.Diagnostics.Add(type + " / " + automationId + " / " + element.CachedClassName + " " + rect);
                            actions++;
                            if (automationId.StartsWith("SystemTray.", StringComparison.Ordinal)) hasTray = true;
                        }
                        finally { Marshal.ReleaseComObject(element); }
                    }
                }
                finally
                {
                    if (elements != null) Marshal.ReleaseComObject(elements);
                    if (otherProcess != null) Marshal.ReleaseComObject(otherProcess);
                    if (ownProcess != null) Marshal.ReleaseComObject(ownProcess);
                    if (cache != null) Marshal.ReleaseComObject(cache);
                    if (root != null) Marshal.ReleaseComObject(root);
                    if (automation != null) Marshal.ReleaseComObject(automation);
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

    internal sealed class TaskbarLayoutStability
    {
        private TaskbarLayout stable;
        private bool overflowWasOpen;
        private DateTime settleAfter;

        internal TaskbarLayout Select(TaskbarLayout current, IntPtr taskbar, Rectangle bar, bool overflowOpen, DateTime now)
        {
            if (stable != null && (stable.Handle != taskbar || stable.Bounds != bar))
            {
                stable = null;
                overflowWasOpen = false;
                settleAfter = DateTime.MinValue;
            }
            if (overflowOpen)
            {
                overflowWasOpen = true;
                // The flyout can temporarily expand the tray's reported bounds.
                // Keep using the last confirmed gap throughout that interaction.
                if (stable != null) return stable;
            }
            else if (overflowWasOpen)
            {
                overflowWasOpen = false;
                settleAfter = now.AddMilliseconds(300);
            }
            bool fresh = current != null && current.Reliable && current.Handle == taskbar && current.Bounds == bar && (now - current.CapturedAt).TotalSeconds <= 1.5;
            // A slow or interrupted accessibility read is not evidence that the
            // existing position became unsafe. Only replace it with a verified
            // layout, or discard it when the taskbar's actual geometry changes.
            if (!fresh || current.CapturedAt < settleAfter) return stable;
            stable = current;
            settleAfter = DateTime.MinValue;
            return current;
        }
    }

    internal sealed class TaskbarLayoutMonitor : IDisposable
    {
        private readonly System.Threading.Timer timer;
        private readonly Func<TaskbarLayout> capture;
        private int reading, generation;
        private readonly object snapshotGate = new object();
        private volatile bool disposed;
        private TaskbarLayout snapshot;

        internal TaskbarLayoutMonitor(Func<TaskbarLayout> capture = null)
        {
            this.capture = capture ?? TaskbarLayout.Capture;
            timer = new System.Threading.Timer(Read, null, 0, 400);
        }
        internal TaskbarLayout Snapshot { get { return Interlocked.CompareExchange(ref snapshot, null, null); } }
        internal void RequestRefresh(bool discardSnapshot = false)
        {
            lock (snapshotGate)
            {
                // Coalesce ordinary Explorer notifications. Cancelling every
                // in-flight read can starve publication throughout an animation.
                if (discardSnapshot) { generation++; Interlocked.Exchange(ref snapshot, null); }
            }
            if (!disposed) ThreadPool.QueueUserWorkItem(Read);
        }
        private void Read(object state)
        {
            if (disposed || Interlocked.CompareExchange(ref reading, 1, 0) != 0) return;
            try
            {
                int version = generation;
                TaskbarLayout layout = capture();
                lock (snapshotGate) { if (!disposed && version == generation) Interlocked.Exchange(ref snapshot, layout); }
            }
            finally { Interlocked.Exchange(ref reading, 0); }
        }
        public void Dispose() { disposed = true; timer.Dispose(); Interlocked.Exchange(ref snapshot, null); }
    }
}
