// Polyfill required for C# 9+ init accessors when targeting netstandard2.1.
// Roslyn looks up this type by name; an internal stub here is sufficient.

namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
