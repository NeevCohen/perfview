param([switch]$Test, [string]$OutputDirectory, [string]$TestOutputDirectory)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 is required (included with Windows 10/11).' }
$output = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'bin' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Xml.dll')
$frameworkWpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references += @("/r:$frameworkWpf\UIAutomationClient.dll", "/r:$frameworkWpf\UIAutomationTypes.dll", "/r:$frameworkWpf\WindowsBase.dll")
& $compiler /nologo /warn:4 /warnaserror+ /optimize+ /target:winexe /platform:anycpu /main:Perfview.Program "/win32manifest:$PSScriptRoot\app.manifest" "/out:$output\Perfview.exe" $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'App.config') -Destination (Join-Path $output 'Perfview.exe.config') -Force
Write-Output "Built $output\Perfview.exe"
if ($Test) {
    $testOutput = if ($TestOutputDirectory) { [System.IO.Path]::GetFullPath($TestOutputDirectory) } else { Join-Path $PSScriptRoot 'artifacts' }
    & (Join-Path $PSScriptRoot 'tests\Run-AppTests.ps1') -AppDirectory $output -ResultsDirectory $testOutput
}
