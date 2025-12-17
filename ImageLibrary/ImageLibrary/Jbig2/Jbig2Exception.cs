using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// Base exception for all JBIG2 decoding errors.
/// </summary>
public class Jbig2Exception : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Jbig2Exception"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public Jbig2Exception(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Jbig2Exception"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public Jbig2Exception(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when JBIG2 data is malformed or invalid.
/// </summary>
public class Jbig2DataException : Jbig2Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Jbig2DataException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public Jbig2DataException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Jbig2DataException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public Jbig2DataException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when a JBIG2 feature is not supported by this decoder.
/// </summary>
public class Jbig2UnsupportedException : Jbig2Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Jbig2UnsupportedException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public Jbig2UnsupportedException(string message) : base(message) { }
}

/// <summary>
/// Thrown when resource limits are exceeded during decoding.
/// </summary>
public class Jbig2ResourceException : Jbig2Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Jbig2ResourceException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public Jbig2ResourceException(string message) : base(message) { }
}
