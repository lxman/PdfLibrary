using System;

namespace ImageLibrary.Tiff;

/// <summary>
/// Exception thrown when an error occurs during TIFF encoding or decoding.
/// </summary>
public sealed class TiffException : Exception
{
    /// <summary>
    /// Creates a new TIFF exception with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TiffException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new TIFF exception with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public TiffException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
