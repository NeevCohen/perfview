# Contributing to Perfview

Contributions are very welcome! Bug reports, feature ideas, documentation improvements, and code are all appreciated.

## Getting involved

- Open an issue to report a bug or suggest an improvement. For bugs, include your Windows version, steps to reproduce, and what you expected to happen.
- For a larger change, open an issue first so we can discuss the approach.
- To contribute a fix or feature, fork the repository, create a branch, and open a pull request. Keep changes focused and explain what changed and how you checked it. Screenshots are helpful for UI changes.

## Building and testing

Use Windows 10/11 with .NET Framework 4.8 and the .NET SDK. Building and testing restore their pinned NuGet dependencies on the first run.

Exit any running development copy of Perfview before rebuilding, then run these commands in PowerShell from the repository root:

```powershell
.\build.ps1
.\build.ps1 -Test
```

Launch the app with `run.cmd` or `bin\Perfview.exe`. Follow the existing code style and add or update tests for behavior changes. If live performance counters are unavailable, run the deterministic tests with:

```powershell
.\tests\Run-AppTests.ps1 -Filter 'TestCategory!=LiveCounters&TestCategory!=UI&TestCategory!=Desktop'
```

Perfview is licensed under the [GNU General Public License v3.0](LICENSE). Contributions are welcome under the same license.
