using System;

namespace PbmCodec;

/// <summary>
/// Exception thrown when PBM/PGM/PPM encoding or decoding fails.
/// </summary>
public class PbmException : Exception
{
    public PbmException(string message) : base(message)
    {
    }

    public PbmException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
