# api-surface

Reflection dump and diff of the exported public API of `PdfLibrary.dll`. Used as the release gate
(see `Docs/Releasing.md`): every release must be a superset of the previous published package.

    pwsh tools/api-surface/diff.ps1                       # 2.5.2 package (NuGet cache) vs bin/Release/net8.0
    pwsh tools/api-surface/diff.ps1 -OldDll <a> -NewDll <b>

`dump.ps1` runs once per assembly in its own process because two builds of `PdfLibrary.dll` cannot
share a load context. Output lands in `tools/api-surface/out/` (gitignored). Types forwarded to
`PdfLibrary.Xmp.dll` (`XmpPacket`, `XmpProperty`, `XmpSchemas`, `XmpValueKind`) show as REMOVED
from `PdfLibrary.dll` alone; they are moves, not breaks, and remain the only accepted removals in 2.x.
