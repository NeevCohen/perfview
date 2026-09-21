param([switch]$Test, [string]$OutputDirectory, [string]$TestOutputDirectory)
$ErrorActionPreference = 'Stop'
$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnet) { $dotnet.Source } else { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (!(Test-Path -LiteralPath $dotnetPath)) { throw 'Building Perfview requires the .NET SDK to restore hardware sensor dependencies.' }
$output = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'bin' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
& $dotnetPath build (Join-Path $PSScriptRoot 'Perfview.csproj') --configuration Release --nologo "-p:OutputPath=$output" -p:RestoreLockedMode=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built $output\Perfview.exe"
if ($Test) {
    $testOutput = if ($TestOutputDirectory) { [System.IO.Path]::GetFullPath($TestOutputDirectory) } else { Join-Path $PSScriptRoot 'artifacts' }
    & (Join-Path $PSScriptRoot 'tests\Run-AppTests.ps1') -AppDirectory $output -ResultsDirectory $testOutput
}
