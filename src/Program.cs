using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Perfview
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try { Native.SetThreadDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }
            bool first;
            using (Mutex instance = new Mutex(true, @"Local\PerfviewTaskbar.v1", out first))
            {
                if (!first)
                {
                    IntPtr window = Native.FindWindow(null, "Perfview Taskbar");
                    if (window != IntPtr.Zero) Native.PostMessage(window, TaskbarWindow.ActivateMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, "Perfview", MessageBoxButtons.OK, MessageBoxIcon.Error); };
                using (PerfviewContext context = new PerfviewContext()) Application.Run(context);
                instance.ReleaseMutex();
            }
        }
    }

    internal sealed class PerfviewContext : ApplicationContext
    {
        private Settings settings = Settings.Load(Settings.FilePath);
        private Palette palette;
        private readonly History history = new History();
        private readonly TaskbarWindow overlay;
        private readonly NotifyIcon tray;
        private readonly Icon icon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem pauseItem, visibleItem;
        private readonly ToolTip tooltip = new ToolTip { InitialDelay = 600, ReshowDelay = 300, AutoPopDelay = 10000 };
        private readonly SamplingService sampler;
        private DetailsWindow details;
        private SettingsWindow settingsWindow;
        private bool paused, exiting;
        private readonly System.Windows.Forms.Timer themeTimer = new System.Windows.Forms.Timer { Interval = 3000 };

        internal PerfviewContext()
        {
            palette = Palette.Create(settings.Theme);
            icon = GraphPaint.CreateIcon();
            overlay = new TaskbarWindow(history, settings, palette);
            overlay.Icon = icon;
            menu = new ContextMenuStrip();
            menu.Items.Add("Open performance", null, delegate { OpenDetails(); });
            menu.Items.Add("Settings\u2026", null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            pauseItem = new ToolStripMenuItem("Pause graphs", null, delegate { TogglePause(); });
            visibleItem = new ToolStripMenuItem("Show taskbar graphs", null, delegate { overlay.HiddenByUser = !overlay.HiddenByUser; visibleItem.Checked = !overlay.HiddenByUser; overlay.UpdatePosition(); }) { Checked = true };
            menu.Items.Add(pauseItem); menu.Items.Add(visibleItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit Perfview", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = icon, Text = "Perfview \u00b7 Live performance", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenDetails(); };
            overlay.OpenDetails += OpenDetails;
            overlay.OpenMenu += delegate { menu.Show(Cursor.Position); };
            overlay.PositionChanged += delegate { SaveSettings(); };
            overlay.Show();
            sampler = new SamplingService(settings.Interval, ReceiveSample, settings.ShowTemperatures);
            themeTimer.Tick += delegate
            {
                Palette next = Palette.Create(settings.Theme);
                if (next.Background != palette.Background)
                {
                    palette = next; overlay.Graphs.Palette = palette; overlay.Graphs.Invalidate();
                    if (details != null && !details.IsDisposed) { details.Close(); OpenDetails(); }
                }
            };
            themeTimer.Start();
            // Introduce the controls on first launch; subsequent launches stay in the taskbar.
            if (!System.IO.File.Exists(Settings.FilePath))
            {
                SaveSettings();
                overlay.BeginInvoke((Action)OpenDetails);
            }
        }
        private void ReceiveSample(Sample sample)
        {
            if (overlay.IsDisposed || !overlay.IsHandleCreated) return;
            try
            {
                overlay.BeginInvoke((Action)delegate
                {
                    if (exiting || paused) return;
                    history.Add(sample);
                    overlay.Graphs.Invalidate();
                    string summary = GraphPaint.Summary(sample, settings.ShowTemperatures);
                    tray.Text = summary.Length > 63 ? summary.Substring(0, 63) : summary;
                    tooltip.SetToolTip(overlay.Graphs, summary + "\nDownload " + MetricMath.RateText(sample.Download) + " | Upload " + MetricMath.RateText(sample.Upload) + "\nDisk read " + MetricMath.RateText(sample.DiskRead) + " | write " + MetricMath.RateText(sample.DiskWrite) + "\nClick for details \u00b7 Drag to move \u00b7 Right-click for settings");
                    overlay.Graphs.AccessibleDescription = summary;
                    if (details != null && !details.IsDisposed) details.UpdateSample(sample, paused, settings.Interval);
                });
            }
            catch (InvalidOperationException) { } // The window may close between checking and posting.
        }
        private void OpenDetails()
        {
            if (exiting) return;
            if (details != null && !details.IsDisposed) { details.Activate(); return; }
            details = new DetailsWindow(history, palette, settings.ShowTemperatures) { Icon = icon };
            details.ShowSettings += OpenSettings;
            details.TogglePause += TogglePause;
            details.UpdateSample(history.Latest, paused, settings.Interval);
            details.Show(overlay);
            details.Location = Placement.Popup(overlay.Bounds, details.Size, Screen.FromRectangle(overlay.Bounds).WorkingArea);
            details.Activate();
        }
        private void OpenSettings()
        {
            if (settingsWindow != null && !settingsWindow.IsDisposed) { settingsWindow.Activate(); return; }
            using (settingsWindow = new SettingsWindow(settings, palette) { Icon = icon })
            {
                if (settingsWindow.ShowDialog() != DialogResult.OK) return;
                try
                {
                    if (Startup.Enabled != settingsWindow.StartWithWindows) Startup.Set(settingsWindow.StartWithWindows);
                    settings = settingsWindow.Result;
                    SaveSettings();
                    palette = Palette.Create(settings.Theme);
                    overlay.Graphs.Settings = settings;
                    overlay.Graphs.Palette = palette;
                    overlay.UpdatePosition(); overlay.Graphs.Invalidate();
                    sampler.SetInterval(settings.Interval);
                    sampler.SetTemperaturesEnabled(settings.ShowTemperatures);
                    if (details != null && !details.IsDisposed) { details.Close(); OpenDetails(); }
                }
                catch (Exception error) { MessageBox.Show(error.Message, "Could not apply settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
        private void SaveSettings()
        {
            try { settings.Save(Settings.FilePath); }
            catch (Exception error) { MessageBox.Show("Your changes work for this session, but could not be saved.\n\n" + error.Message, "Perfview settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void TogglePause()
        {
            paused = !paused;
            pauseItem.Text = paused ? "Resume graphs" : "Pause graphs";
            pauseItem.Checked = paused;
            overlay.Graphs.Paused = paused; overlay.Graphs.Invalidate();
            if (details != null && !details.IsDisposed) details.UpdateSample(history.Latest, paused, settings.Interval);
        }
        protected override void ExitThreadCore()
        {
            exiting = true;
            themeTimer.Stop();
            tray.Visible = false;
            if (details != null) details.Close();
            if (settingsWindow != null) settingsWindow.Close();
            overlay.Close();
            base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                exiting = true; sampler.Dispose(); themeTimer.Dispose(); tooltip.Dispose(); tray.Dispose(); menu.Dispose();
                if (details != null) details.Dispose();
                overlay.Dispose(); icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

