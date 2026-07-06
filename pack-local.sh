#!/usr/bin/env bash
# Packs Lxman.PdfLibrary from local source into Focal's local NuGet feed for co-development,
# then pins Focal to that exact dev build. Edit library source -> run this -> 'dotnet build' Focal.
# No nuget.org publish, no GitHub Release, no manual version bumps, no 'dotnet restore --force':
# writing the exact version into Focal's Directory.Build.props.local forces a clean re-restore on
# Focal's next build (a floating "2.3.0-dev*" alone leaves restore on a stale prior dev build).
#
# Cross-platform partner of pack-local.ps1 — identical behavior. Paths are derived from the
# script's own location, so nothing is hardcoded. Override Focal's location or the build config
# with the two optional args below.
#
#   Usage: ./pack-local.sh [FOCAL_ROOT] [CONFIGURATION]
#     FOCAL_ROOT     default: sibling ../Focal relative to this repo
#     CONFIGURATION  default: Release
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FOCAL_ROOT="${1:-$(cd "$SCRIPT_DIR/../Focal" && pwd)}"
CONFIGURATION="${2:-Release}"

CSPROJ="$SCRIPT_DIR/PdfLibrary/PdfLibrary.csproj"
FEED="$SCRIPT_DIR/local-feed"

# Base version comes from the library csproj so it never drifts from what the package will publish as.
BASE_VERSION="$(grep -oE '<Version>[^<]+</Version>' "$CSPROJ" | head -1 | sed -E 's|</?Version>||g')"
if [[ -z "$BASE_VERSION" ]]; then
  echo "ERROR: could not read <Version> from $CSPROJ" >&2
  exit 1
fi
VER="${BASE_VERSION}-dev$(date +%Y%m%d%H%M%S)"

mkdir -p "$FEED"

echo "Packing Lxman.PdfLibrary $VER -> $FEED"
dotnet pack "$CSPROJ" -c "$CONFIGURATION" -p:PackageVersion="$VER" -o "$FEED"

# Pin Focal to this exact dev build (gitignored override). A changed build prop forces Focal's
# next 'dotnet build' to re-restore deterministically — no --force needed.
cat > "$FOCAL_ROOT/Directory.Build.props.local" <<EOF
<Project>
  <!-- Written by pack-local.sh — pins Focal to the latest local dev build of the engine.
       Gitignored. Delete this file + nuget.config to return to the published package. -->
  <PropertyGroup>
    <LxmanPdfLibraryVersion>$VER</LxmanPdfLibraryVersion>
  </PropertyGroup>
</Project>
EOF

# Ensure Focal has a nuget.config wiring the local feed alongside nuget.org. Only created if
# missing, so an existing (hand-tuned) config is never clobbered.
NUGET_CONFIG="$FOCAL_ROOT/nuget.config"
if [[ ! -f "$NUGET_CONFIG" ]]; then
  cat > "$NUGET_CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<!-- Written by pack-local.sh. Gitignored. Adds the local co-development feed for the
     Lxman.PdfLibrary engine alongside nuget.org. Delete this file + Directory.Build.props.local
     to return to the published engine. -->
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="pdflibrary-local" value="$FEED" />
  </packageSources>
</configuration>
EOF
  echo "Created $NUGET_CONFIG (pdflibrary-local -> $FEED)"
fi

echo ""
echo "Pinned Focal to $VER. Just 'dotnet build' Focal — it re-restores the new build."
