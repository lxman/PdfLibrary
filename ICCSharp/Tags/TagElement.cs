using ICCSharp.IO;

namespace ICCSharp.Tags;

/// <summary>
/// Base class for parsed ICC tag element types (ICC.1:2010 §10).
/// Every concrete type carries the four-byte type signature it parsed itself from.
/// </summary>
public abstract class TagElement
{
    public IccSignature TypeSignature { get; }

    protected TagElement(IccSignature typeSignature)
    {
        TypeSignature = typeSignature;
    }
}
