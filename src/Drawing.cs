using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Perfview
{
    internal sealed class Palette
    {
        internal Color Background, Card, Border, Text, Muted, Grid;
        internal static Palette Create(string preference)
        {
            bool light = preference == "Light";
            if (preference == "System")
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        light = key != null && Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 0)) == 1;
                }
                catch (System.Security.SecurityException) { }
            }
            return light
                ? new Palette { Background = Color.FromArgb(243, 246, 249), Card = Color.White, Border = Color.FromArgb(216, 223, 230), Text = Color.FromArgb(25, 36, 48), Muted = Color.FromArgb(94, 108, 124), Grid = Color.FromArgb(231, 236, 241) }
                : new Palette { Background = Color.FromArgb(21, 25, 31), Card = Color.FromArgb(29, 34, 42), Border = Color.FromArgb(49, 57, 68), Text = Color.FromArgb(235, 241, 248), Muted = Color.FromArgb(146, 159, 176), Grid = Color.FromArgb(42, 49, 60) };
        }
        internal Color Accent(Metric metric)
        {
            bool light = Background.R > 128;
            switch (metric)
            {
                case Metric.Cpu: return light ? Color.FromArgb(0, 132, 113) : Color.FromArgb(75, 221, 180);
                case Metric.Gpu: return light ? Color.FromArgb(186, 58, 116) : Color.FromArgb(244, 129, 181);
                case Metric.Memory: return light ? Color.FromArgb(124, 78, 200) : Color.FromArgb(181, 152, 251);
                case Metric.Disk: return light ? Color.FromArgb(181, 115, 17) : Color.FromArgb(241, 188, 92);
                default: return light ? Color.FromArgb(30, 117, 197) : Color.FromArgb(104, 182, 255);
            }
        }
    }

    internal static class GraphPaint
    {
        internal static string Name(Metric metric)
        {
            switch (metric) { case Metric.Cpu: return "CPU"; case Metric.Gpu: return "GPU"; case Metric.Memory: return "Memory"; case Metric.Disk: return "Disk"; default: return "Network"; }
        }
        internal static string Value(Sample sample, Metric metric, bool showTemperatures = false, bool compact = false)
        {
            string value = metric == Metric.Network ? MetricMath.RateText(sample.Value(metric)) : MetricMath.PercentText(sample.Value(metric));
            if (compact && metric == Metric.Network) value = value.Replace(" ", "");
            if (!showTemperatures) return value;
            double temperature = sample.Temperatures.ForComponent(metric);
            if (double.IsNaN(temperature)) return value;
            string text = compact ? temperature.ToString("0") + "\u00b0C" : TemperatureReading.Text(temperature);
            return value + "  " + text;
        }
        internal static string Summary(Sample sample, bool showTemperatures)
        {
            List<string> values = new List<string>();
            foreach (Metric metric in Enum.GetValues(typeof(Metric))) values.Add(Name(metric) + " " + Value(sample, metric, showTemperatures));
            return string.Join(" | ", values);
        }
        internal static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
        internal static void Text(Graphics graphics, string text, float size, FontStyle style, Color color, RectangleF bounds, bool right)
        {
            using (Font font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel))
            using (Brush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Alignment = right ? StringAlignment.Far : StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                graphics.DrawString(text, font, brush, bounds, format);
        }

        private static float TextWidth(Graphics graphics, string text, float size, StringFormat format)
        {
            using (Font font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                return graphics.MeasureString(text, font, int.MaxValue, format).Width;
        }

        internal static void MiniHeader(Graphics graphics, string label, string value, float scale, float valueSize, Color accent, Color color, RectangleF bounds)
        {
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.None;
                float labelSize = 9 * scale, gap = 3 * scale;
                float labelWidth = TextWidth(graphics, label, labelSize, format);
                float valueWidth = TextWidth(graphics, value, valueSize, format);
                if (labelWidth + gap + valueWidth > bounds.Width)
                {
                    value = value.Replace("  ", " ").Replace("KB/s", "K/s").Replace("MB/s", "M/s").Replace("GB/s", "G/s").Replace("TB/s", "T/s");
                    valueWidth = TextWidth(graphics, value, valueSize, format);
                }
                // Measure both runs with the same format used to draw them. Keep
                // the complete reading on one row even in a compressed taskbar.
                float fit = Math.Min(1, Math.Max(1, bounds.Width - gap - scale) / Math.Max(1, labelWidth + valueWidth));
                labelSize *= fit; valueSize *= fit;
                labelWidth = TextWidth(graphics, label, labelSize, format);
                using (Font labelFont = new Font("Segoe UI", labelSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font valueFont = new Font("Segoe UI", valueSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush labelBrush = new SolidBrush(accent))
                using (Brush valueBrush = new SolidBrush(color))
                {
                    graphics.DrawString(label, labelFont, labelBrush, new RectangleF(bounds.Left, bounds.Top, labelWidth, bounds.Height), format);
                    format.Alignment = StringAlignment.Far;
                    graphics.DrawString(value, valueFont, valueBrush, new RectangleF(bounds.Left + labelWidth + gap, bounds.Top, Math.Max(1, bounds.Width - labelWidth - gap), bounds.Height), format);
                }
            }
        }

        internal static void Graph(Graphics graphics, RectangleF rect, History history, Metric metric, Color color, Color grid, bool detailed)
        {
            if (rect.Width < 2 || rect.Height < 2) return;
            using (Pen gridPen = new Pen(grid))
            {
                if (detailed)
                {
                    for (int row = 0; row <= 4; row++) graphics.DrawLine(gridPen, rect.Left, rect.Top + rect.Height * row / 4, rect.Right, rect.Top + rect.Height * row / 4);
                    for (int column = 0; column <= 6; column++) graphics.DrawLine(gridPen, rect.Left + rect.Width * column / 6, rect.Top, rect.Left + rect.Width * column / 6, rect.Bottom);
                }
                else graphics.DrawLine(gridPen, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
            }
            if (history.Samples.Count < 2) return;
            double maximum = history.Maximum(metric);
            DateTime end = history.Latest.Time;
            List<PointF> segment = new List<PointF>();
            DateTime? previous = null;
            foreach (Sample sample in history.Samples)
            {
                double value = sample.Value(metric);
                if (double.IsNaN(value) || (previous.HasValue && (sample.Time - previous.Value).TotalSeconds > 5))
                {
                    DrawSegment(graphics, segment, rect.Bottom, color, detailed);
                    segment.Clear();
                }
                if (!double.IsNaN(value)) segment.Add(new PointF(rect.Right - (float)((end - sample.Time).TotalSeconds / 60) * rect.Width, rect.Bottom - (float)Math.Max(0, Math.Min(1, value / maximum)) * rect.Height));
                previous = sample.Time;
            }
            DrawSegment(graphics, segment, rect.Bottom, color, detailed);
        }
        private static void DrawSegment(Graphics graphics, List<PointF> points, float baseline, Color color, bool detailed)
        {
            if (points.Count < 2) return;
            List<PointF> area = new List<PointF>(points);
            area.Add(new PointF(points[points.Count - 1].X, baseline));
            area.Add(new PointF(points[0].X, baseline));
            if (detailed)
            {
                using (Brush fill = new SolidBrush(Color.FromArgb(25, color))) graphics.FillPolygon(fill, area.ToArray());
            }
            else
            {
                float top = baseline - 1;
                foreach (PointF point in points) top = Math.Min(top, point.Y);
                using (Brush fill = new LinearGradientBrush(new PointF(0, top), new PointF(0, baseline + 1), Color.FromArgb(70, color), Color.FromArgb(4, color))) graphics.FillPolygon(fill, area.ToArray());
            }
            using (Pen line = new Pen(color, detailed ? 1.7f : 1.3f)) { line.LineJoin = LineJoin.Round; graphics.DrawLines(line, points.ToArray()); }
        }
        internal static Icon CreateIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Rounded(new RectangleF(0, 0, 32, 32), 8))
                using (Brush brush = new SolidBrush(Color.FromArgb(25, 34, 43))) graphics.FillPath(brush, path);
                using (Pen pen = new Pen(Color.FromArgb(75, 221, 180), 2.5f)) graphics.DrawLines(pen, new[] { new PointF(5, 22), new PointF(10, 22), new PointF(14, 10), new PointF(18, 25), new PointF(23, 15), new PointF(27, 15) });
                IntPtr handle = bitmap.GetHicon();
                try { using (Icon temporary = Icon.FromHandle(handle)) return (Icon)temporary.Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }
    }

    internal sealed class MiniGraphs : Control
    {
        internal readonly History History;
        internal Settings Settings;
        internal Palette Palette;
        internal bool Paused;
        private bool hover;
        internal MiniGraphs(History history, Settings settings, Palette palette)
        {
            History = history; Settings = settings; Palette = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = "Perfview performance graphs. Click for details; right-click for settings.";
            MouseEnter += delegate { hover = true; Invalidate(); };
            MouseLeave += delegate { hover = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(Palette.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            List<Metric> metrics = Settings.Metrics;
            bool horizontal = Width >= Height;
            float scale = Math.Max(0.7f, Math.Min(2.5f, (horizontal ? Height : Height / metrics.Count) / 40f));
            RectangleF outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath shell = GraphPaint.Rounded(outer, 10 * scale))
            using (Brush surface = new LinearGradientBrush(new PointF(0, 0), new PointF(0, Math.Max(1, Height)), Palette.Card, Palette.Background))
            using (Pen outline = new Pen(hover ? Palette.Muted : Palette.Border))
            { g.FillPath(surface, shell); g.DrawPath(outline, shell); }
            for (int i = 0; i < metrics.Count; i++)
            {
                Metric metric = metrics[i];
                RectangleF cell = horizontal ? new RectangleF(i * Width / (float)metrics.Count, 0, Width / (float)metrics.Count, Height) : new RectangleF(0, i * Height / (float)metrics.Count, Width, Height / (float)metrics.Count);
                Color accent = Palette.Accent(metric);
                bool temperatures = Settings.ShowTemperatures;
                float pad = (temperatures ? 4 : 9) * scale;
                string label = metric == Metric.Memory ? "RAM" : metric == Metric.Network ? "NET" : GraphPaint.Name(metric).ToUpperInvariant();
                string value = Paused ? "\u2016" : GraphPaint.Value(History.Latest, metric, temperatures, true);
                float valueSize = (metric == Metric.Network || temperatures ? 9 : 11) * scale;
                RectangleF chart = new RectangleF(cell.Left + pad, cell.Top + 20 * scale, cell.Width - 2 * pad, Math.Max(2, cell.Height - 26 * scale));
                GraphPaint.Graph(g, chart, History, metric, accent, Palette.Grid, false);
                GraphPaint.MiniHeader(g, label, value, scale, valueSize, accent, Palette.Text, new RectangleF(cell.Left + pad, cell.Top + 3 * scale, Math.Max(1, cell.Width - 2 * pad), 15 * scale));
                if (i > 0 && horizontal) using (Pen border = new Pen(Color.FromArgb(110, Palette.Border))) g.DrawLine(border, cell.Left, 9 * scale, cell.Left, Height - 9 * scale);
            }
        }
    }

    internal sealed class DetailGraphs : Control
    {
        private readonly History history;
        private readonly bool showTemperatures;
        internal Palette Palette;
        internal DetailGraphs(History history, Palette palette, bool showTemperatures = false)
        {
            this.history = history; Palette = palette; this.showTemperatures = showTemperatures;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleRole = AccessibleRole.Chart;
            AccessibleName = "60-second system performance history";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(Palette.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Width / 918f;
            g.ScaleTransform(scale, scale);
            float height = Height / scale;
            for (int i = 0; i < 5; i++)
            {
                Metric metric = (Metric)i;
                RectangleF card = new RectangleF((i % 3) * 310, (i / 3) * (height / 2 + 1), metric == Metric.Network ? 608 : 298, height / 2 - 10);
                Color accent = Palette.Accent(metric);
                using (GraphicsPath path = GraphPaint.Rounded(card, 10))
                using (Brush fill = new SolidBrush(Palette.Card))
                using (Pen border = new Pen(Palette.Border)) { g.FillPath(fill, path); g.DrawPath(border, path); }
                GraphPaint.Text(g, GraphPaint.Name(metric), 13, FontStyle.Bold, accent, new RectangleF(card.X + 16, card.Y + 12, 84, 21), false);
                GraphPaint.Text(g, GraphPaint.Value(history.Latest, metric, showTemperatures), showTemperatures ? 19 : 22, FontStyle.Regular, Palette.Text, new RectangleF(card.X + 100, card.Y + 9, card.Width - 116, 29), true);
                string subtitle;
                Sample current = history.Latest;
                switch (metric)
                {
                    case Metric.Cpu: subtitle = Environment.ProcessorCount + " logical processors"; break;
                    case Metric.Gpu: subtitle = "Busiest engine \u00b7 across all GPUs"; break;
                    case Metric.Memory: subtitle = current.TotalMemory == 0 ? "Physical memory unavailable" : MetricMath.Bytes(current.UsedMemory) + " / " + MetricMath.Bytes(current.TotalMemory) + " in use"; break;
                    case Metric.Disk: subtitle = "Average active time \u00b7 all disks"; break;
                    default: subtitle = "\u2193 " + MetricMath.RateText(current.Download) + "    \u2191 " + MetricMath.RateText(current.Upload); break;
                }
                GraphPaint.Text(g, subtitle, 11, FontStyle.Regular, Palette.Muted, new RectangleF(card.X + 16, card.Y + 38, card.Width - 32, 18), false);
                RectangleF graph = new RectangleF(card.X + 16, card.Y + 72, card.Width - 32, card.Height - 106);
                string ceiling = metric == Metric.Network ? MetricMath.RateText(history.Maximum(metric)) : "100%";
                GraphPaint.Text(g, ceiling, 9, FontStyle.Regular, Palette.Muted, new RectangleF(card.X + 16, card.Y + 56, card.Width - 32, 15), true);
                GraphPaint.Graph(g, graph, history, metric, accent, Palette.Grid, true);
                GraphPaint.Text(g, "60 seconds", 10, FontStyle.Regular, Palette.Muted, new RectangleF(graph.Left, graph.Bottom + 6, 100, 17), false);
                GraphPaint.Text(g, "now", 10, FontStyle.Regular, Palette.Muted, new RectangleF(graph.Right - 50, graph.Bottom + 6, 50, 17), true);
                if (double.IsNaN(current.Value(metric))) GraphPaint.Text(g, history.Samples.Count < 3 ? "Collecting samples\u2026" : "Counter unavailable", 12, FontStyle.Regular, Palette.Muted, new RectangleF(graph.Left + 12, graph.Top + 8, graph.Width - 24, 22), false);
            }
        }
    }
}

