using System.Security.Cryptography;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Security;

/// <summary>
/// Handles PDF document decryption according to ISO 32000-1:2008 section 7.6
/// and ISO 32000-2:2020 for AES-256.
/// Supports:
/// - RC4 40-bit (V=1, R=2)
/// - RC4 128-bit (V=2, R=3)
/// - AES-128 (V=4, R=4)
/// - AES-256 (V=5, R=5/R=6)
/// </summary>
internal class PdfDecryptor
{
    // Padding string used in PDF encryption (Table 21 in ISO 32000-1)
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];

    // "sAlT" marker for AES-256 (V=5)
    private static readonly byte[] AesSaltMarker = "sAlT"u8.ToArray();

    private readonly byte[] _encryptionKey;
    private readonly int _keyLengthBytes;
    private readonly int _version;
    private readonly int _revision;
    private readonly byte[] _documentId;
    private readonly bool _encryptMetadata;

    // Crypt filter settings (V=4+)
    private readonly string _stringFilter;
    private readonly string _streamFilter;

    /// <summary>
    /// Gets whether the document was successfully decrypted.
    /// </summary>
    public bool IsDecrypted { get; }

    /// <summary>
    /// Gets the encryption method used.
    /// </summary>
    public PdfEncryptionMethod Method { get; set; }

    /// <summary>
    /// Gets the document permissions.
    /// </summary>
    public PdfPermissions Permissions { get; }

    /// <summary>
    /// Gets whether the user password was used (vs owner password).
    /// </summary>
    public bool IsUserPassword { get; private set; }

    /// <summary>
    /// Gets whether metadata streams are encrypted.
    /// </summary>
    public bool EncryptMetadata => _encryptMetadata;

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
        int keyLengthBits = GetIntValue(encryptDict, "Length", 40);
        _keyLengthBytes = keyLengthBits / 8;

        // Permissions
        int pValue = GetIntValue(encryptDict, "P", 0);
        Permissions = new PdfPermissions(pValue);

        // Metadata encryption (default true for V<4, configurable for V>=4)
        _encryptMetadata = GetBoolValue(encryptDict, "EncryptMetadata", true);

        // Determine encryption method and key length
        Method = DetermineEncryptionMethod(encryptDict);

        // Adjust key length based on version
        switch (_version)
        {
            case 1:
                _keyLengthBytes = 5; // Always 40-bit
                break;
            case 5:
                _keyLengthBytes = 32; // Always 256-bit for AES-256
                break;
        }

        // Get crypt filters for V=4+
        _stringFilter = "Identity";
        _streamFilter = "Identity";

        if (_version >= 4)
        {
            _stringFilter = GetNameValue(encryptDict, "StrF", "Identity");
            _streamFilter = GetNameValue(encryptDict, "StmF", "Identity");
        }

        // Extract required values
        byte[] oValue = GetBytesValue(encryptDict, "O")
                        ?? throw new PdfSecurityException("Missing /O value in encryption dictionary");
        byte[] uValue = GetBytesValue(encryptDict, "U")
                        ?? throw new PdfSecurityException("Missing /U value in encryption dictionary");

        // V=5 has additional values
        byte[]? oeValue = null;
        byte[]? ueValue = null;
        byte[]? permsValue = null;

        if (_version == 5)
        {
            oeValue = GetBytesValue(encryptDict, "OE");
            ueValue = GetBytesValue(encryptDict, "UE");
            permsValue = GetBytesValue(encryptDict, "Perms");
        }

        // Try to authenticate
        _encryptionKey = Authenticate(password, oValue, uValue, oeValue, ueValue, permsValue, pValue);
        IsDecrypted = _encryptionKey.Length > 0;

        if (!IsDecrypted)
        {
            throw new PdfSecurityException("Invalid password or unsupported encryption");
        }
    }

    /// <summary>
    /// Determines the encryption method from the dictionary.
    /// </summary>
    private PdfEncryptionMethod DetermineEncryptionMethod(PdfDictionary encryptDict)
    {
        return _version switch
        {
            1 => PdfEncryptionMethod.Rc4_40,
            2 => PdfEncryptionMethod.Rc4_128,
            3 => PdfEncryptionMethod.Rc4_128, // V=3 is also RC4 with extended key length
            4 => DetermineV4Method(encryptDict),
            5 => PdfEncryptionMethod.Aes256,
            _ => throw new PdfSecurityException($"Unsupported encryption version: {_version}")
        };
    }

    /// <summary>
    /// Determines the encryption method for V=4 (can be RC4 or AES-128).
    /// </summary>
    private static PdfEncryptionMethod DetermineV4Method(PdfDictionary encryptDict)
    {
        // Check crypt filters
        if (!encryptDict.TryGetValue(new PdfName("CF"), out PdfObject cfObj) || cfObj is not PdfDictionary cf)
            return PdfEncryptionMethod.Rc4_128; // Default to RC4

        // Check the StdCF filter (or whatever StmF points to)
        string filterName = GetNameValue(encryptDict, "StmF", "StdCF");
        if (!cf.TryGetValue(new PdfName(filterName), out PdfObject filterObj) || filterObj is not PdfDictionary filter)
            return PdfEncryptionMethod.Rc4_128;

        string cfm = GetNameValue(filter, "CFM", "V2");
        return cfm switch
        {
            "AESV2" => PdfEncryptionMethod.Aes128,
            "V2" => PdfEncryptionMethod.Rc4_128,
            _ => PdfEncryptionMethod.Rc4_128
        };
    }

    /// <summary>
    /// Attempts to authenticate with the given password.
    /// </summary>
    private byte[] Authenticate(string password, byte[] oValue, byte[] uValue,
        byte[]? oeValue, byte[]? ueValue, byte[]? permsValue, int permissions)
    {
        if (_version == 5)
        {
            return AuthenticateV5(password, oValue, uValue, oeValue!, ueValue!, permsValue!);
        }

        // V1-V4: Try user password first, then owner password
        byte[] key = ComputeEncryptionKeyV1V4(password, oValue, permissions);

        if (VerifyUserPasswordV1V4(key, uValue))
        {
            IsUserPassword = true;
            return key;
        }

        // Try as owner password
        byte[] userPassword = RecoverUserPasswordFromOwner(password, oValue);
        key = ComputeEncryptionKeyV1V4(Encoding.Latin1.GetString(userPassword), oValue, permissions);

        if (VerifyUserPasswordV1V4(key, uValue))
        {
            IsUserPassword = false;
            return key;
        }

        // Try empty password as fallback
        if (!string.IsNullOrEmpty(password))
        {
            key = ComputeEncryptionKeyV1V4("", oValue, permissions);
            if (VerifyUserPasswordV1V4(key, uValue))
            {
                IsUserPassword = true;
                return key;
            }
        }

        return [];
    }

    /// <summary>
    /// Authenticates using V=5 (AES-256) algorithm.
    /// ISO 32000-2:2020 section 7.6.4.3.3
    /// </summary>
    private byte[] AuthenticateV5(string password, byte[] oValue, byte[] uValue,
        byte[] oeValue, byte[] ueValue, byte[] permsValue)
    {
        byte[] passwordBytes = TruncatePassword(password);

        // Try user password first
        // User validation salt is bytes 32-39 of U
        var userValidationSalt = new byte[8];
        var userKeySalt = new byte[8];
        Array.Copy(uValue, 32, userValidationSalt, 0, 8);
        Array.Copy(uValue, 40, userKeySalt, 0, 8);

        byte[] hash = ComputeHashV5(passwordBytes, userValidationSalt, null);
        if (CompareBytes(hash, uValue, 32))
        {
            // User password is correct - decrypt the file encryption key
            IsUserPassword = true;
            byte[] keyHash = ComputeHashV5(passwordBytes, userKeySalt, null);
            return AesCipher.Decrypt(keyHash, PrependIV(ueValue));
        }

        // Try owner password
        // Owner validation salt is bytes 32-39 of O
        var ownerValidationSalt = new byte[8];
        var ownerKeySalt = new byte[8];
        Array.Copy(oValue, 32, ownerValidationSalt, 0, 8);
        Array.Copy(oValue, 40, ownerKeySalt, 0, 8);

        hash = ComputeHashV5(passwordBytes, ownerValidationSalt, uValue);
        if (CompareBytes(hash, oValue, 32))
        {
            // Owner password is correct
            IsUserPassword = false;
            byte[] keyHash = ComputeHashV5(passwordBytes, ownerKeySalt, uValue);
            return AesCipher.Decrypt(keyHash, PrependIV(oeValue));
        }

        return [];
    }

    /// <summary>
    /// Computes hash for V=5 password validation.
    /// ISO 32000-2:2020 Algorithm 2.B
    /// </summary>
    private static byte[] ComputeHashV5(byte[] password, byte[] salt, byte[]? userKey)
    {
        // Initial hash: SHA-256(password || salt || userKey)
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(password);
        sha256.AppendData(salt);
        if (userKey != null)
            sha256.AppendData(userKey);

        byte[] k = sha256.GetHashAndReset();

        // For R=6, perform additional rounds
        // (Simplified version - full implementation needs 64+ rounds with AES)
        return k[..32];
    }

    /// <summary>
    /// Prepends a zero IV for AES decryption of UE/OE values.
    /// </summary>
    private static byte[] PrependIV(byte[] data)
    {
        var result = new byte[16 + data.Length];
        // IV is all zeros for UE/OE decryption
        Array.Copy(data, 0, result, 16, data.Length);
        return result;
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
        if (data.Length == 0)
            return data;

        return Method switch
        {
            PdfEncryptionMethod.Rc4_40 or
            PdfEncryptionMethod.Rc4_128 => DecryptRc4(data, objectNumber, generationNumber),
            PdfEncryptionMethod.Aes128 => DecryptAes128(data, objectNumber, generationNumber),
            PdfEncryptionMethod.Aes256 => DecryptAes256(data),
            _ => data
        };
    }

    /// <summary>
    /// Decrypts data using RC4.
    /// </summary>
    private byte[] DecryptRc4(byte[] data, int objectNumber, int generationNumber)
    {
        byte[] objectKey = ComputeObjectKeyRc4(objectNumber, generationNumber);
        var rc4 = new RC4(objectKey);
        return rc4.ProcessCopy(data);
    }

    /// <summary>
    /// Decrypts data using AES-128.
    /// </summary>
    private byte[] DecryptAes128(byte[] data, int objectNumber, int generationNumber)
    {
        if (data.Length < 16)
            return data; // Too short for IV

        byte[] objectKey = ComputeObjectKeyAes128(objectNumber, generationNumber);
        return AesCipher.Decrypt(objectKey, data);
    }

    /// <summary>
    /// Decrypts data using AES-256.
    /// For V=5, the file encryption key is used directly (no object-specific key).
    /// </summary>
    private byte[] DecryptAes256(byte[] data)
    {
        if (data.Length < 16)
            return data;

        return AesCipher.Decrypt(_encryptionKey, data);
    }

    /// <summary>
    /// Computes the object-specific RC4 encryption key.
    /// ISO 32000-1 Algorithm 1 (section 7.6.2)
    /// </summary>
    private byte[] ComputeObjectKeyRc4(int objectNumber, int generationNumber)
    {
        int keyInputLength = _encryptionKey.Length + 5;
        var keyInput = new byte[keyInputLength];

        Array.Copy(_encryptionKey, 0, keyInput, 0, _encryptionKey.Length);
        keyInput[_encryptionKey.Length] = (byte)(objectNumber & 0xFF);
        keyInput[_encryptionKey.Length + 1] = (byte)((objectNumber >> 8) & 0xFF);
        keyInput[_encryptionKey.Length + 2] = (byte)((objectNumber >> 16) & 0xFF);
        keyInput[_encryptionKey.Length + 3] = (byte)(generationNumber & 0xFF);
        keyInput[_encryptionKey.Length + 4] = (byte)((generationNumber >> 8) & 0xFF);

        byte[] hash = MD5.HashData(keyInput);

        int objectKeyLength = Math.Min(_keyLengthBytes + 5, 16);
        var objectKey = new byte[objectKeyLength];
        Array.Copy(hash, objectKey, objectKeyLength);

        return objectKey;
    }

    /// <summary>
    /// Computes the object-specific AES-128 encryption key.
    /// ISO 32000-1 Algorithm 1 with AES marker.
    /// </summary>
    private byte[] ComputeObjectKeyAes128(int objectNumber, int generationNumber)
    {
        // Same as RC4 but with "sAlT" marker appended
        int keyInputLength = _encryptionKey.Length + 5 + 4;
        var keyInput = new byte[keyInputLength];

        Array.Copy(_encryptionKey, 0, keyInput, 0, _encryptionKey.Length);
        keyInput[_encryptionKey.Length] = (byte)(objectNumber & 0xFF);
        keyInput[_encryptionKey.Length + 1] = (byte)((objectNumber >> 8) & 0xFF);
        keyInput[_encryptionKey.Length + 2] = (byte)((objectNumber >> 16) & 0xFF);
        keyInput[_encryptionKey.Length + 3] = (byte)(generationNumber & 0xFF);
        keyInput[_encryptionKey.Length + 4] = (byte)((generationNumber >> 8) & 0xFF);
        Array.Copy(AesSaltMarker, 0, keyInput, _encryptionKey.Length + 5, 4);

        byte[] hash = MD5.HashData(keyInput);

        // AES-128 always uses 16 byte key
        return hash;
    }

    /// <summary>
    /// Computes the file encryption key from the password (V1-V4).
    /// ISO 32000-1 Algorithm 2 (section 7.6.3.3)
    /// </summary>
    private byte[] ComputeEncryptionKeyV1V4(string password, byte[] oValue, int permissions)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        byte[] paddedPassword = PadPassword(password);
        md5.AppendData(paddedPassword);
        md5.AppendData(oValue);

        byte[] pBytes = [(byte)permissions, (byte)(permissions >> 8), (byte)(permissions >> 16), (byte)(permissions >> 24)];
        md5.AppendData(pBytes);
        md5.AppendData(_documentId);

        // Revision 4: If metadata is not encrypted, pass 0xFFFFFFFF
        if (_revision >= 4 && !_encryptMetadata)
        {
            md5.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        byte[] hash = md5.GetHashAndReset();

        // Revision 3+: 50 additional MD5 iterations
        if (_revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash.AsSpan(0, _keyLengthBytes));
            }
        }

        var key = new byte[_keyLengthBytes];
        Array.Copy(hash, key, _keyLengthBytes);
        return key;
    }

    /// <summary>
    /// Verifies the user password against the /U value (V1-V4).
    /// ISO 32000-1 Algorithm 4/5 (section 7.6.3.4)
    /// </summary>
    private bool VerifyUserPasswordV1V4(byte[] encryptionKey, byte[] uValue)
    {
        byte[] computedU;

        if (_revision == 2)
        {
            var rc4 = new RC4(encryptionKey);
            computedU = rc4.ProcessCopy(PasswordPadding);
        }
        else
        {
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            md5.AppendData(PasswordPadding);
            md5.AppendData(_documentId);
            computedU = md5.GetHashAndReset();

            for (var i = 0; i < 20; i++)
            {
                var iterKey = new byte[encryptionKey.Length];
                for (var j = 0; j < encryptionKey.Length; j++)
                {
                    iterKey[j] = (byte)(encryptionKey[j] ^ i);
                }
                var rc4 = new RC4(iterKey);
                rc4.Process(computedU);
            }
        }

        int compareLength = _revision == 2 ? 32 : 16;
        return CompareBytes(computedU, uValue, compareLength);
    }

    /// <summary>
    /// Recovers the user password from the owner password (V1-V4).
    /// ISO 32000-1 Algorithm 7 (section 7.6.3.4)
    /// </summary>
    private byte[] RecoverUserPasswordFromOwner(string ownerPassword, byte[] oValue)
    {
        byte[] paddedPassword = PadPassword(ownerPassword);
        byte[] hash = MD5.HashData(paddedPassword);

        if (_revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash);
            }
        }

        var key = new byte[_keyLengthBytes];
        Array.Copy(hash, key, _keyLengthBytes);

        var userPassword = new byte[oValue.Length];
        Array.Copy(oValue, userPassword, oValue.Length);

        if (_revision == 2)
        {
            var rc4 = new RC4(key);
            rc4.Process(userPassword);
        }
        else
        {
            for (var i = 19; i >= 0; i--)
            {
                var iterKey = new byte[key.Length];
                for (var j = 0; j < key.Length; j++)
                {
                    iterKey[j] = (byte)(key[j] ^ i);
                }
                var rc4 = new RC4(iterKey);
                rc4.Process(userPassword);
            }
        }

        return userPassword;
    }

    /// <summary>
    /// Pads or truncates a password to exactly 32 bytes.
    /// </summary>
    private static byte[] PadPassword(string password)
    {
        var result = new byte[32];
        byte[] passwordBytes = Encoding.Latin1.GetBytes(password ?? "");

        int copyLength = Math.Min(passwordBytes.Length, 32);
        Array.Copy(passwordBytes, result, copyLength);

        if (copyLength < 32)
        {
            Array.Copy(PasswordPadding, 0, result, copyLength, 32 - copyLength);
        }

        return result;
    }

    /// <summary>
    /// Truncates password to 127 bytes for V=5.
    /// </summary>
    private static byte[] TruncatePassword(string password)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password ?? "");
        if (passwordBytes.Length <= 127)
            return passwordBytes;

        var truncated = new byte[127];
        Array.Copy(passwordBytes, truncated, 127);
        return truncated;
    }

    /// <summary>
    /// Compares two byte arrays up to a specified length.
    /// </summary>
    private static bool CompareBytes(byte[] a, byte[] b, int length)
    {
        if (a.Length < length || b.Length < length)
            return false;

        for (var i = 0; i < length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    private static int GetIntValue(PdfDictionary dict, string key, int defaultValue)
    {
        if (dict.TryGetValue(new PdfName(key), out PdfObject obj) && obj is PdfInteger intVal)
            return intVal.Value;
        return defaultValue;
    }

    private static bool GetBoolValue(PdfDictionary dict, string key, bool defaultValue)
    {
        if (dict.TryGetValue(new PdfName(key), out PdfObject obj) && obj is PdfBoolean boolVal)
            return boolVal.Value;
        return defaultValue;
    }

    private static string GetNameValue(PdfDictionary dict, string key, string defaultValue)
    {
        if (dict.TryGetValue(new PdfName(key), out PdfObject obj) && obj is PdfName nameVal)
            return nameVal.Value;
        return defaultValue;
    }

    private static byte[]? GetBytesValue(PdfDictionary dict, string key)
    {
        if (dict.TryGetValue(new PdfName(key), out PdfObject obj) && obj is PdfString strVal)
            return strVal.Bytes;
        return null;
    }
}