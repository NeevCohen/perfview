$ErrorActionPreference = 'Stop'

dotnet restore Perfview.csproj --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Application restore failed.' }
dotnet restore tests/Perfview.Tests.csproj --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
