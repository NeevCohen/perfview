#!/usr/bin/env bash
set -euo pipefail
cd release
app="Perfview-${RELEASE_VERSION}-Windows-Portable.zip"
installer="Perfview-${RELEASE_VERSION}-Setup.exe"
test -s "$app"
test -s "$installer"
sha256sum --check "$app.sha256" "$installer.sha256"
tag="v${RELEASE_VERSION}"
# Atomically create a new tag at the tested commit; this fails if
# another release has claimed the tag since validation.
gh api --method POST "repos/${GH_REPO}/git/refs" \
  -f "ref=refs/tags/${tag}" -f "sha=${GITHUB_SHA}" > /dev/null
# Upload while still a draft, so a failed upload does not expose a
# published release with missing assets. A leftover draft is retained.
gh release create "$tag" "$app" "$app.sha256" "$installer" "$installer.sha256" \
  --verify-tag --title "Perfview $RELEASE_VERSION" --generate-notes --draft
gh release edit "$tag" --draft=false
