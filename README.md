# Perfview

A small, native Windows app that puts live performance graphs in your taskbar area. CPU, GPU, memory, disk activity, and network throughput are visible at a glance. Click the strip for a larger 60-second history.

## Install

Run **`Perfview-0.1.0.0-Setup.exe`** from `artifacts\installer\` (see **Build an installer** below to create it). Setup installs for your account in `%LOCALAPPDATA%\Programs\Perfview`, adds a Start menu shortcut, and offers an optional desktop shortcut. Administrator rights are not required.

Exit Perfview from its right-click menu before installing, upgrading, or uninstalling. Run a newer installer to update the existing installation; your settings are preserved. Setup checks for .NET Framework 4.8 or later and explains what to install if it is missing.

Uninstall through **Windows Settings > Apps > Installed apps > Perfview** (Windows 10: **Apps & features**). Uninstall removes the program, shortcuts, and any startup entry pointing to this installation. It keeps your settings in `%LOCALAPPDATA%\PerfviewTaskbar`; delete that folder manually if you want to reset them.

## Run without installing

Double-click **`run.cmd`**. It builds the app on first launch, then starts `bin\Perfview.exe`. You can also run the executable directly or copy it and `Perfview.exe.config` to another folder.

The first launch opens the performance window to introduce the controls. Close it to leave just the taskbar graphs running.

Requires Windows 10/11 and .NET Framework 4.8 (included on current Windows installations). No NuGet downloads, SDK installation, administrator privileges, or Explorer modifications are needed.

## Use

- **Click** the taskbar graphs or tray icon to open the performance window.
- The performance window is a tray popup with no extra taskbar button, so opening and closing it does not rearrange the taskbar graphs.
- **Drag** the rounded strip along the taskbar. It snaps to the nearest safe space; your preferred position is saved.
- **Right-click** the strip or tray icon for Settings, Pause, Show/hide, and Exit.
- **Settings** lets you choose graphs, anchor edge, distance from that edge, width, update interval (0.5 / 1 / 2 seconds), and system/light/dark appearance. Switching anchors resets the distance to zero and picks the nearest safe space on the chosen side; you can then adjust the distance or drag the strip.
- **GPU** is enabled by default, including when upgrading existing settings. The strip automatically fits its enabled graphs into free taskbar space.
- **Start with Windows** is optional and off by default. It registers the executable's current location for your account; keep that location stable or toggle the option again after moving the app.
- **Escape** or the close button dismisses the details window. The graphs keep running. Use **Exit Perfview** to quit.
- A second launch opens the existing instance's details window.

The tray icon may initially be in Windows' hidden-icons menu. Drag it into the visible system tray if you want it always accessible.

## Taskbar behavior

The strip is a rounded, non-activating overlay **over the primary taskbar**, rather than a registered Explorer toolbar. Windows 11 does not provide the old deskband extension mechanism ([Microsoft discussion](https://github.com/microsoft/WindowsAppSDK/discussions/2320)). Perfview reads the taskbar's accessibility bounds and native tray bounds to protect Start, Search, Widgets, app buttons, and the entire notification area (including language, audio, network, clock, and Show desktop).

Placement is constrained to a free interval with a small safety margin. Your offset is a preference: the strip snaps away from buttons, relocates when taskbar contents change, and can shrink to 68 logical pixels per graph. If there is no readable free space, discovery fails, or the layout data becomes stale, the strip hides. Its tray icon still opens the graphs and settings. Reduce the enabled graphs or width to fit a crowded taskbar. Layout is checked in the background, with Explorer layout notifications invalidating old positions; it does not reserve or resize Explorer's controls.

It follows the primary taskbar's bounds and DPI, hides when the taskbar is hidden or a foreground fullscreen app covers that monitor, and rediscovers the taskbar after Explorer restarts. Secondary taskbars are not supported in this version. Mixed-DPI changes, auto-hide, fullscreen apps, and Explorer restart handling are implemented but should be checked on your own display setup.

## Metrics

All data stays on your machine. There is no telemetry or network service.

| Graph | Measurement |
| --- | --- |
| CPU | Busy CPU time from `GetSystemTimes`, across logical processors in the current processor group. This can differ from Task Manager's frequency-adjusted utilization. Systems with more than 64 logical processors require a group-aware collector for a machine-wide total. |
| GPU | Busiest physical engine across all GPUs, using `GPU Engine(*)\Utilization Percentage`. Process contributions are added per adapter and engine, then the highest engine utilization is shown on a 0–100% scale, following [Task Manager's approach](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/). Requires a driver exposing Windows GPU performance counters (WDDM 2.0+). Unavailable counters show **—**. |
| Memory | Physical memory in use from `GlobalMemoryStatusEx`. |
| Disk | `100 - PhysicalDisk(_Total)\% Idle Time`: average active time across physical disks. Hover also shows total read/write throughput. |
| Network | Received + sent bytes/sec across active non-loopback, non-tunnel interfaces; download/upload are separate in details. Virtual/VPN adapters can cause traffic to be counted on more than one interface. |

CPU, GPU, memory and disk use a fixed 0–100% scale. Network auto-scales to the recent peak with a minimum ceiling of 1 KB/s. Byte labels use powers of 1024. Gaps and unavailable counters appear as **—**. Graphs warm up after launch; the full 60-second history fills as the app runs. Pausing freezes the displayed history while collectors continue sampling. Sample gaps longer than five seconds break the graph line.

Settings are stored in `%LOCALAPPDATA%\PerfviewTaskbar\settings.xml`. Missing or corrupt settings use defaults. Start-with-Windows uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PerfviewTaskbar`. Setup updates an existing startup entry to the installed location, but does not enable startup if it was off. To remove a portable copy, disable its startup option, exit, and delete its folder; the optional settings folder can also be removed.

