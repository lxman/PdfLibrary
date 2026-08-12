// netstandard2.1 lacks IsExternalInit; supplying a stub enables `record` and `init` syntax.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
