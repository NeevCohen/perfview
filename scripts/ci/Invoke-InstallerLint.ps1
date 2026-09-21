$ErrorActionPreference = 'Stop'

# Match build-installer.ps1's compiler discovery order.
$candidates = @((Join-Path $env:GITHUB_WORKSPACE 'artifacts\tools\innosetup-6.7.3\ISCC.exe'))
$onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($onPath) { $candidates += $onPath.Source }
foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
  if ($root) {
    $candidates += Join-Path $root 'Inno Setup 7\ISCC.exe'
    $candidates += Join-Path $root 'Inno Setup 6\ISCC.exe'
  }
}
$compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (!$compiler) { throw 'Inno Setup compiler was not found after the installer build.' }
"INNO_COMPILER=$compiler" >> $env:GITHUB_ENV
# /O- checks the Inno/Pascal source without emitting another installer.
# Inno has no warnings-as-errors switch, so enforce its diagnostics here.
& $compiler /O- "/DAppSourceDir=$env:PerfviewAppDirectory" installer/Perfview.iss 2>&1 |
  Tee-Object -FilePath artifacts/installer-lint.log
if ($LASTEXITCODE -ne 0) { throw 'Installer source validation failed.' }
if (Select-String -LiteralPath artifacts/installer-lint.log -Pattern '^\s*Warning:' -Quiet) {
  throw 'Installer compiler warnings must be resolved.'
}
