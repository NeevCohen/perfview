using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace Perfview
{
    public sealed class Settings
    {
        public bool ShowCpu = true, ShowGpu = true, ShowMemory = true, ShowDisk = true, ShowNetwork = true;
        public bool AlignRight;
        public int Offset = 160, CellWidth = 88, Interval = 1000;
        public string Theme = "System";

        internal List<Metric> Metrics
        {
            get
            {
                List<Metric> metrics = new List<Metric>();
                if (ShowCpu) metrics.Add(Metric.Cpu);
                if (ShowGpu) metrics.Add(Metric.Gpu);
                if (ShowMemory) metrics.Add(Metric.Memory);
                if (ShowDisk) metrics.Add(Metric.Disk);
                if (ShowNetwork) metrics.Add(Metric.Network);
                return metrics;
            }
        }
        internal void Normalize()
        {
            if (Metrics.Count == 0) ShowCpu = true;
            Offset = Math.Max(0, Math.Min(10000, Offset));
            CellWidth = Math.Max(68, Math.Min(160, CellWidth));
            if (Interval != 500 && Interval != 1000 && Interval != 2000) Interval = 1000;
            if (Theme != "System" && Theme != "Dark" && Theme != "Light") Theme = "System";
        }
        internal static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PerfviewTaskbar", "settings.xml"); } }
        internal static Settings Load(string path)
        {
            try
            {
                using (FileStream file = File.OpenRead(path))
                {
                    Settings settings = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(file);
                    settings.Normalize();
                    return settings;
                }
            }
            catch (IOException) { return new Settings(); }
            catch (UnauthorizedAccessException) { return new Settings(); }
            catch (InvalidOperationException) { return new Settings(); }
        }
        internal void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (FileStream file = File.Create(temporary)) new XmlSerializer(typeof(Settings)).Serialize(file, this);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }

    internal static class Startup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static bool Enabled
        {
            get { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && key.GetValue("PerfviewTaskbar") != null; }
        }
        internal static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue("PerfviewTaskbar", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("PerfviewTaskbar", false);
            }
        }
    }
}

