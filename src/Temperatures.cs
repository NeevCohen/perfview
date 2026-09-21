using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace Perfview
{
    internal sealed class TemperatureReading
    {
        internal readonly string Id, Hardware, Name;
        internal readonly string DeviceId;
        internal readonly Metric? Component;
        internal readonly double Celsius;
        internal TemperatureReading(string id, string hardware, string name, double? celsius, Metric? component = null, string deviceId = null)
        {
            Id = id; Hardware = hardware; Name = name;
            Component = component; DeviceId = deviceId ?? id;
            Celsius = celsius.HasValue && IsValid(celsius.Value) ? celsius.Value : double.NaN;
        }
        internal static bool IsValid(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value > -273.15; }
        internal static string Text(double value) { return IsValid(value) ? value.ToString("0.#", CultureInfo.CurrentCulture) + "\u00b0C" : "\u2014"; }
        internal int Priority
        {
            get
            {
                // Prefer the usual package/core/composite temperature over hot spots
                // or memory-junction readings when a device exposes several sensors.
                if (Component == Metric.Cpu)
                    return Name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || Name.Equals("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase) || Name.Equals("Core (Tdie)", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (Component == Metric.Gpu)
                    return Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || Name.Equals("GPU Temperature", StringComparison.OrdinalIgnoreCase) || Name.Equals("Temperature", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (Component == Metric.Disk)
                    return Name.Equals("Temperature", StringComparison.OrdinalIgnoreCase) || Name.IndexOf("Composite", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1;
                return 0;
            }
        }
    }

    internal sealed class TemperatureSnapshot
    {
        internal static readonly TemperatureSnapshot Disabled = new TemperatureSnapshot(DateTime.MinValue, new TemperatureReading[0], "Enable Temperatures in Settings to read hardware sensors.");
        internal readonly DateTime Time;
        internal readonly ReadOnlyCollection<TemperatureReading> Readings;
        internal readonly string Status;
        internal TemperatureSnapshot(DateTime time, IEnumerable<TemperatureReading> readings, string status)
        {
            Time = time; Readings = new List<TemperatureReading>(readings).AsReadOnly(); Status = status;
        }
        internal double ForComponent(Metric component)
        {
            Dictionary<string, TemperatureReading> devices = new Dictionary<string, TemperatureReading>(StringComparer.Ordinal);
            foreach (TemperatureReading reading in Readings)
            {
                if (reading.Component != component || double.IsNaN(reading.Celsius)) continue;
                TemperatureReading previous;
                if (!devices.TryGetValue(reading.DeviceId, out previous) || reading.Priority < previous.Priority || (reading.Priority == previous.Priority && reading.Celsius > previous.Celsius)) devices[reading.DeviceId] = reading;
            }
            double maximum = double.NaN;
            foreach (TemperatureReading reading in devices.Values) maximum = double.IsNaN(maximum) ? reading.Celsius : Math.Max(maximum, reading.Celsius);
            return maximum;
        }
        internal TemperatureSnapshot FreshAt(DateTime now)
        {
            return Time != DateTime.MinValue && now - Time > TimeSpan.FromSeconds(10)
                ? new TemperatureSnapshot(now, new TemperatureReading[0], "Temperature readings are delayed or unavailable.") : this;
        }
    }

    internal interface ITemperatureSource : IDisposable
    {
        TemperatureSnapshot Read();
    }

    internal sealed class HardwareTemperatureSource : ITemperatureSource
    {
        private readonly Computer computer = new Computer();
        private readonly bool lowLevelAccess;
        private readonly string accessStatus;
        private bool partial;
        internal HardwareTemperatureSource()
        {
            try
            {
                bool installed = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
                lowLevelAccess = Native.CanReadTemperatureDriver();
                accessStatus = lowLevelAccess ? "" : !installed ? "Some temperature unavailable: install PawnIO, then restart Perfview as administrator." : "Sensor driver unavailable. Check PawnIO, then restart Perfview as administrator.";
                computer.Open();
                // Isolate discovery failures so one unsupported device group cannot
                // hide readings from the other components with performance graphs.
                Action[] enable = {
                    delegate { computer.IsCpuEnabled = true; }, delegate { computer.IsGpuEnabled = true; },
                    delegate { computer.IsMemoryEnabled = true; }, delegate { computer.IsStorageEnabled = true; },
                    delegate { computer.IsNetworkEnabled = true; }
                };
                foreach (Action action in enable)
                    try { action(); }
                    catch (Exception error) { partial = true; Trace.TraceWarning(error.ToString()); }
            }
            catch { Dispose(); throw; }
        }
        public TemperatureSnapshot Read()
        {
            List<TemperatureReading> readings = new List<TemperatureReading>();
            bool incomplete = partial;
            foreach (IHardware hardware in computer.Hardware) ReadHardware(hardware, "", readings, lowLevelAccess, ref incomplete);
            readings.Sort(delegate(TemperatureReading left, TemperatureReading right)
            {
                int order = StringComparer.CurrentCultureIgnoreCase.Compare(left.Hardware, right.Hardware);
                if (order == 0) order = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
                return order == 0 ? StringComparer.Ordinal.Compare(left.Id, right.Id) : order;
            });
            return new TemperatureSnapshot(DateTime.UtcNow, readings, accessStatus.Length > 0 ? accessStatus : incomplete ? "Some hardware sensors could not be read." : "");
        }
        internal static double? SensorValue(double? value, HardwareType type, bool lowLevelAccess)
        {
            // PawnIO returns zero-filled data when its driver cannot be opened.
            // CPU/SuperIO readings then look numeric but are not measurements.
            return !lowLevelAccess && (type == HardwareType.Cpu || type == HardwareType.SuperIO || type == HardwareType.Motherboard) ? null : value;
        }
        internal static Metric? ComponentFor(HardwareType type)
        {
            switch (type)
            {
                case HardwareType.Cpu: return Metric.Cpu;
                case HardwareType.GpuNvidia: case HardwareType.GpuAmd: case HardwareType.GpuIntel: return Metric.Gpu;
                case HardwareType.Memory: return Metric.Memory;
                case HardwareType.Storage: return Metric.Disk;
                case HardwareType.Network: return Metric.Network;
                default: return null;
            }
        }
        internal static void ReadHardware(IHardware hardware, string parent, List<TemperatureReading> readings, bool lowLevelAccess, ref bool incomplete, Metric? parentComponent = null, string parentDevice = null)
        {
            string name = parent.Length == 0 ? hardware.Name : parent + " / " + hardware.Name;
            Metric? component = ComponentFor(hardware.HardwareType) ?? parentComponent;
            string device = parentDevice ?? hardware.Identifier.ToString();
            bool updated = false;
            try { hardware.Update(); updated = true; }
            catch (Exception error) { incomplete = true; Trace.TraceWarning(error.ToString()); }
            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature) continue;
                // Never reuse cached values from a device whose update failed.
                double? value = null;
                if (updated)
                    try { value = SensorValue(sensor.Value, hardware.HardwareType, lowLevelAccess); }
                    catch (Exception error) { incomplete = true; Trace.TraceWarning(error.ToString()); }
                readings.Add(new TemperatureReading(sensor.Identifier.ToString(), name, sensor.Name, value, component, device));
            }
            foreach (IHardware child in hardware.SubHardware) ReadHardware(child, name, readings, lowLevelAccess, ref incomplete, component, device);
        }
        public void Dispose()
        {
            try { computer.Close(); }
            catch (Exception error) { Trace.TraceWarning(error.ToString()); }
        }
    }

    // Hardware discovery can be slow. Keep it off both the UI thread and the
    // ordinary performance-counter worker, and publish immutable snapshots.
    internal sealed class TemperatureService : IDisposable
    {
        private readonly object workerGate = new object(), stateGate = new object();
        private readonly Timer timer;
        private readonly Func<ITemperatureSource> createSource;
        private ITemperatureSource source;
        private TemperatureSnapshot latest = TemperatureSnapshot.Disabled;
        private bool enabled, disposed;
        private int generation, sourceGeneration = -1;
        private DateTime retryAfter;
        internal TemperatureService(bool enabled, int interval)
            : this(enabled, interval, delegate { return new HardwareTemperatureSource(); }, true) { }
        internal TemperatureService(bool enabled, int interval, Func<ITemperatureSource> createSource, bool startTimer)
        {
            this.createSource = createSource;
            SetEnabled(enabled);
            timer = new Timer(delegate { Refresh(); }, null, startTimer ? 0 : Timeout.Infinite, interval);
        }
        internal TemperatureSnapshot Latest
        {
            get { lock (stateGate) return latest.FreshAt(DateTime.UtcNow); }
        }
        internal void SetEnabled(bool value)
        {
            lock (stateGate)
            {
                if (disposed) return;
                enabled = value; generation++;
                latest = value ? new TemperatureSnapshot(DateTime.MinValue, new TemperatureReading[0], "Discovering temperature sensors\u2026") : TemperatureSnapshot.Disabled;
            }
        }
        internal void SetInterval(int interval) { timer.Change(0, interval); }
        internal void Refresh()
        {
            if (!Monitor.TryEnter(workerGate)) return;
            try
            {
                int currentGeneration;
                bool read;
                lock (stateGate) { if (disposed) return; currentGeneration = generation; read = enabled; }
                if (sourceGeneration != currentGeneration)
                {
                    CloseSource(); sourceGeneration = currentGeneration; retryAfter = DateTime.MinValue;
                }
                if (!read || DateTime.UtcNow < retryAfter) return;
                TemperatureSnapshot result;
                try
                {
                    if (source == null) source = createSource();
                    result = source.Read();
                }
                catch (Exception error)
                {
                    Trace.TraceWarning(error.ToString()); CloseSource();
                    retryAfter = DateTime.UtcNow.AddSeconds(30);
                    result = new TemperatureSnapshot(DateTime.MinValue, new TemperatureReading[0], "Temperature sensors are unavailable. Retrying shortly.");
                }
                lock (stateGate) if (!disposed && enabled && generation == currentGeneration) latest = result;
            }
            finally { Monitor.Exit(workerGate); }
        }
        private void CloseSource()
        {
            if (source == null) return;
            try { source.Dispose(); }
            catch (Exception error) { Trace.TraceWarning(error.ToString()); }
            finally { source = null; }
        }
        public void Dispose()
        {
            lock (stateGate) { disposed = true; latest = TemperatureSnapshot.Disabled; }
            timer.Dispose();
            lock (workerGate) CloseSource();
        }
    }
}
