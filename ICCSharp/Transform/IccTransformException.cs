using System;

namespace ICCSharp.Transform;

/// <summary>Thrown when an <see cref="IccTwoProfileTransform"/> cannot be built — e.g. neither
/// profile carries usable tags for the requested rendering intent, or PCS combinations are
/// unsupported by the current implementation.</summary>
public sealed class IccTransformException : Exception
{
    public IccTransformException(string message) : base(message) { }
    public IccTransformException(string message, Exception inner) : base(message, inner) { }
}
