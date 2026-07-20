#!/usr/bin/env bash
# Packs Lxman.PdfLibrary from local source into Pellucid's local NuGet feed for co-development,
# then pins Pellucid to that exact dev build. Edit library source -> run this -> 'dotnet build' Pellucid.
# No nuget.org publish, no GitHub Release, no manual version bumps, no 'dotnet restore --force':
# writing the exact version into Pellucid's Directory.Build.props.local forces a clean re-restore on
# Pellucid's next build (a floating "2.3.0-dev*" alone leaves restore on a stale prior dev build).
#
# Cross-platform partner of pack-local.ps1 — identical behavior. Paths are derived from the
# script's own location, so nothing is hardcoded. Override Pellucid's location or the build config
# with the two optional args below.
#
#   Usage: ./pack-local.sh [PELLUCID_ROOT] [CONFIGURATION]
#     PELLUCID_ROOT     default: sibling ../Pellucid relative to this repo
#     CONFIGURATION  default: Release
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PELLUCID_ROOT="${1:-$(cd "$SCRIPT_DIR/../Pellucid" && pwd)}"
CONFIGURATION="${2:-Release}"

CSPROJ="$SCRIPT_DIR/PdfLibrary/PdfLibrary.csproj"
FEED="$SCRIPT_DIR/local-feed"

# The nuget.config value must be a native path. Under Git Bash on Windows $FEED is an MSYS path
# (/c/Users/...) which NuGet reads as RELATIVE (-> C:\c\Users\...) and fails to restore, so convert
# to a Windows path with cygpath. On Linux/macOS (no cygpath) the MSYS form IS native — use it as-is.
if command -v cygpath >/dev/null 2>&1; then
  FEED_FOR_CONFIG="$(cygpath -w "$FEED")"
else
  FEED_FOR_CONFIG="$FEED"
fi

# One timestamp shared by both packages so a single pack-local run is easy to correlate.
STAMP="$(date +%Y%m%d%H%M%S)"

# Base versions come from each csproj so they never drift from what the packages publish as.
BASE_VERSION="$(grep -oE '<Version>[^<]+</Version>' "$CSPROJ" | head -1 | sed -E 's|</?Version>||g')"
if [[ -z "$BASE_VERSION" ]]; then
  echo "ERROR: could not read <Version> from $CSPROJ" >&2
  exit 1
fi
VER="${BASE_VERSION}-dev${STAMP}"

mkdir -p "$FEED"

echo "Packing Lxman.PdfLibrary $VER -> $FEED"
dotnet pack "$CSPROJ" -c "$CONFIGURATION" -p:PackageVersion="$VER" -o "$FEED"

# Pin Pellucid to these exact dev builds (gitignored override). Changed build props force Pellucid's
# next 'dotnet build' to re-restore deterministically — no --force, no cache clear.
cat > "$PELLUCID_ROOT/Directory.Build.props.local" <<EOF
<Project>
  <!-- Written by pack-local.sh — pins Pellucid to the latest local dev builds of the engine.
       Gitignored. Delete this file + nuget.config to return to the published packages. -->
  <PropertyGroup>
    <LxmanPdfLibraryVersion>$VER</LxmanPdfLibraryVersion>
  </PropertyGroup>
</Project>
EOF

# Ensure Pellucid has a nuget.config wiring the local feed alongside nuget.org. Only created if
# missing, so an existing (hand-tuned) config is never clobbered.
NUGET_CONFIG="$PELLUCID_ROOT/nuget.config"
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
    <add key="pdflibrary-local" value="$FEED_FOR_CONFIG" />
  </packageSources>
</configuration>
EOF
  echo "Created $NUGET_CONFIG (pdflibrary-local -> $FEED_FOR_CONFIG)"
fi

echo ""
echo "Pinned Pellucid to core $VER. Just 'dotnet build' Pellucid — it re-restores the new build."
