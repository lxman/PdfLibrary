# Packs Lxman.PdfLibrary from local source into Pellucid's local NuGet feed for co-development,
# then pins Pellucid to that exact dev build. Edit library source -> run this -> 'dotnet build' Pellucid.
# No nuget.org publish, no GitHub Release, no manual version bumps, no 'dotnet restore --force':
# writing the exact version into Pellucid's Directory.Build.props.local forces a clean re-restore on
# Pellucid's next build (a floating "2.3.0-dev*" alone leaves restore on a stale prior dev build).
#
# Cross-platform partner of pack-local.sh — identical behavior. Paths are derived from the
# script's own location, so nothing is hardcoded. PellucidRoot defaults to the sibling ../Pellucid.
param(
    [string]$PellucidRoot = (Join-Path $PSScriptRoot ".." "Pellucid"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$csproj     = Join-Path $PSScriptRoot "PdfLibrary" "PdfLibrary.csproj"
$skiaCsproj = Join-Path $PSScriptRoot "PdfLibrary.Rendering.Skia" "PdfLibrary.Rendering.Skia.csproj"
$Feed       = Join-Path $PSScriptRoot "local-feed"

# Base versions come from each csproj so they never drift from what the packages publish as. Skia's
# csproj has two conditional <Version> lines (the SkiaPackVersion override); take the plain one.
$baseVersion = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version |
    ForEach-Object { if ($_ -is [string]) { $_ } else { $_.InnerText } } |
    Where-Object { $_ -and $_ -notmatch '\$' } | Select-Object -First 1
if (-not $baseVersion) { throw "could not read <Version> from $csproj" }
$skiaBaseVersion = ([xml](Get-Content $skiaCsproj)).Project.PropertyGroup.Version |
    ForEach-Object { if ($_ -is [string]) { $_ } else { $_.InnerText } } |
    Where-Object { $_ -and $_ -notmatch '\$' } | Select-Object -First 1
if (-not $skiaBaseVersion) { throw "could not read base <Version> from $skiaCsproj" }

# One timestamp shared by both packages so a single pack-local run is easy to correlate.
$ts      = Get-Date -Format "yyyyMMddHHmmss"
$ver     = "$baseVersion-dev$ts"
$skiaVer = "$skiaBaseVersion-dev$ts"

New-Item -ItemType Directory -Force -Path $Feed | Out-Null

Write-Host "Packing Lxman.PdfLibrary $ver -> $Feed" -ForegroundColor Cyan
dotnet pack $csproj -c $Configuration -p:PackageVersion=$ver -o $Feed
if ($LASTEXITCODE -ne 0) { throw "pack failed" }

# Rendering.Skia is dev-versioned via SkiaPackVersion — a property ONLY its csproj reads — so its
# content propagates to consumers instead of hiding behind a cached static 0.1.1. We must NOT use
# -p:PackageVersion here: that global property would also corrupt the "Lxman.PdfLibrary" dependency
# version in Skia's own nuspec (it packs core in the same invocation).
Write-Host "Packing Lxman.PdfLibrary.Rendering.Skia $skiaVer -> $Feed" -ForegroundColor Cyan
dotnet pack $skiaCsproj -c $Configuration -p:SkiaPackVersion=$skiaVer -o $Feed
if ($LASTEXITCODE -ne 0) { throw "skia pack failed" }

# Normalize PellucidRoot to an absolute path for clean file writes.
$PellucidRoot = (Resolve-Path $PellucidRoot).Path

# Pin Pellucid to these exact dev builds (gitignored override). Changed build props force Pellucid's
# next 'dotnet build' to re-restore deterministically — no --force, no cache clear.
$propsLocal = Join-Path $PellucidRoot "Directory.Build.props.local"
@"
<Project>
  <!-- Written by pack-local.ps1 — pins Pellucid to the latest local dev builds of the engine.
       Gitignored. Delete this file + nuget.config to return to the published packages. -->
  <PropertyGroup>
    <LxmanPdfLibraryVersion>$ver</LxmanPdfLibraryVersion>
    <LxmanPdfLibraryRenderingSkiaVersion>$skiaVer</LxmanPdfLibraryRenderingSkiaVersion>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path $propsLocal -Encoding UTF8

# Ensure Pellucid has a nuget.config wiring the local feed alongside nuget.org. Only created if
# missing, so an existing (hand-tuned) config is never clobbered.
$nugetConfig = Join-Path $PellucidRoot "nuget.config"
if (-not (Test-Path $nugetConfig)) {
@"
<?xml version="1.0" encoding="utf-8"?>
<!-- Written by pack-local.ps1. Gitignored. Adds the local co-development feed for the
     Lxman.PdfLibrary engine alongside nuget.org. Delete this file + Directory.Build.props.local
     to return to the published engine. -->
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="pdflibrary-local" value="$Feed" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $nugetConfig -Encoding UTF8
    Write-Host "Created $nugetConfig (pdflibrary-local -> $Feed)" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Pinned Pellucid to core $ver + Rendering.Skia $skiaVer. Just 'dotnet build' Pellucid — it re-restores the new builds." -ForegroundColor Green
