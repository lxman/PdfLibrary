using System.Security.Cryptography;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Security;

/// <summary>
/// Handles PDF document decryption according to ISO 32000-1:2008 section 7.6.
/// Supports Standard security handler with RC4 encryption (V=1,2,3 R=2,3,4).
/// </summary>
public class PdfDecryptor
{
    // Padding string used in PDF encryption (Table 21 in ISO 32000-1)
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];

    private readonly byte[] _encryptionKey;
    private readonly int _keyLength;
    private readonly int _version;
    private readonly int _revision;
    private readonly byte[] _documentId;

    /// <summary>
    /// Gets whether the document was successfully decrypted.
    /// </summary>
    public bool IsDecrypted { get; }

    /// <summary>
    /// Creates a decryptor for an encrypted PDF document.
    /// </summary>
    /// <param name="encryptDict">The /Encrypt dictionary</param>
    /// <param name="documentId">First element of the /ID array from trailer</param>
    /// <param name="password">User or owner password (empty string for no password)</param>
    public PdfDecryptor(PdfDictionary encryptDict, byte[] documentId, string password = "")
    {
        ArgumentNullException.ThrowIfNull(encryptDict);
        ArgumentNullException.ThrowIfNull(documentId);

        _documentId = documentId;

        // Extract encryption parameters
        _version = GetIntValue(encryptDict, "V", 0);
        _revision = GetIntValue(encryptDict, "R", 2);
        _keyLength = GetIntValue(encryptDict, "Length", 40) / 8; // Convert bits to bytes

        // For V=1, key length is always 40 bits (5 bytes)
        if (_version == 1)
            _keyLength = 5;

        byte[] oValue = GetBytesValue(encryptDict, "O") ?? throw new PdfSecurityException("Missing /O value in encryption dictionary");
        byte[] uValue = GetBytesValue(encryptDict, "U") ?? throw new PdfSecurityException("Missing /U value in encryption dictionary");
        int permissions = GetIntValue(encryptDict, "P", 0);

        // Try to authenticate with the provided password
        _encryptionKey = ComputeEncryptionKey(password, oValue, permissions);

        // Verify the password by checking against /U value
        IsDecrypted = VerifyUserPassword(_encryptionKey, uValue, permissions);

        if (!IsDecrypted)
        {
            // Try empty password if the provided one failed
            if (!string.IsNullOrEmpty(password))
            {
                _encryptionKey = ComputeEncryptionKey("", oValue, permissions);
                IsDecrypted = VerifyUserPassword(_encryptionKey, uValue, permissions);
            }
        }

        if (!IsDecrypted)
        {
            throw new PdfSecurityException("Invalid password or unsupported encryption");
        }
    }

    /// <summary>
    /// Decrypts a string or stream for the given object.
    /// </summary>
    /// <param name="data">Encrypted data</param>
    /// <param name="objectNumber">Object number</param>
    /// <param name="generationNumber">Generation number</param>
    /// <returns>Decrypted data</returns>
    public byte[] Decrypt(byte[] data, int objectNumber, int generationNumber)
    {
        // Compute object-specific key (Algorithm 1 in ISO 32000-1 section 7.6.2)
        byte[] objectKey = ComputeObjectKey(objectNumber, generationNumber);

        // Decrypt using RC4
        var rc4 = new RC4(objectKey);
        return rc4.ProcessCopy(data);
    }

    /// <summary>
    /// Computes the object-specific encryption key.
    /// ISO 32000-1 Algorithm 1 (section 7.6.2)
    /// </summary>
    private byte[] ComputeObjectKey(int objectNumber, int generationNumber)
    {
        // Concatenate encryption key with object and generation numbers
        int keyInputLength = _encryptionKey.Length + 5;
        byte[] keyInput = new byte[keyInputLength];

        Array.Copy(_encryptionKey, 0, keyInput, 0, _encryptionKey.Length);
        keyInput[_encryptionKey.Length] = (byte)(objectNumber & 0xFF);
        keyInput[_encryptionKey.Length + 1] = (byte)((objectNumber >> 8) & 0xFF);
        keyInput[_encryptionKey.Length + 2] = (byte)((objectNumber >> 16) & 0xFF);
        keyInput[_encryptionKey.Length + 3] = (byte)(generationNumber & 0xFF);
        keyInput[_encryptionKey.Length + 4] = (byte)((generationNumber >> 8) & 0xFF);

        // MD5 hash
        byte[] hash = MD5.HashData(keyInput);

        // Key length is min(n+5, 16) bytes
        int objectKeyLength = Math.Min(_keyLength + 5, 16);
        byte[] objectKey = new byte[objectKeyLength];
        Array.Copy(hash, objectKey, objectKeyLength);

        return objectKey;
    }

    /// <summary>
    /// Computes the file encryption key from the password.
    /// ISO 32000-1 Algorithm 2 (section 7.6.3.3)
    /// </summary>
    private byte[] ComputeEncryptionKey(string password, byte[] oValue, int permissions)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        // Step 1: Pad or truncate password to 32 bytes
        byte[] paddedPassword = PadPassword(password);
        md5.AppendData(paddedPassword);

        // Step 2: Pass O value
        md5.AppendData(oValue);

        // Step 3: Pass P value as 4-byte little-endian
        byte[] pBytes = [(byte)permissions, (byte)(permissions >> 8), (byte)(permissions >> 16), (byte)(permissions >> 24)];
        md5.AppendData(pBytes);

        // Step 4: Pass document ID
        md5.AppendData(_documentId);

        // Step 5: (Revision 4 only) If metadata is not encrypted, pass 0xFFFFFFFF
        // We'll skip this for now as most PDFs don't use this

        byte[] hash = md5.GetHashAndReset();

        // Step 6: (Revision 3+) Do 50 additional MD5 iterations
        if (_revision >= 3)
        {
            for (int i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash.AsSpan(0, _keyLength));
            }
        }

        // Return first n bytes as encryption key
        byte[] key = new byte[_keyLength];
        Array.Copy(hash, key, _keyLength);
        return key;
    }

    /// <summary>
    /// Verifies the user password against the /U value.
    /// ISO 32000-1 Algorithm 4/5 (section 7.6.3.4)
    /// </summary>
    private bool VerifyUserPassword(byte[] encryptionKey, byte[] uValue, int permissions)
    {
        byte[] computedU;

        if (_revision == 2)
        {
            // Algorithm 4: Encrypt padding string with RC4
            var rc4 = new RC4(encryptionKey);
            computedU = rc4.ProcessCopy(PasswordPadding);
        }
        else // Revision 3+
        {
            // Algorithm 5: MD5 of padding + document ID, then RC4 iterations
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            md5.AppendData(PasswordPadding);
            md5.AppendData(_documentId);
            computedU = md5.GetHashAndReset();

            // 20 RC4 iterations with modified keys
            for (int i = 0; i < 20; i++)
            {
                byte[] iterKey = new byte[encryptionKey.Length];
                for (int j = 0; j < encryptionKey.Length; j++)
                {
                    iterKey[j] = (byte)(encryptionKey[j] ^ i);
                }
                var rc4 = new RC4(iterKey);
                rc4.Process(computedU);
            }
        }

        // Compare (first 16 bytes for revision 3+)
        int compareLength = _revision == 2 ? 32 : 16;
        for (int i = 0; i < compareLength && i < uValue.Length && i < computedU.Length; i++)
        {
            if (uValue[i] != computedU[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Pads or truncates a password to exactly 32 bytes.
    /// </summary>
    private static byte[] PadPassword(string password)
    {
        byte[] result = new byte[32];
        byte[] passwordBytes = Encoding.Latin1.GetBytes(password ?? "");

        int copyLength = Math.Min(passwordBytes.Length, 32);
        Array.Copy(passwordBytes, result, copyLength);

        if (copyLength < 32)
        {
            Array.Copy(PasswordPadding, 0, result, copyLength, 32 - copyLength);
        }

        return result;
    }

    private static int GetIntValue(PdfDictionary dict, string key, int defaultValue)
    {
        if (dict.TryGetValue(new PdfName(key), out PdfObject? obj) && obj is PdfInteger intVal)
            return intVal.Value;
        return defaultValue;
    }

    private static byte[]? GetBytesValue(PdfDictionary dict, string key)
    {
        if (dict.TryGetValue(new PdfName(key), out PdfObject? obj) && obj is PdfString strVal)
            return strVal.Bytes;
        return null;
    }
}

/// <summary>
/// Exception thrown when PDF security operations fail.
/// </summary>
public class PdfSecurityException : Exception
{
    public PdfSecurityException(string message) : base(message) { }
    public PdfSecurityException(string message, Exception inner) : base(message, inner) { }
}
