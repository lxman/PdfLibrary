using System;

namespace ICCSharp.IO;

/// <summary>
/// Thrown when an ICC reader is asked for more bytes than remain in the buffer.
/// Carries the requested vs. available counts so callers can localize the failure
/// to a specific tag or header field.
/// </summary>
public sealed class IccEndOfStreamException : Exception
{
    public int Requested { get; }
    public int Available { get; }

    public IccEndOfStreamException(int requested, int available)
        : base($"Requested {requested} bytes but only {available} remain.")
    {
        Requested = requested;
        Available = available;
    }
}
