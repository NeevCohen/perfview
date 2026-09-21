using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Perfview
{
    internal sealed class TaskbarWindow : Form
    {
        internal readonly MiniGraphs Graphs;
        internal event Action OpenDetails;
        internal event Action OpenMenu;
        internal event Action<int> PositionChanged;
        private readonly Timer locationTimer = new Timer { Interval = 100 };
        private readonly TaskbarLayoutMonitor layoutMonitor = new TaskbarLayoutMonitor();
        private readonly TaskbarLayoutStability layoutStability = new TaskbarLayoutStability();
        private Native.WinEventCallback layoutEvent;
        private IntPtr eventHook, observedTaskbar;
        private readonly int processId = Process.GetCurrentProcess().Id;
        private Point dragStart;
        private int initialOffset;
        private bool dragging, moved;
        internal bool HiddenByUser;
        internal static readonly uint ActivateMessage = Native.RegisterWindowMessage("PerfviewTaskbar.ShowDetails.v1");

        internal TaskbarWindow(History history, Settings settings, Palette palette)
        {
            Text = "Perfview Taskbar";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Opacity = 0;
            AutoScaleMode = AutoScaleMode.None;
            Graphs = new MiniGraphs(history, settings, palette) { Dock = DockStyle.Fill };
            Controls.Add(Graphs);
            Graphs.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    dragging = true; moved = false; dragStart = Cursor.Position;
                    Rectangle bar; IntPtr taskbar;
                    initialOffset = Graphs.Settings.Offset;
                    if (Native.TryTaskbar(out bar, out taskbar))
                    {
                        bool horizontal = bar.Width >= bar.Height;
                        int pixels = Graphs.Settings.AlignRight ? (horizontal ? bar.Right - Right : bar.Bottom - Bottom) : (horizontal ? Left - bar.Left : Top - bar.Top);
                        initialOffset = (int)(pixels / Native.Scale(taskbar));
                    }
                    Graphs.Capture = true;
                }
            };
            Graphs.MouseMove += delegate
            {
                if (!dragging) return;
                Rectangle bar; IntPtr taskbar;
                if (!Native.TryTaskbar(out bar, out taskbar)) return;
                int delta = bar.Width >= bar.Height ? Cursor.Position.X - dragStart.X : Cursor.Position.Y - dragStart.Y;
                if (!moved && Math.Abs(delta) < SystemInformation.DragSize.Width) return;
                moved = true;
                float scale = Native.Scale(taskbar);
                Graphs.Settings.Offset = Math.Max(0, initialOffset + (int)(delta / scale) * (Graphs.Settings.AlignRight ? -1 : 1));
                UpdatePosition();
            };
            Graphs.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) { if (OpenMenu != null) OpenMenu(); return; }
                if (e.Button != MouseButtons.Left || !dragging) return;
                bool didMove = moved;
                dragging = false; Graphs.Capture = false;
                if (didMove) { if (PositionChanged != null) PositionChanged(Graphs.Settings.Offset); }
                else if (OpenDetails != null) OpenDetails();
            };
            Graphs.MouseCaptureChanged += delegate { if (!Graphs.Capture) dragging = false; };
            locationTimer.Tick += delegate { UpdatePosition(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x80; return parameters; }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); UpdatePosition(); locationTimer.Start(); }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 1 || Height <= 1) return;
            Region old = Region;
            using (GraphicsPath path = GraphPaint.Rounded(new RectangleF(0, 0, Width, Height), Math.Min(Height / 2f, 10 * Native.Scale(Handle)))) Region = new Region(path);
            if (old != null) old.Dispose();
        }
        private void WatchTaskbar(IntPtr taskbar)
        {
            if (observedTaskbar == taskbar) return;
            if (eventHook != IntPtr.Zero) Native.UnhookWinEvent(eventHook);
            observedTaskbar = taskbar;
            uint process;
            Native.GetWindowThreadProcessId(taskbar, out process);
            layoutEvent = delegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
            {
                if (IsDisposed || (eventType > 0x8003 && eventType != 0x800B)) return;
                if (window != observedTaskbar && !Native.IsChild(observedTaskbar, window)) return;
                // A flyout also emits layout events. Verify the new geometry in
                // the background before changing the overlay's visibility.
                layoutMonitor.RequestRefresh();
            };
            eventHook = Native.SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, layoutEvent, process, 0, 0);
            layoutMonitor.RequestRefresh(true);
        }
        protected override void WndProc(ref Message message)
        {
            if ((uint)message.Msg == ActivateMessage) { if (OpenDetails != null) OpenDetails(); }
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }
        internal void UpdatePosition()
        {
            if (IsDisposed) return;
            Rectangle bar; IntPtr taskbar;
            if (HiddenByUser || !Native.TryTaskbar(out bar, out taskbar) || Native.IsFullscreen(taskbar, processId)) { if (Visible) Hide(); return; }
            WatchTaskbar(taskbar);
            // Win32 keeps an owned top-level window above its owner as one
            // operation, so raising Explorer cannot briefly cover the graphs.
            bool ownedByTaskbar = Native.SetWindowOwner(Handle, taskbar);
            bool overflowOpen = Native.IsTrayOverflowOpen(taskbar);
            TaskbarLayout layout = layoutStability.Select(layoutMonitor.Snapshot, taskbar, bar, overflowOpen, DateTime.UtcNow);
            Rectangle bounds;
            if (layout == null || !Placement.TryFreeOverlay(bar, layout.Occupied, Graphs.Settings.Metrics.Count, Graphs.Settings, Native.Scale(taskbar), out bounds))
            { if (Visible) Hide(); return; }
            if (Bounds != bounds) Bounds = bounds;
            if (!Visible) Show();
            if (Opacity == 0) Opacity = 1;
            if (!ownedByTaskbar) KeepAboveTaskbar();
        }
        private void KeepAboveTaskbar()
        {
            // Raising an owner also raises its owned windows. Doing that while
            // settings is active puts the dialog above its native dropdown list.
            // Keep checking safe placement, but leave interactive popup ordering
            // alone until focus and mouse capture have returned to another app.
            // Explorer raises the taskbar when its flyout opens; keep the graph
            // strip above it without activating a window or dismissing the flyout.
            if (Visible && Opacity > 0 && Form.ActiveForm == null && Native.GetCapture() == IntPtr.Zero)
                Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                locationTimer.Dispose(); layoutMonitor.Dispose();
                if (eventHook != IntPtr.Zero) { Native.UnhookWinEvent(eventHook); eventHook = IntPtr.Zero; }
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class DetailsWindow : Form
    {
        private readonly DetailGraphs graphs;
        private readonly LinkLabel temperatureStatus;
        private readonly bool showTemperatures;
        private readonly Label status;
        private readonly Button pause;
        internal event Action ShowSettings;
        internal event Action TogglePause;
        internal DetailsWindow(History history, Palette palette, bool showTemperatures = false)
        {
            this.showTemperatures = showTemperatures;
            SuspendLayout();
            Text = "Perfview \u00b7 Performance";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            // This is a tray popup. Adding/removing a normal taskbar button would
            // rearrange Explorer's icons and invalidate the strip's safe position.
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(954, 540);
            BackColor = palette.Background;
            ForeColor = palette.Text;
            Font = new Font("Segoe UI", 9);
            Label title = new Label { Text = "Performance", Font = new Font("Segoe UI Semibold", 19), Location = new Point(18, 15), Size = new Size(290, 38), ForeColor = palette.Text };
            status = new Label { Text = "LIVE  /  Updated every second", Location = new Point(20, 58), Size = new Size(480, 20), ForeColor = palette.Muted };
            Controls.Add(title); Controls.Add(status);
            graphs = new DetailGraphs(history, palette, showTemperatures) { Location = new Point(18, 94), Size = new Size(918, 370) };
            Controls.Add(graphs);
            if (showTemperatures)
            {
                temperatureStatus = new LinkLabel { Location = new Point(238, 483), Size = new Size(535, 43), ForeColor = palette.Muted, LinkColor = palette.Text, ActiveLinkColor = palette.Muted, VisitedLinkColor = palette.Text };
                temperatureStatus.LinkClicked += delegate
                {
                    try { Process.Start(new ProcessStartInfo("https://github.com/namazso/PawnIO") { UseShellExecute = true }); }
                    catch (System.ComponentModel.Win32Exception error) { MessageBox.Show(this, error.Message, "Could not open sensor help"); }
                };
                Controls.Add(temperatureStatus);
            }
            Button settings = MakeButton("Settings", new Rectangle(18, 485, 100, 34), palette);
            settings.Click += delegate { if (ShowSettings != null) ShowSettings(); };
            pause = MakeButton("Pause", new Rectangle(128, 485, 90, 34), palette);
            pause.Click += delegate { if (TogglePause != null) TogglePause(); };
            Button taskManager = MakeButton("Task Manager \u2197", new Rectangle(792, 485, 144, 34), palette);
            taskManager.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception error) { MessageBox.Show(this, error.Message, "Could not open Task Manager"); }
            };
            Controls.Add(settings); Controls.Add(pause); Controls.Add(taskManager);
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            ResumeLayout(false);
        }
        internal static Button MakeButton(string text, Rectangle bounds, Palette palette)
        {
            Button button = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, BackColor = palette.Card, ForeColor = palette.Text, Cursor = Cursors.Hand, TabStop = true };
            button.FlatAppearance.BorderColor = palette.Border;
            return button;
        }
        internal void UpdateSample(Sample sample, bool paused, int interval)
        {
            status.Text = paused ? "PAUSED  /  History is frozen" : "LIVE  /  " + (interval / 1000.0).ToString("0.#") + "s refresh  /  Last 60 seconds";
            pause.Text = paused ? "Resume" : "Pause";
            graphs.AccessibleDescription = GraphPaint.Summary(sample, showTemperatures);
            graphs.Invalidate();
            if (temperatureStatus != null)
            {
                temperatureStatus.Text = sample.Temperatures.Status.Length > 0 ? sample.Temperatures.Status : "Component temperatures in \u00b0C. \u2014 means no sensor reading is available.";
                temperatureStatus.Links.Clear();
                int driver = temperatureStatus.Text.IndexOf("PawnIO", StringComparison.Ordinal);
                if (driver >= 0) temperatureStatus.Links.Add(driver, 6);
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = BackColor.R < 128 ? 1 : 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }
    }

    internal sealed class SettingsWindow : Form
    {
        internal Settings Result;
        internal bool StartWithWindows;
        internal SettingsWindow(Settings settings, Palette palette)
        {
            SuspendLayout();
            Text = "Perfview settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(470, 575);
            BackColor = palette.Background; ForeColor = palette.Text; Font = new Font("Segoe UI", 9);
            Label title = new Label { Text = "Make room for your graphs", Font = new Font("Segoe UI Semibold", 16), Location = new Point(22, 19), Size = new Size(430, 32) };
            Controls.Add(title);
            AddLabel("Changing the anchor resets its distance. Graphs stay clear of taskbar buttons.", 22, 61, 430, 33, palette.Muted);
            AddLabel("GRAPHS", 22, 105, 150, 20, palette.Muted);
            CheckBox[] metricChecks = new CheckBox[5];
            CheckBox temperatureCheck = new CheckBox { Text = "Show temperature next to each graph's usage", Checked = settings.ShowTemperatures, Location = new Point(22, 172), Size = new Size(425, 24), TabIndex = 5 };
            bool[] selected = { settings.ShowCpu, settings.ShowGpu, settings.ShowMemory, settings.ShowDisk, settings.ShowNetwork };
            for (int i = 0; i < selected.Length; i++)
            {
                metricChecks[i] = new CheckBox { Text = GraphPaint.Name((Metric)i), Checked = selected[i], Location = new Point(22 + i * 86, 136), Size = new Size(86, 24) };
                Controls.Add(metricChecks[i]);
            }
            AddLabel("Anchor", 22, 258, 150, 24, palette.Text);
            ComboBox anchor = Combo(new[] { "Left / top edge", "Right / bottom edge" }, settings.AlignRight ? 1 : 0, 225, 253, 220);
            AddLabel("Distance from edge", 22, 298, 190, 24, palette.Text);
            NumericUpDown offset = Number(settings.Offset, 0, 10000, 225, 294, 140);
            // A dragged position can be more than halfway across the taskbar.
            // Reusing that distance at the opposite edge reverses the chosen side.
            anchor.SelectedIndexChanged += delegate { offset.Value = 0; };
            AddLabel("px", 377, 298, 55, 24, palette.Muted);
            AddLabel("Width per graph", 22, 338, 190, 24, palette.Text);
            NumericUpDown width = Number(settings.CellWidth, 68, 160, 225, 334, 140);
            AddLabel("px", 377, 338, 55, 24, palette.Muted);
            AddLabel("Refresh interval", 22, 378, 190, 24, palette.Text);
            ComboBox interval = Combo(new[] { "0.5 seconds", "1 second", "2 seconds" }, settings.Interval == 500 ? 0 : settings.Interval == 1000 ? 1 : 2, 225, 374, 220);
            AddLabel("Appearance", 22, 418, 190, 24, palette.Text);
            ComboBox theme = Combo(new[] { "System", "Dark", "Light" }, settings.Theme == "System" ? 0 : settings.Theme == "Dark" ? 1 : 2, 225, 414, 220);
            CheckBox startup = new CheckBox { Text = "Start with Windows", Checked = Startup.Enabled, Location = new Point(22, 463), Size = new Size(400, 25) };
            Controls.Add(startup);
            Button save = DetailsWindow.MakeButton("Save changes", new Rectangle(310, 518, 135, 35), palette);
            Button cancel = DetailsWindow.MakeButton("Cancel", new Rectangle(202, 518, 96, 35), palette);
            cancel.DialogResult = DialogResult.Cancel;
            save.Click += delegate
            {
                if (!Array.Exists(metricChecks, check => check.Checked))
                { MessageBox.Show(this, "Choose at least one graph to display.", "Choose a graph", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                Result = new Settings { ShowCpu = metricChecks[0].Checked, ShowGpu = metricChecks[1].Checked, ShowMemory = metricChecks[2].Checked, ShowDisk = metricChecks[3].Checked, ShowNetwork = metricChecks[4].Checked, ShowTemperatures = temperatureCheck.Checked, AlignRight = anchor.SelectedIndex == 1, Offset = (int)offset.Value, CellWidth = (int)width.Value, Interval = new[] { 500, 1000, 2000 }[interval.SelectedIndex], Theme = theme.SelectedItem.ToString() };
                StartWithWindows = startup.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save); Controls.Add(cancel);
            Controls.Add(temperatureCheck);
            AddLabel("Usage and temperature together, for example: 35%  65\u00b0C.\nSome sensors need the PawnIO driver and administrator rights.", 22, 203, 425, 38, palette.Muted);
            AcceptButton = save; CancelButton = cancel;
            ResumeLayout(false);
        }
        private void AddLabel(string text, int x, int y, int width, int height, Color color)
        { Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color }); }
        private ComboBox Combo(string[] items, int selected, int x, int y, int width)
        {
            ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Width = width };
            combo.Items.AddRange(items); combo.SelectedIndex = selected; Controls.Add(combo); return combo;
        }
        private NumericUpDown Number(int value, int minimum, int maximum, int x, int y, int width)
        {
            NumericUpDown number = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value, Location = new Point(x, y), Width = width };
            Controls.Add(number); return number;
        }
    }
}