## Build and verify

Exit the running app from its right-click menu before rebuilding, since Windows locks running executables.

```powershell
.\build.ps1
.\build.ps1 -Test
```

The build uses the C# compiler included with .NET Framework, with warnings treated as errors. `Perfview.csproj` also opens in Visual Studio. Tests use [NUnit](https://docs.nunit.org/articles/nunit/intro.html) with the standard .NET test runner and require the .NET SDK. Their pinned NuGet dependencies are restored on the first test run. Building or running the app without `-Test` still needs no SDK or package downloads.

Tests cover CPU/rate calculations, GPU engine aggregation across processes and adapters, counter resets, time-based history, corrupt/persisted/legacy settings, taskbar geometry at multiple DPI scales, live Windows counters, and rendering in both themes. Previews are written to `artifacts\`. Live-counter checks can fail if performance counters are disabled or unavailable, including GPU drivers without WDDM 2.0+ counters. To test without replacing a running build, use `-OutputDirectory .\artifacts\test-build`.

The NUnit fixtures use independent test cases, setup/teardown, and parameterized geometry and theme cases. Results are written as TRX and NUnit XML, with rendered previews attached to the UI test results. You can also run them directly (the standalone runner builds into `artifacts\tests\app`):

```powershell
.\tests\Run-AppTests.ps1
dotnet test .\tests\Perfview.Tests.csproj --configuration Release

# Run deterministic tests without live Windows counters or UI.
.\tests\Run-AppTests.ps1 -Filter 'TestCategory!=LiveCounters&TestCategory!=UI&TestCategory!=Desktop'
```

The standalone runner writes results to `artifacts\test-results`. Use `-ResultsDirectory` to change that location, or `-TestOutputDirectory` with `build.ps1`. `LiveCounters` and `UI` tests run by default. `Desktop` tests are explicit opt-in diagnostics: select a test by fully qualified name with `-Filter` and follow its prerequisite message (some require the app running, others require it closed).

## Build an installer

```powershell
# First build: download and verify a portable Inno Setup compiler.
.\build-installer.ps1 -Bootstrap

# Later builds reuse the compiler; -Test also checks install/upgrade/uninstall.
.\build-installer.ps1 -Test
```

The bootstrap uses a pinned [official Inno Setup release](https://jrsoftware.org/isdl.php), verifies its SHA-256 and publisher signature, and keeps the compiler under `artifacts\tools` without registering it on your system. Alternatively, install Inno Setup 6/7 or pass `-InnoCompiler 'C:\path\to\ISCC.exe'`. The first test run also restores its test dependencies; installing Perfview needs no internet connection once .NET Framework is available.

The output is `artifacts\installer\Perfview-<version>-Setup.exe` with a `.sha256` checksum file. The version comes from `src\AssemblyInfo.cs`. Builds use a separate staging directory, so you can keep your development copy running. Only the app and its configuration are packaged. The installer source is `installer\Perfview.iss`; keep its `AppId` stable for future upgrades.

Installer integration tests use [Pester 5](https://pester.dev/docs/v5/quick-start). The runner restores the pinned, checksum-verified Pester module into `artifacts\tools` if it is not already available. Each test starts with a fresh installation directory; Pester teardown uninstalls the test app and removes its startup value even when an assertion fails. These tests use unique names, registry values, and shortcuts, and check file integrity, reinstall, shortcut options, startup migration/cleanup, and blocking install/uninstall while an instance is running. Logs and `installer-tests.xml` stay under `artifacts\installer-tests`. Run them in a normal Windows user session with permission to create per-user shortcuts and registry entries.

To run only the installer suite against an existing build:

```powershell
.\tests\Run-InstallerTests.ps1 -InnoCompiler .\artifacts\tools\innosetup-6.7.3\ISCC.exe -AppDirectory .\artifacts\installer\app
```

The generated installer is unsigned. Public releases should be signed with your code-signing certificate; unsigned downloads may trigger Windows SmartScreen.

Implementation references: [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes), [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex), [language-independent counters](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw).
