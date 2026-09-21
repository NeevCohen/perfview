$ErrorActionPreference = 'Stop'

if ($env:GITHUB_REF -ne 'refs/heads/main') { throw 'Run this workflow from the main branch.' }
if ($env:RELEASE_VERSION -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*))?$') {
  throw 'A release version is required in major.minor or major.minor.patch format.'
}
foreach ($part in $env:RELEASE_VERSION.Split('.')) {
  if ([long]$part -gt 65534) { throw 'Version components must be at most 65534.' }
}
# Listing matching refs succeeds with [] when none exist, while
# authentication/network errors still fail the job.
$tag = "v$env:RELEASE_VERSION"
$refs = gh api "repos/$env:GH_REPO/git/matching-refs/tags/$tag" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not check existing tags.' }
if (@($refs | Where-Object { $_.ref -ceq "refs/tags/$tag" }).Count -gt 0) {
  throw "Tag $tag already exists. Choose a new version."
}
$releases = gh api --paginate "repos/$env:GH_REPO/releases" --jq '.[].tag_name'
if ($LASTEXITCODE -ne 0) { throw 'Could not check existing releases.' }
if (@($releases) -ccontains $tag) { throw "Release $tag already exists. Choose a new version." }
