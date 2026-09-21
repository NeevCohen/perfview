$ErrorActionPreference = 'Stop'

Install-Module PSScriptAnalyzer -RequiredVersion 1.24.0 -Scope CurrentUser -Force -Repository PSGallery
Import-Module PSScriptAnalyzer -RequiredVersion 1.24.0
# Gate correctness/security errors without imposing new style rules
# on the existing standalone scripts.
$scripts = @('build.ps1', 'build-installer.ps1') +
    @(Get-ChildItem tests -Filter '*.ps1' | ForEach-Object FullName) +
    @(Get-ChildItem scripts/ci -Filter '*.ps1' | ForEach-Object FullName)
$issues = @($scripts | ForEach-Object { Invoke-ScriptAnalyzer -Path $_ -Severity Error })
if ($issues.Count -gt 0) {
  $issues | Format-Table -AutoSize | Out-String | Write-Output
  throw 'PowerShell lint failed.'
}
