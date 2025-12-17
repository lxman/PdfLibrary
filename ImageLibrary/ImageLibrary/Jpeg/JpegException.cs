using System;

namespace ImageLibrary.Jpeg;

/// <summary>
/// Exception thrown when JPEG decoding fails.
/// </summary>
public class JpegException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JpegException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public JpegException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JpegException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public JpegException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
