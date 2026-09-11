param(
    [string]$OldDll,
    [string]$NewDll = "$PSScriptRoot\..\..\PdfLibrary\bin\Release\net8.0\PdfLibrary.dll",
    [string]$OutDir = "$PSScriptRoot\out"
)
# Release gate (Docs/Releasing.md): the new surface must be a superset of the previous published one.
# Prints REMOVED and ADDED entries; writes the four lists to $OutDir (gitignored).
$ErrorActionPreference = 'Stop'

# The baseline used to be a hard-coded version, which went stale and made the gate fail with a
# missing-assembly error instead of a verdict. Resolve the newest stable package in the NuGet cache
# instead: that is by definition the release this build has to stay compatible with. A stale
# baseline that still loads would be worse than a crash -- it would compare against the wrong
# surface and report additions that shipped releases ago.
if (-not $OldDll) {
    $pkgRoot = "$HOME\.nuget\packages\lxman.pdflibrary"
    if (-not (Test-Path $pkgRoot)) {
        throw "No cached Lxman.PdfLibrary to diff against. Restore a project that references the " +
              "previous release, or pass -OldDll explicitly."
    }
    $baseline = Get-ChildItem $pkgRoot -Directory |
        Where-Object { $_.Name -notmatch '-' } |
        Sort-Object { [version]$_.Name } |
        Select-Object -Last 1
    if (-not $baseline) { throw "Only prerelease packages are cached under $pkgRoot; pass -OldDll." }
    $OldDll = Join-Path $baseline.FullName 'lib\net8.0\PdfLibrary.dll'
    "baseline: $($baseline.Name)"
}
New-Item -ItemType Directory -Force $OutDir | Out-Null
& pwsh -NoProfile -File "$PSScriptRoot\dump.ps1" -Dll $OldDll -OutFile "$OutDir\api-old.txt"
& pwsh -NoProfile -File "$PSScriptRoot\dump.ps1" -Dll $NewDll -OutFile "$OutDir\api-new.txt"
$o = Get-Content "$OutDir\api-old.txt"; $n = Get-Content "$OutDir\api-new.txt"
$cmp = Compare-Object $o $n
$removed = @($cmp | Where-Object SideIndicator -eq '<=' | ForEach-Object InputObject)
$added   = @($cmp | Where-Object SideIndicator -eq '=>' | ForEach-Object InputObject)
$removed | Set-Content "$OutDir\api-removed.txt"; $added | Set-Content "$OutDir\api-added.txt"
"old: $($o.Count)  new: $($n.Count)  REMOVED: $($removed.Count)  ADDED: $($added.Count)"
"--- REMOVED ---"; $removed | ForEach-Object { "  $_" }
"--- ADDED, grouped by type ---"
$added | ForEach-Object { $s = ($_ -split ' ',2)[1]; if ($_.StartsWith('T ')) { ($s -split ' :: ')[0] } else { $s -replace '\.[^.(]+(\(.*\))?$','' } } |
    Group-Object | Sort-Object Count -Descending | ForEach-Object { "{0,4}  {1}" -f $_.Count, $_.Name }
