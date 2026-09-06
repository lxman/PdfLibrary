param(
    [string]$OldDll = "$env:USERPROFILE\.nuget\packages\lxman.pdflibrary\2.5.2\lib\net8.0\PdfLibrary.dll",
    [string]$NewDll = "$PSScriptRoot\..\..\PdfLibrary\bin\Release\net8.0\PdfLibrary.dll",
    [string]$OutDir = "$PSScriptRoot\out"
)
# Release gate (Docs/Releasing.md): the new surface must be a superset of the previous published one.
# Prints REMOVED and ADDED entries; writes the four lists to $OutDir (gitignored).
$ErrorActionPreference = 'Stop'
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
