# Perfview

A small, native Windows 10/11 (64-bit) app that puts live performance graphs in your taskbar. Keep an eye on CPU, GPU, memory, disk activity, and network throughput at a glance.

## Features

- Live graphs for CPU, GPU, memory, disk, and network, with separate download/upload rates in the details view.
- Draggable taskbar strip that adapts to available space and stays clear of taskbar buttons.
- Choose which graphs to show, their width, position, and refresh interval.
- Optional component temperatures next to the usage values in the existing taskbar and detail graphs.
- System, light, and dark themes, plus pause/resume and optional start with Windows.
- All data stays on your machine. No telemetry or Explorer modifications. Ordinary performance graphs work without administrator rights.

## Temperatures

Enable **Show temperature next to each graph's usage** in Settings. The existing CPU, GPU, memory, disk, and network graphs show usage and temperature together, such as **35%  65°C**. The charts continue to show utilization or throughput. No extra graph or separate temperature view is added.

Temperature monitoring uses the bundled [LibreHardwareMonitor library](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). Readings are matched to their component, including sensors on subdevices. CPU package, GPU core, and drive composite temperatures are preferred; if multiple devices share a graph, it shows the highest available device temperature for that component. Availability depends on hardware and drivers. Unavailable temperatures are hidden, leaving only the usage reading; this is common for memory and network adapters. CPU and some other sensors require the separately installed [PawnIO driver](https://github.com/namazso/PawnIO) and administrator access. Performance details explain missing access and link to the driver; after installing it, exit Perfview and run it as administrator. Perfview does not install drivers automatically. Ordinary performance monitoring works without administrator rights. Temperature monitoring is off by default.

Build with `./build.ps1` using the .NET SDK; NuGet restores the pinned sensor dependencies. Portable distributions must include the DLLs and `THIRD-PARTY-NOTICES.txt` alongside `Perfview.exe`.

## Screenshots

**Taskbar graphs**

<img src="docs/screenshots/taskbar-dark.png" alt="Taskbar graphs with usage and available component temperatures on one line" width="660">

**Performance details**

<img src="docs/screenshots/details-dark.png" alt="Dark performance window with 60-second histories and available temperatures beside usage values" width="960">

**Settings**

<img src="docs/screenshots/settings-dark.png" alt="Dark settings window with the component temperature option enabled" width="480">
