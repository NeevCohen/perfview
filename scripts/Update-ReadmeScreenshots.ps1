param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$appDirectory = Join-Path $repository 'artifacts\readme-screenshots\app'
$resultsDirectory = Join-Path $repository 'artifacts\readme-screenshots\results'
& (Join-Path $repository 'build.ps1') -OutputDirectory $appDirectory
& (Join-Path $repository 'tests\Run-AppTests.ps1') -AppDirectory $appDirectory -ResultsDirectory $resultsDirectory -Filter 'FullyQualifiedName~ReadmeScreenshotTests.RenderCurrentWindowsWithTemperaturesEnabled'
$screenshots = Join-Path $repository 'docs\screenshots'
New-Item -ItemType Directory -Force -Path $screenshots | Out-Null
foreach ($name in @('taskbar-dark.png', 'details-dark.png', 'settings-dark.png')) {
    Copy-Item -LiteralPath (Join-Path $resultsDirectory $name) -Destination (Join-Path $screenshots $name) -Force
}
Write-Output "Updated README screenshots in $screenshots"
