using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NUnit.Framework;

namespace Perfview.Tests
{
    [TestFixture, NonParallelizable]
    public class DetailsPopupTests
    {
        [Test, Apartment(ApartmentState.STA), Category("Desktop")]
        [Explicit("Requires an interactive Windows desktop with Perfview closed and free taskbar space.")]
        public void ClickingEachTaskbarGraphOpensThePerformanceWindow()
        {
            Assert.That(Native.FindWindow(null, "Perfview Taskbar"), Is.EqualTo(IntPtr.Zero));
            IntPtr previousDpi = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
            System.Drawing.Point previousCursor = Cursor.Position;
            try
            {
                using (PerfviewContext context = new PerfviewContext())
                {
                    TaskbarWindow overlay = Application.OpenForms.OfType<TaskbarWindow>().Single();
                    WaitFor(() => overlay.Visible && overlay.Opacity == 1, "Graphs did not reach a safe taskbar position.");
                    PumpFor(1000);
                    for (int graph = 0; graph < overlay.Graphs.Settings.Metrics.Count; graph++)
                    {
                        int count = overlay.Graphs.Settings.Metrics.Count;
                        System.Drawing.Point point = overlay.Graphs.PointToScreen(new System.Drawing.Point(overlay.Graphs.Width * (2 * graph + 1) / (2 * count), overlay.Graphs.Height / 2));
                        Assert.That(WindowFromPoint(point), Is.EqualTo(overlay.Graphs.Handle), "The graph is not the mouse input target.");
                        SetCursorPos(point.X, point.Y);
                        mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
                        PumpFor(50);
                        mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
                        WaitFor(() => Application.OpenForms.OfType<DetailsWindow>().Any(window => window.Visible), "Clicking graph " + graph + " did not open Performance.");
                        DetailsWindow details = Application.OpenForms.OfType<DetailsWindow>().Single();
                        System.Drawing.Point inside = details.PointToScreen(new System.Drawing.Point(20, 20));
                        IntPtr hit = WindowFromPoint(inside);
                        Assert.That(hit == details.Handle || Native.IsChild(details.Handle, hit), Is.True, "The performance window opened behind another window.");
                        details.Close();
                        PumpFor(500);
                    }
                }
            }
            finally
            {
                SetCursorPos(previousCursor.X, previousCursor.Y);
                Native.SetThreadDpiAwarenessContext(previousDpi);
            }
        }

