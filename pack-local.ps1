# Packs Lxman.PdfLibrary from local source into Focal's local NuGet feed for co-development,
# then pins Focal to that exact dev build. Edit library source -> run this -> 'dotnet build' Focal.
# No nuget.org publish, no GitHub Release, no manual version bumps, no 'dotnet restore --force':
# writing the exact version into Focal's Directory.Build.props.local forces a clean re-restore on
# Focal's next build (floating "2.2.0-dev*" alone leaves restore on a stale prior dev build).
param(
    [string]$FocalRoot = "C:\Users\jorda\RiderProjects\Focal",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
# Focal's nuget.config points "pdflibrary-local" at ..\PDF\local-feed — pack into THAT feed
# (a Focal-side .nuget\local-feed exists from an older iteration but is not a configured source).
$Feed = Join-Path $PSScriptRoot "local-feed"
$ts   = Get-Date -Format "yyyyMMddHHmmss"
$ver  = "2.3.0-dev$ts"

New-Item -ItemType Directory -Force -Path $Feed | Out-Null

Write-Host "Packing Lxman.PdfLibrary $ver -> $Feed" -ForegroundColor Cyan
dotnet pack "$PSScriptRoot\PdfLibrary\PdfLibrary.csproj" -c $Configuration -p:PackageVersion=$ver -o $Feed
if ($LASTEXITCODE -ne 0) { throw "pack failed" }

# Pin Focal to this exact dev build (gitignored override). A changed build prop forces Focal's
# next 'dotnet build' to re-restore deterministically — no --force needed.
$propsLocal = Join-Path $FocalRoot "Directory.Build.props.local"
@"
<Project>
  <!-- Written by pack-local.ps1 — pins Focal to the latest local dev build of the engine.
       Gitignored. Delete this file + nuget.config to return to the published package. -->
  <PropertyGroup>
    <LxmanPdfLibraryVersion>$ver</LxmanPdfLibraryVersion>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path $propsLocal -Encoding UTF8

Write-Host ""
Write-Host "Pinned Focal to $ver. Just 'dotnet build' Focal — it re-restores the new build." -ForegroundColor Green
