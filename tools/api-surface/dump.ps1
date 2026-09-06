param([Parameter(Mandatory)][string]$Dll, [Parameter(Mandatory)][string]$OutFile)
# Writes one sorted line per exported type and public declared member. One assembly per process: two
# builds of PdfLibrary.dll cannot share a load context, so diff.ps1 runs this twice.
$ErrorActionPreference = 'Stop'
$asm = [System.Reflection.Assembly]::LoadFrom($Dll)   # LoadFrom resolves siblings (FontParser, ICCSharp, Xmp) from the same directory
try { $types = $asm.GetExportedTypes() }
catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ -ne $null -and $_.IsPublic } }
$lines = New-Object System.Collections.Generic.List[string]
$flags = [System.Reflection.BindingFlags]'Public,Instance,Static,DeclaredOnly'
foreach ($t in $types) {
    if ($t.IsNested -and -not $t.IsNestedPublic) { continue }
    $kind = if ($t.IsEnum) { 'enum' } elseif ($t.IsInterface) { 'interface' } elseif ($t.IsValueType) { 'struct' } else { 'class' }
    $lines.Add("T " + $t.FullName + " :: " + $kind)
    if ($t.IsEnum) { foreach ($n in [Enum]::GetNames($t)) { $lines.Add("E " + $t.FullName + "." + $n) }; continue }
    foreach ($m in $t.GetMembers($flags)) {
        switch ($m.MemberType) {
            'Method'      { if ($m.IsSpecialName) { continue }; $ps = ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ','; $lines.Add("M " + $t.FullName + "." + $m.Name + "(" + $ps + ")") }
            'Constructor' { $ps = ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ','; $lines.Add("C " + $t.FullName + ".ctor(" + $ps + ")") }
            'Property'    { $lines.Add("P " + $t.FullName + "." + $m.Name) }
            'Field'       { $lines.Add("F " + $t.FullName + "." + $m.Name) }
            'Event'       { $lines.Add("V " + $t.FullName + "." + $m.Name) }
        }
    }
}
$lines | Sort-Object -Unique | Set-Content $OutFile
"$($lines.Count) entries -> $OutFile"
