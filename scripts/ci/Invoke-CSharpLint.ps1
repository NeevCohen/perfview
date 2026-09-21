$ErrorActionPreference = 'Stop'

foreach ($project in @('Perfview.csproj', 'tests/Perfview.Tests.csproj')) {
  dotnet format style $project --verify-no-changes --no-restore --severity warn
  if ($LASTEXITCODE -ne 0) { throw "C# style lint failed: $project" }
  dotnet format analyzers $project --verify-no-changes --no-restore --severity warn
  if ($LASTEXITCODE -ne 0) { throw "C# analyzer lint failed: $project" }
}
