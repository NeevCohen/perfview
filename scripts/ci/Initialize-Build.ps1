$ErrorActionPreference = 'Stop'

@{ sdk = @{ version = $env:SDK_VERSION; rollForward = 'disable' } } |
  ConvertTo-Json | Set-Content -LiteralPath global.json -Encoding utf8

$path = Join-Path $env:GITHUB_WORKSPACE 'src\AssemblyInfo.cs'
$source = Get-Content -LiteralPath $path -Raw
$pattern = '\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]'
$attributes = [regex]::Matches($source, $pattern)
if ($attributes.Count -ne 1) {
  throw 'Expected exactly one AssemblyVersion attribute in src/AssemblyInfo.cs.'
}
if ($env:GITHUB_EVENT_NAME -eq 'push') {
  # Push CI uses the checked-in app version and does not stamp source.
  $env:RELEASE_VERSION = $attributes[0].Groups[1].Value
  if ($env:RELEASE_VERSION -cnotmatch '^[0-9]+(\.[0-9]+){1,3}$') {
    throw 'AssemblyVersion must contain two to four numeric components.'
  }
}
elseif ($env:RELEASE_VERSION -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*))?$') {
  throw 'A release version is required in major.minor or major.minor.patch format.'
}
$parts = @($env:RELEASE_VERSION.Split('.') | ForEach-Object {
  if ([long]$_ -gt 65534) { throw 'Version components must be at most 65534.' }
  [int]$_
})
while ($parts.Count -lt 4) { $parts += 0 }
$assemblyVersion = $parts -join '.'
if ($env:GITHUB_EVENT_NAME -ne 'push') {
  $source = [regex]::Replace($source, $pattern, ('[assembly: AssemblyVersion("{0}")]' -f $assemblyVersion))
  Set-Content -LiteralPath $path -Value $source -Encoding utf8 -NoNewline
}
"RELEASE_VERSION=$env:RELEASE_VERSION" >> $env:GITHUB_ENV
"ASSEMBLY_VERSION=$assemblyVersion" >> $env:GITHUB_ENV
"artifact-name=Perfview-$env:RELEASE_VERSION-windows" >> $env:GITHUB_OUTPUT
