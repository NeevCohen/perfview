using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Perfview
{
    internal enum Metric { Cpu, Gpu, Memory, Disk, Network }

    internal sealed class Sample
    {
        public DateTime Time = DateTime.UtcNow;
        public double Cpu = double.NaN, Gpu = double.NaN, Memory = double.NaN, Disk = double.NaN;
        public double Download = double.NaN, Upload = double.NaN, DiskRead = double.NaN, DiskWrite = double.NaN;
        public ulong TotalMemory, UsedMemory;
        public double Value(Metric metric)
        {
            switch (metric) { case Metric.Cpu: return Cpu; case Metric.Gpu: return Gpu; case Metric.Memory: return Memory; case Metric.Disk: return Disk; default: return Download + Upload; }
        }
    }

    internal static class MetricMath
    {
        internal static double Percent(double value) { return double.IsNaN(value) ? double.NaN : Math.Max(0, Math.Min(100, value)); }
        internal static double Cpu(ulong idle, ulong total, ulong oldIdle, ulong oldTotal)
        {
            if (total <= oldTotal || idle < oldIdle) return double.NaN;
            return Percent(100.0 * (1 - Math.Min(idle - oldIdle, total - oldTotal) / (double)(total - oldTotal)));
        }
        internal static double Rate(long current, long previous, double seconds)
        {
            return seconds <= 0 || current < previous ? double.NaN : (current - previous) / seconds;
        }
        internal static string PercentText(double value) { return double.IsNaN(value) ? "\u2014" : value.ToString("0", CultureInfo.CurrentCulture) + "%"; }
        internal static string Bytes(double value)
        {
            if (double.IsNaN(value)) return "\u2014";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString(value < 10 && unit > 0 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + units[unit];
        }
        internal static string RateText(double value) { return double.IsNaN(value) ? "\u2014" : Bytes(value) + "/s"; }
    }

    internal sealed class History
    {
        internal readonly List<Sample> Samples = new List<Sample>();
        internal Sample Latest { get { return Samples.Count == 0 ? new Sample() : Samples[Samples.Count - 1]; } }
        internal void Add(Sample sample)
        {
            Samples.Add(sample);
            DateTime cutoff = sample.Time.AddSeconds(-60);
            while (Samples.Count > 1 && Samples[0].Time < cutoff) Samples.RemoveAt(0);
        }
        internal double Maximum(Metric metric)
        {
            if (metric != Metric.Network) return 100;
            double max = 1024;
            foreach (Sample sample in Samples) { double value = sample.Value(metric); if (!double.IsNaN(value)) max = Math.Max(max, value); }
            // A power-of-two ceiling keeps the network scale readable and stable.
            return Math.Pow(2, Math.Ceiling(Math.Log(max, 2)));
        }
    }

    internal sealed class GpuUtilization
    {
        private readonly Dictionary<string, double> engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        internal void Add(string instance, double value, uint status)
        {
            if (status > 1 || string.IsNullOrEmpty(instance) || double.IsNaN(value) || double.IsInfinity(value) || value < 0) return;
            // Instances are per process. Sum processes on the same physical engine,
            // retaining the adapter LUID and engine index (not just "3D" or "Copy").
            int start = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            int end = instance.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
            if (start < 0 || end <= start) return;
            string engine = instance.Substring(start, end - start);
            if (engine.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase) < 0 || engine.IndexOf("_eng_", StringComparison.OrdinalIgnoreCase) < 0) return;
            double total;
            engines.TryGetValue(engine, out total);
            engines[engine] = total + value;
        }

        internal double Value
        {
            get
            {
                if (engines.Count == 0) return double.NaN;
                double busiest = 0;
                foreach (double value in engines.Values) busiest = Math.Max(busiest, value);
                return MetricMath.Percent(busiest);
            }
        }
    }

    internal sealed class MetricsReader : IDisposable
    {
        private IntPtr query, diskIdle, diskRead, diskWrite, gpuEngines;
        private ulong oldIdle, oldTotal;
        private bool hasCpu;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double previousTime;
        private Dictionary<string, long[]> previousNetwork = new Dictionary<string, long[]>();

        internal MetricsReader()
        {
            if (Native.PdhOpenQuery(null, IntPtr.Zero, out query) == 0)
            {
                diskIdle = AddCounter(@"\PhysicalDisk(_Total)\% Idle Time");
                diskRead = AddCounter(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
                diskWrite = AddCounter(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
                gpuEngines = AddCounter(@"\GPU Engine(*)\Utilization Percentage");
                Native.PdhCollectQueryData(query);
            }
        }
        private IntPtr AddCounter(string path)
        {
            IntPtr handle;
            return Native.PdhAddEnglishCounter(query, path, IntPtr.Zero, out handle) == 0 ? handle : IntPtr.Zero;
        }
        private static double Counter(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return double.NaN;
            uint type;
            Native.CounterValue value;
            uint result = Native.PdhGetFormattedCounterValue(handle, 0x200 | 0x8000, out type, out value);
            return result == 0 && value.Status <= 1 && !double.IsInfinity(value.Value) ? Math.Max(0, value.Value) : double.NaN;
        }
        private double ReadGpu()
        {
            if (gpuEngines == IntPtr.Zero) return double.NaN;
            const uint moreData = 0x800007D2;
            const uint format = 0x200 | 0x8000; // PDH_FMT_DOUBLE | PDH_FMT_NOCAP100
            // Wildcard arrays discover new processes/engines on each collection.
            // Re-query the size on retries: PDH does not guarantee the size returned
            // by an undersized, nonempty buffer when instances change.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint size = 0, count;
                if (Native.PdhGetFormattedCounterArray(gpuEngines, format, ref size, out count, IntPtr.Zero) != moreData || size == 0 || size > int.MaxValue) return double.NaN;
                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    uint allocated = size;
                    uint result = Native.PdhGetFormattedCounterArray(gpuEngines, format, ref size, out count, buffer);
                    if (result == moreData) continue;
                    if (result != 0) return double.NaN;
                    int itemSize = Marshal.SizeOf(typeof(Native.CounterItem));
                    if ((ulong)count * (uint)itemSize > allocated) return double.NaN;
                    GpuUtilization gpu = new GpuUtilization();
                    for (int i = 0; i < count; i++)
                    {
                        Native.CounterItem item = (Native.CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer, i * itemSize), typeof(Native.CounterItem));
                        gpu.Add(Marshal.PtrToStringUni(item.Name), item.Value.Value, item.Value.Status);
                    }
                    return gpu.Value;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return double.NaN;
        }
        internal Sample Read()
        {
            Sample sample = new Sample();
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = now - previousTime;
            bool discontinuity = previousTime == 0 || elapsed > 10;
            Native.FileTime idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                ulong total = kernel.Value + user.Value;
                if (hasCpu && !discontinuity) sample.Cpu = MetricMath.Cpu(idle.Value, total, oldIdle, oldTotal);
                oldIdle = idle.Value; oldTotal = total; hasCpu = true;
            }
            else hasCpu = false;

            Native.MemoryStatus memory = new Native.MemoryStatus();
            memory.Length = (uint)Marshal.SizeOf(typeof(Native.MemoryStatus));
            if (Native.GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0)
            {
                sample.TotalMemory = memory.TotalPhysical;
                sample.UsedMemory = memory.TotalPhysical - memory.AvailablePhysical;
                sample.Memory = MetricMath.Percent(100.0 * sample.UsedMemory / sample.TotalMemory);
            }
            if (query != IntPtr.Zero && Native.PdhCollectQueryData(query) == 0 && !discontinuity)
            {
                sample.Disk = MetricMath.Percent(100 - Counter(diskIdle));
                sample.DiskRead = Counter(diskRead);
                sample.DiskWrite = Counter(diskWrite);
                sample.Gpu = ReadGpu();
            }
            ReadNetwork(sample, elapsed, discontinuity);
            previousTime = now;
            return sample;
        }

        private void ReadNetwork(Sample sample, double elapsed, bool discontinuity)
        {
            Dictionary<string, long[]> current = new Dictionary<string, long[]>();
            double download = 0, upload = 0;
            int matched = 0, eligible = 0, failed = 0;
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    eligible++;
                    try
                    {
                        IPv4InterfaceStatistics stats = adapter.GetIPv4Statistics();
                        current[adapter.Id] = new[] { stats.BytesReceived, stats.BytesSent };
                        long[] previous;
                        if (!discontinuity && previousNetwork.TryGetValue(adapter.Id, out previous))
                        {
                            double down = MetricMath.Rate(stats.BytesReceived, previous[0], elapsed);
                            double up = MetricMath.Rate(stats.BytesSent, previous[1], elapsed);
                            if (!double.IsNaN(down) && !double.IsNaN(up)) { download += down; upload += up; matched++; }
                        }
                    }
                    catch (NetworkInformationException) { failed++; }
                }
                if (!discontinuity && failed == 0 && (matched > 0 || eligible == 0)) { sample.Download = download; sample.Upload = upload; }
            }
            catch (NetworkInformationException) { }
            previousNetwork = current;
        }
        public void Dispose() { if (query != IntPtr.Zero) { Native.PdhCloseQuery(query); query = IntPtr.Zero; } }
    }

    internal sealed class SamplingService : IDisposable
    {
        private readonly object gate = new object();
        private readonly Action<Sample> publish;
        private readonly System.Threading.Timer timer;
        private MetricsReader reader;
        private bool disposed;
        internal SamplingService(int interval, Action<Sample> publish)
        {
            this.publish = publish;
            timer = new System.Threading.Timer(Tick, null, 0, interval);
        }
        internal void SetInterval(int interval) { timer.Change(0, interval); }
        private void Tick(object state)
        {
            if (!System.Threading.Monitor.TryEnter(gate)) return;
            try
            {
                if (disposed) return;
                if (reader == null) reader = new MetricsReader();
                publish(reader.Read());
            }
            catch (Exception error) { Trace.TraceError(error.ToString()); publish(new Sample()); }
            finally { System.Threading.Monitor.Exit(gate); }
        }
        public void Dispose()
        {
            timer.Dispose();
            lock (gate) { disposed = true; if (reader != null) reader.Dispose(); }
        }
    }
}