        [TestCase(false), TestCase(true), Apartment(ApartmentState.STA), Category("Desktop")]
        [Explicit("Requires an interactive Windows desktop with Perfview closed and free taskbar space.")]
        public void SettingsDropdownsStayOpenWhileGraphsRefresh(bool fromDetails)
        {
            Assert.That(Native.FindWindow(null, "Perfview Taskbar"), Is.EqualTo(IntPtr.Zero), "Exit the running app before this desktop regression check.");
            IntPtr previousDpi = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
            System.Drawing.Point previousCursor = Cursor.Position;
            try
            {
                Application.EnableVisualStyles();
                using (PerfviewContext context = new PerfviewContext())
                {
                    TaskbarWindow overlay = Application.OpenForms.OfType<TaskbarWindow>().Single();
                    WaitFor(() => overlay.Visible && overlay.Opacity == 1, "Graphs did not reach a safe taskbar position.");
                    if (fromDetails)
                    {
                        Native.PostMessage(overlay.Handle, TaskbarWindow.ActivateMessage, IntPtr.Zero, IntPtr.Zero);
                        WaitFor(() => Application.OpenForms.OfType<DetailsWindow>().Any(window => window.Visible), "Details did not open.");
                    }

                    Exception failure = null;
                    // Run inside ShowDialog's message loop, with the real sampling and
                    // taskbar positioning timers active throughout each interaction.
                    overlay.BeginInvoke((Action)delegate
                    {
                        SettingsWindow window = Application.OpenForms.OfType<SettingsWindow>().Single();
                        try
                        {
                            window.Activate();
                            foreach (ComboBox combo in window.Controls.OfType<ComboBox>())
                            {
                                combo.Focus();
                                // Use desktop input: opening by CB_SHOWDROPDOWN or sent
                                // window messages bypasses the native capture behavior.
                                System.Drawing.Point arrow = combo.PointToScreen(new System.Drawing.Point(combo.Width - 10, combo.Height / 2));
                                TestContext.WriteLine("Dropdown target: " + arrow + "; hit=" + WindowFromPoint(arrow) + "; combo=" + combo.Handle + "; foreground=" + Native.GetForegroundWindow() + "; dialog=" + window.Handle + "; owner=" + Native.GetWindow(window.Handle, 4));
                                SetCursorPos(arrow.X, arrow.Y);
                                mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
                                mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
                                PumpFor(30);
                                Assert.That(combo.DroppedDown, Is.True, "Dropdown did not open. Hit=" + WindowFromPoint(arrow) + "; foreground=" + Native.GetForegroundWindow() + "; focused=" + combo.Focused);
                                PumpFor(1200);
                                Assert.That(combo.DroppedDown, Is.True, "Dropdown closed during graph refresh: " + combo.SelectedItem);
                                ComboBoxInfo info = new ComboBoxInfo { Size = Marshal.SizeOf(typeof(ComboBoxInfo)) };
                                Assert.That(GetComboBoxInfo(combo.Handle, ref info), Is.True);
                                Native.Rect listBounds;
                                Assert.That(Native.GetWindowRect(info.List, out listBounds), Is.True);
                                System.Drawing.Point itemPoint = new System.Drawing.Point(listBounds.Left + 15, listBounds.Top + 10);
                                Assert.That(WindowFromPoint(itemPoint), Is.EqualTo(info.List), "Another window covered the dropdown while it was open.");
                                int next = (combo.SelectedIndex + 1) % combo.Items.Count;
                                Native.Rect itemBounds = new Native.Rect();
                                Assert.That(SendMessageRect(info.List, 0x198, new IntPtr(next), ref itemBounds).ToInt64(), Is.Not.EqualTo(-1), "Could not find the choice in the list."); // LB_GETITEMRECT
                                itemPoint = new System.Drawing.Point(listBounds.Left + (itemBounds.Left + itemBounds.Right) / 2, listBounds.Top + (itemBounds.Top + itemBounds.Bottom) / 2);
                                SetCursorPos(itemPoint.X, itemPoint.Y);
                                mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
                                mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
                                PumpFor(30);
                                Assert.That(combo.SelectedIndex, Is.EqualTo(next), "The clicked item was not selected.");
                                Assert.That(combo.DroppedDown, Is.False, "Selecting an item did not dismiss the list.");
                                Assert.That(overlay.Visible, Is.True, "Graph updates must continue while choosing settings.");
                            }
                        }
                        catch (Exception error) { failure = error; }
                        finally { window.DialogResult = DialogResult.Cancel; window.Close(); }
                    });
                    if (fromDetails)
                        Application.OpenForms.OfType<DetailsWindow>().Single().Controls.OfType<Button>().Single(button => button.Text == "Settings").PerformClick();
                    else
                    {
                        ContextMenuStrip menu = (ContextMenuStrip)typeof(PerfviewContext).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context);
                        menu.Show(overlay.Location);
                        ToolStripMenuItem item = menu.Items.OfType<ToolStripMenuItem>().Single(entry => entry.Text.StartsWith("Settings"));
                        int point = ((item.Bounds.Top + item.Height / 2) << 16) | (item.Bounds.Left + item.Width / 2);
                        SendMessage(menu.Handle, 0x200, IntPtr.Zero, new IntPtr(point));
                        SendMessage(menu.Handle, 0x201, new IntPtr(1), new IntPtr(point));
                        SendMessage(menu.Handle, 0x202, IntPtr.Zero, new IntPtr(point));
                    }
                    if (failure != null) throw failure;
                }
            }
            finally
            {
                SetCursorPos(previousCursor.X, previousCursor.Y);
                if (previousDpi != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(previousDpi);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessageRect(IntPtr window, uint message, IntPtr wParam, ref Native.Rect rect);
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
        [DllImport("user32.dll")]
        private static extern bool GetComboBoxInfo(IntPtr combo, ref ComboBoxInfo info);
        [StructLayout(LayoutKind.Sequential)]
        private struct ComboBoxInfo
        {
            public int Size;
            public Native.Rect ItemBounds, ButtonBounds;
            public uint ButtonState;
            public IntPtr Combo, Item, List;
        }

        [Test, Apartment(ApartmentState.STA), Category("Desktop")]
        [Explicit("Requires an interactive Windows desktop with Perfview closed and free taskbar space.")]
        public void OpeningAndClosingDetailsKeepsTaskbarGraphsVisible()
        {
            Assert.That(Native.FindWindow(null, "Perfview Taskbar"), Is.EqualTo(IntPtr.Zero), "Exit the running app before this desktop regression check.");
            IntPtr previousDpi = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                Application.EnableVisualStyles();
                using (PerfviewContext context = new PerfviewContext())
                {
                    TaskbarWindow overlay = Application.OpenForms.OfType<TaskbarWindow>().Single();
                    WaitFor(() => overlay.Visible && overlay.Opacity == 1, "Graphs did not reach a safe taskbar position.");
                    PumpFor(600);
                    int disappearances = 0;
                    EventHandler visibilityChanged = delegate { if (!overlay.Visible) disappearances++; };
                    overlay.VisibleChanged += visibilityChanged;
                    try
                    {
                        for (int cycle = 0; cycle < 4; cycle++)
                        {
                            // The singleton activation message uses the same OpenDetails
                            // handler as a click on the taskbar graphs or tray icon.
                            Native.PostMessage(overlay.Handle, TaskbarWindow.ActivateMessage, IntPtr.Zero, IntPtr.Zero);
                            WaitFor(() => Application.OpenForms.OfType<DetailsWindow>().Any(window => window.Visible), "The details window did not open.");
                            PumpFor(800);
                            Assert.That(disappearances, Is.Zero, "Graphs disappeared while opening details (cycle " + cycle + ").");
                            DetailsWindow details = Application.OpenForms.OfType<DetailsWindow>().Single();
                            Assert.That(details.Owner, Is.SameAs(overlay), "The popup must remain associated with the taskbar app.");
                            details.Close();
                            PumpFor(800);
                            Assert.That(disappearances, Is.Zero, "Graphs disappeared while closing details (cycle " + cycle + ").");
                            Assert.That(overlay.Visible, Is.True);
                        }
                    }
                    finally { overlay.VisibleChanged -= visibilityChanged; }
                }
            }
            finally { if (previousDpi != IntPtr.Zero) Native.SetThreadDpiAwarenessContext(previousDpi); }
        }

        private static void WaitFor(Func<bool> ready, string failure)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (!ready() && timeout.ElapsedMilliseconds < 7000) PumpFor(10);
            Assert.That(ready(), Is.True, failure);
        }

        private static void PumpFor(int milliseconds)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < milliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }
    }
}
