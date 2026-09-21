$ErrorActionPreference = 'Stop'

# The legacy app project has no lock file; the NUnit project does.
dotnet restore Perfview.csproj -p:RestoreLockedMode=false
if ($LASTEXITCODE -ne 0) { throw 'Application restore failed.' }
dotnet restore tests/Perfview.Tests.csproj --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
