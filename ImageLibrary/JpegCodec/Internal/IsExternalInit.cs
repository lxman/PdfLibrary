// Polyfill required for C# 9+ init accessors when targeting netstandard2.1.
// Roslyn looks up this type by name; an internal stub here is sufficient.

namespace JpegCodec.Internal;

internal static class IsExternalInit { }
