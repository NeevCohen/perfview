param(
    [switch]$Test,
    [switch]$Bootstrap,
    [string]$InnoCompiler
)
$ErrorActionPreference = 'Stop'

$toolsDirectory = Join-Path $PSScriptRoot 'artifacts\tools'
$portableDirectory = Join-Path $toolsDirectory 'innosetup-6.7.3'

if (!$InnoCompiler) {
    $candidates = @((Join-Path $portableDirectory 'ISCC.exe'))
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if ($root) {
            $candidates += Join-Path $root 'Inno Setup 7\ISCC.exe'
            $candidates += Join-Path $root 'Inno Setup 6\ISCC.exe'
        }
    }
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

if (!$InnoCompiler -and $Bootstrap) {
    # Official pinned release; portable mode creates no shortcuts or uninstall entry.
    $download = Join-Path $toolsDirectory 'innosetup-6.7.3.exe'
    $expectedHash = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
    New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
    if (!(Test-Path -LiteralPath $download)) {
        $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
        try {
            [Net.ServicePointManager]::SecurityProtocol = $previousProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $download
        }
        finally { [Net.ServicePointManager]::SecurityProtocol = $previousProtocol }
    }
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedHash) {
        throw "Inno Setup download checksum mismatch. Remove '$download' and retry."
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )CN=Pyrsys B\.V\.(,|$)') {
        throw 'Inno Setup publisher signature could not be verified.'
    }
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/PORTABLE=1', ('/DIR="{0}"' -f $portableDirectory))
    $process = Start-Process -FilePath $download -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Inno Setup bootstrap failed (exit $($process.ExitCode))." }
    $InnoCompiler = Join-Path $portableDirectory 'ISCC.exe'
}

if (!$InnoCompiler -or !(Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup was not found. Run .\build-installer.ps1 -Bootstrap, install Inno Setup 6/7, or pass -InnoCompiler <path to ISCC.exe>.'
}
$InnoCompiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
$appDirectory = Join-Path $PSScriptRoot 'artifacts\installer\app'
$outputDirectory = Join-Path $PSScriptRoot 'artifacts\installer'
& (Join-Path $PSScriptRoot 'build.ps1') -Test:$Test -OutputDirectory $appDirectory -TestOutputDirectory (Join-Path $outputDirectory 'test-results')

& $InnoCompiler /Qp "/DAppSourceDir=$appDirectory" "/O$outputDirectory" (Join-Path $PSScriptRoot 'installer\Perfview.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appDirectory 'Perfview.exe')).FileVersion
$installer = Join-Path $outputDirectory "Perfview-$version-Setup.exe"
if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer output was not found: $installer" }
$checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$installer.sha256" -Value "$checksum  $([IO.Path]::GetFileName($installer))" -Encoding ASCII
Write-Output "Built $installer"

if ($Test) {
    & (Join-Path $PSScriptRoot 'tests\Run-InstallerTests.ps1') -InnoCompiler $InnoCompiler -AppDirectory $appDirectory
}
