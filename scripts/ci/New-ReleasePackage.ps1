$ErrorActionPreference = 'Stop'

$releaseDirectory = Join-Path $env:GITHUB_WORKSPACE 'artifacts\release'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$app = Join-Path $env:PerfviewAppDirectory 'Perfview.exe'
$actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion
if ($actualVersion -ne $env:ASSEMBLY_VERSION) { throw "Unexpected app version: $actualVersion" }
$zip = Join-Path $releaseDirectory "Perfview-$env:RELEASE_VERSION-Windows-Portable.zip"
Compress-Archive -LiteralPath $app, "$app.config" -DestinationPath $zip
$installer = Join-Path $env:GITHUB_WORKSPACE "artifacts\installer\Perfview-$env:ASSEMBLY_VERSION-Setup.exe"
Copy-Item -LiteralPath $installer -Destination (Join-Path $releaseDirectory "Perfview-$env:RELEASE_VERSION-Setup.exe")
foreach ($file in Get-ChildItem -LiteralPath $releaseDirectory -File) {
  $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  # Use LF so these files also work with sha256sum on the release runner.
  Set-Content -LiteralPath "$($file.FullName).sha256" -Value "$hash  $($file.Name)`n" -Encoding ascii -NoNewline
}
