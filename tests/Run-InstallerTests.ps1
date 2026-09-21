param(
    [Parameter(Mandatory = $true)][string]$InnoCompiler,
    [Parameter(Mandatory = $true)][string]$AppDirectory,
    [string]$ResultsDirectory
)
$ErrorActionPreference = 'Stop'
if (!$ResultsDirectory) { $ResultsDirectory = Join-Path $PSScriptRoot '..\artifacts\installer-tests' }
$version = '5.7.1'
$toolsDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\tools'))
$moduleDirectory = Join-Path $toolsDirectory "Pester\$version"
$manifest = Join-Path $moduleDirectory 'Pester.psd1'
if (!(Test-Path -LiteralPath $manifest)) {
    $installed = Get-Module -ListAvailable Pester | Where-Object Version -eq $version | Select-Object -First 1
    if ($installed) { $manifest = $installed.Path }
    else {
        New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
        $package = Join-Path $toolsDirectory "Pester.$version.nupkg"
        if (!(Test-Path -LiteralPath $package)) {
            Write-Host "Restoring Pester $version from PowerShell Gallery..."
            $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
            try {
                [Net.ServicePointManager]::SecurityProtocol = $previousProtocol -bor [Net.SecurityProtocolType]::Tls12
                Invoke-WebRequest -UseBasicParsing -Uri "https://www.powershellgallery.com/api/v2/package/Pester/$version" -OutFile $package
            }
            finally { [Net.ServicePointManager]::SecurityProtocol = $previousProtocol }
        }
        $expectedHash = '4A27904C6814A5FBE4758F8E49861F6A1994AEE77B71165A5C43C0371BA6C580'
        if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expectedHash) {
            throw "Pester checksum mismatch. Remove '$package' and retry."
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($package, $moduleDirectory)
    }
}
Import-Module -Name $manifest -Force
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$configuration = New-PesterConfiguration
$configuration.Run.Container = New-PesterContainer -Path (Join-Path $PSScriptRoot 'Installer.Tests.ps1') -Data @{
    InnoCompiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
    AppDirectory = (Resolve-Path -LiteralPath $AppDirectory).Path
    ResultsDirectory = $ResultsDirectory
}
$configuration.Run.PassThru = $true
$configuration.Output.Verbosity = 'Detailed'
$configuration.TestResult.Enabled = $true
$configuration.TestResult.OutputFormat = 'NUnitXml'
$configuration.TestResult.OutputPath = Join-Path $ResultsDirectory 'installer-tests.xml'
$result = Invoke-Pester -Configuration $configuration
if ($result.Result -ne 'Passed' -or $result.TotalCount -eq 0) { throw "Pester installer tests failed. Results: $ResultsDirectory" }
