param(
    [string]$AppDirectory,
    [string]$ResultsDirectory,
    [string]$Filter
)
$ErrorActionPreference = 'Stop'
if (!$ResultsDirectory) { $ResultsDirectory = Join-Path $PSScriptRoot '..\artifacts\test-results' }
$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($dotnet) { $dotnetPath = $dotnet.Source }
else { $dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (!(Test-Path -LiteralPath $dotnetPath)) { throw 'Running NUnit tests requires the .NET SDK. Install it from https://dotnet.microsoft.com/download.' }
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$arguments = @('test', (Join-Path $PSScriptRoot 'Perfview.Tests.csproj'), '--configuration', 'Release', '--nologo', '--results-directory', $ResultsDirectory, '--logger', 'trx;LogFileName=app-tests.trx')
if ($AppDirectory) { $arguments += "-p:PerfviewAppDirectory=$([IO.Path]::GetFullPath($AppDirectory))" }
if ($Filter) { $arguments += @('--filter', $Filter) }
$arguments += @('--', "NUnit.WorkDirectory=$ResultsDirectory", "NUnit.TestOutputXml=$ResultsDirectory")
& $dotnetPath $arguments
if ($LASTEXITCODE -ne 0) { throw "NUnit tests failed (exit $LASTEXITCODE). Results: $ResultsDirectory" }
