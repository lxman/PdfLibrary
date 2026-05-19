using System;

namespace ICCSharp.Profile;

/// <summary>Thrown when an ICC profile fails structural validation during parsing.</summary>
public sealed class IccParseException : Exception
{
    public IccParseException(string message) : base(message) { }
    public IccParseException(string message, Exception inner) : base(message, inner) { }
}
