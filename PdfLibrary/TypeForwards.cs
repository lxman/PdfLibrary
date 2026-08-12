using System.Runtime.CompilerServices;
using PdfLibrary.Metadata;

// These four types were public in Lxman.PdfLibrary 2.5.2 and shipped inside PdfLibrary.dll. The XMP
// format layer moved to the PdfLibrary.Xmp assembly on 2026-08-12; the forwarders keep every binary
// compiled against 2.5.2 working. Source is unaffected either way — the namespace did not change.
//
// TRANSITIONAL. These exist only to avoid a binary break within the 2.x line. Remove them at the next
// MAJOR version bump (3.0.0), together with XmpTypeForwardingTests, and note the removal in
// CHANGELOG.md as a breaking change so consumers know a recompile is required.
[assembly: TypeForwardedTo(typeof(XmpPacket))]
[assembly: TypeForwardedTo(typeof(XmpProperty))]
[assembly: TypeForwardedTo(typeof(XmpSchemas))]
[assembly: TypeForwardedTo(typeof(XmpValueKind))]
