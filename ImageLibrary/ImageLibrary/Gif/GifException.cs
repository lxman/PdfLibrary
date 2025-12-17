using System;

namespace ImageLibrary.Gif;

/// <summary>
/// Exception thrown when GIF decoding or encoding fails.
/// </summary>
public class GifException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GifException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GifException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="GifException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public GifException(string message, Exception innerException) : base(message, innerException) { }
}
