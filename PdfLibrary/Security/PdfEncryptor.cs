using System.Security.Cryptography;
using System.Text;

namespace PdfLibrary.Security;

/// <summary>
/// Handles PDF document encryption according to ISO 32000-1:2008 section 7.6
/// and ISO 32000-2:2020 for AES-256.
/// Supports:
/// - RC4 40-bit (V=1, R=2) - Legacy, not recommended
/// - RC4 128-bit (V=2, R=3) - Legacy, not recommended
/// - AES-128 (V=4, R=4)
/// - AES-256 (V=5, R=6) - Recommended
/// </summary>
internal class PdfEncryptor
{
    // Padding string used in PDF encryption (Table 21 in ISO 32000-1)
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];

    private readonly byte[] _fileEncryptionKey;
    private readonly PdfEncryptionMethod _method;
    private readonly int _keyLengthBytes;
    private readonly byte[] _documentId;

    /// <summary>
    /// Gets the encryption method.
    /// </summary>
    public PdfEncryptionMethod Method => _method;

    /// <summary>
    /// Gets the /O value (owner password hash).
    /// </summary>
    public byte[] OValue { get; }

    /// <summary>
    /// Gets the /U value (user password hash).
    /// </summary>
    public byte[] UValue { get; }

    /// <summary>
    /// Gets the /OE value (encrypted owner key, V=5 only).
    /// </summary>
    public byte[]? OEValue { get; }

    /// <summary>
    /// Gets the /UE value (encrypted user key, V=5 only).
    /// </summary>
    public byte[]? UEValue { get; }

    /// <summary>
    /// Gets the /Perms value (encrypted permissions, V=5 only).
    /// </summary>
    public byte[]? PermsValue { get; }

    /// <summary>
    /// Gets the encryption version (V value).
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the encryption revision (R value).
    /// </summary>
    public int Revision { get; }

    /// <summary>
    /// Gets the key length in bits.
    /// </summary>
    public int KeyLengthBits => _keyLengthBytes * 8;

    /// <summary>
    /// Gets the permissions value.
    /// </summary>
    public PdfPermissions Permissions { get; }

    /// <summary>
    /// Creates an encryptor for a new PDF document.
    /// </summary>
    /// <param name="userPassword">Password required to open the document (can be empty for no password)</param>
    /// <param name="ownerPassword">Password for full access (if empty, uses userPassword)</param>
    /// <param name="permissions">Document permissions</param>
    /// <param name="method">Encryption method</param>
    /// <param name="documentId">Document ID (first element of /ID array)</param>
    public PdfEncryptor(
        string userPassword,
        string ownerPassword,
        PdfPermissionFlags permissions,
        PdfEncryptionMethod method,
        byte[] documentId)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        // If owner password is empty, use user password
        if (string.IsNullOrEmpty(ownerPassword))
            ownerPassword = userPassword;

        _documentId = documentId;
        _method = method;

        // Set version, revision, and key length based on method
        (Version, Revision, _keyLengthBytes) = method switch
        {
            PdfEncryptionMethod.Rc4_40 => (1, 2, 5),
            PdfEncryptionMethod.Rc4_128 => (2, 3, 16),
            PdfEncryptionMethod.Aes128 => (4, 4, 16),
            PdfEncryptionMethod.Aes256 => (5, 6, 32),
            _ => throw new ArgumentException($"Unsupported encryption method: {method}")
        };

        // Compute permissions value with required bits set
        // Bits 1-2 must be 0, bits 7-8 must be 1, bits 13-32 must be 1
        int pValue = (int)permissions | unchecked((int)0xFFFFF0C0);
        Permissions = new PdfPermissions(pValue);

        if (Version == 5)
        {
            // AES-256 (V=5, R=6) uses different algorithm
            (_fileEncryptionKey, OValue, UValue, OEValue, UEValue, PermsValue) =
                ComputeEncryptionValuesV5(userPassword, ownerPassword, pValue);
        }
        else
        {
            // V1-V4 use standard algorithm
            (OValue, UValue, _fileEncryptionKey) =
                ComputeEncryptionValuesV1V4(userPassword, ownerPassword, pValue);
            OEValue = null;
            UEValue = null;
            PermsValue = null;
        }
    }

    /// <summary>
    /// Encrypts data for a specific object.
    /// </summary>
    /// <param name="data">Plaintext data</param>
    /// <param name="objectNumber">Object number</param>
    /// <param name="generationNumber">Generation number</param>
    /// <returns>Encrypted data</returns>
    public byte[] Encrypt(byte[] data, int objectNumber, int generationNumber)
    {
        if (data.Length == 0)
            return data;

        return _method switch
        {
            PdfEncryptionMethod.Rc4_40 or
            PdfEncryptionMethod.Rc4_128 => EncryptRc4(data, objectNumber, generationNumber),
            PdfEncryptionMethod.Aes128 => EncryptAes128(data, objectNumber, generationNumber),
            PdfEncryptionMethod.Aes256 => EncryptAes256(data),
            _ => data
        };
    }

    /// <summary>
    /// Encrypts a string for a specific object.
    /// </summary>
    public byte[] EncryptString(byte[] data, int objectNumber, int generationNumber)
    {
        return Encrypt(data, objectNumber, generationNumber);
    }

    /// <summary>
    /// Encrypts stream data for a specific object.
    /// </summary>
    public byte[] EncryptStream(byte[] data, int objectNumber, int generationNumber)
    {
        return Encrypt(data, objectNumber, generationNumber);
    }

    // ==================== V1-V4 Encryption ====================

    /// <summary>
    /// Computes O, U values and encryption key for V1-V4.
    /// </summary>
    private (byte[] O, byte[] U, byte[] key) ComputeEncryptionValuesV1V4(
        string userPassword, string ownerPassword, int permissions)
    {
        // Step 1: Compute O value (Algorithm 3)
        byte[] oValue = ComputeOValueV1V4(userPassword, ownerPassword);

        // Step 2: Compute encryption key (Algorithm 2)
        byte[] encryptionKey = ComputeEncryptionKeyV1V4(userPassword, oValue, permissions);

        // Step 3: Compute U value (Algorithm 4/5)
        byte[] uValue = ComputeUValueV1V4(encryptionKey);

        return (oValue, uValue, encryptionKey);
    }

    /// <summary>
    /// Computes the O value (owner password hash).
    /// ISO 32000-1 Algorithm 3.
    /// </summary>
    private byte[] ComputeOValueV1V4(string userPassword, string ownerPassword)
    {
        // Step a: Pad owner password
        byte[] paddedOwner = PadPassword(ownerPassword);

        // Step b: MD5 hash
        byte[] hash = MD5.HashData(paddedOwner);

        // Step c: For R=3+, do 50 additional iterations
        if (Revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash);
            }
        }

        // Step d: Use first n bytes as RC4 key
        var rc4Key = new byte[_keyLengthBytes];
        Array.Copy(hash, rc4Key, _keyLengthBytes);

        // Step e: Pad user password
        byte[] paddedUser = PadPassword(userPassword);

        // Step f: Encrypt with RC4
        byte[] oValue;
        if (Revision == 2)
        {
            var rc4 = new RC4(rc4Key);
            oValue = rc4.ProcessCopy(paddedUser);
        }
        else
        {
            // R=3+: 20 iterations with modified keys
            oValue = new byte[paddedUser.Length];
            Array.Copy(paddedUser, oValue, paddedUser.Length);

            for (var i = 0; i < 20; i++)
            {
                var iterKey = new byte[rc4Key.Length];
                for (var j = 0; j < rc4Key.Length; j++)
                {
                    iterKey[j] = (byte)(rc4Key[j] ^ i);
                }
                var rc4 = new RC4(iterKey);
                rc4.Process(oValue);
            }
        }

        return oValue;
    }

    /// <summary>
    /// Computes the file encryption key.
    /// ISO 32000-1 Algorithm 2.
    /// </summary>
    private byte[] ComputeEncryptionKeyV1V4(string userPassword, byte[] oValue, int permissions)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        // Step a-b: Pad password
        byte[] paddedPassword = PadPassword(userPassword);
        md5.AppendData(paddedPassword);

        // Step c: O value
        md5.AppendData(oValue);

        // Step d: Permissions (low-order bytes first)
        byte[] pBytes =
        [
            (byte)permissions,
            (byte)(permissions >> 8),
            (byte)(permissions >> 16),
            (byte)(permissions >> 24)
        ];
        md5.AppendData(pBytes);

        // Step e: Document ID
        md5.AppendData(_documentId);

        // Step f: For R=4, if metadata not encrypted, add 0xFFFFFFFF
        // (We always encrypt metadata for simplicity)

        byte[] hash = md5.GetHashAndReset();

        // Step g: For R=3+, do 50 additional iterations
        if (Revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash.AsSpan(0, _keyLengthBytes));
            }
        }

        // Step h: Use first n bytes
        var key = new byte[_keyLengthBytes];
        Array.Copy(hash, key, _keyLengthBytes);
        return key;
    }

    /// <summary>
    /// Computes the U value (user password verification).
    /// ISO 32000-1 Algorithm 4 (R=2) or Algorithm 5 (R=3+).
    /// </summary>
    private byte[] ComputeUValueV1V4(byte[] encryptionKey)
    {
        if (Revision == 2)
        {
            // Algorithm 4: Simply encrypt padding string
            var rc4 = new RC4(encryptionKey);
            return rc4.ProcessCopy(PasswordPadding);
        }

        // Algorithm 5 (R=3+)
        // Step a: MD5 hash of padding + document ID
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(PasswordPadding);
        md5.AppendData(_documentId);
        byte[] hash = md5.GetHashAndReset();

        // Step b-c: 20 RC4 iterations
        for (var i = 0; i < 20; i++)
        {
            var iterKey = new byte[encryptionKey.Length];
            for (var j = 0; j < encryptionKey.Length; j++)
            {
                iterKey[j] = (byte)(encryptionKey[j] ^ i);
            }
            var rc4 = new RC4(iterKey);
            rc4.Process(hash);
        }

        // Step d: Append 16 arbitrary bytes (we use zeros)
        var uValue = new byte[32];
        Array.Copy(hash, uValue, 16);
        return uValue;
    }

    /// <summary>
    /// Encrypts data using RC4.
    /// </summary>
    private byte[] EncryptRc4(byte[] data, int objectNumber, int generationNumber)
    {
        byte[] objectKey = ComputeObjectKeyRc4(objectNumber, generationNumber);
        var rc4 = new RC4(objectKey);
        return rc4.ProcessCopy(data);
    }

    /// <summary>
    /// Computes the object-specific RC4 key.
    /// </summary>
    private byte[] ComputeObjectKeyRc4(int objectNumber, int generationNumber)
    {
        int keyInputLength = _fileEncryptionKey.Length + 5;
        var keyInput = new byte[keyInputLength];

        Array.Copy(_fileEncryptionKey, 0, keyInput, 0, _fileEncryptionKey.Length);
        keyInput[_fileEncryptionKey.Length] = (byte)(objectNumber & 0xFF);
        keyInput[_fileEncryptionKey.Length + 1] = (byte)((objectNumber >> 8) & 0xFF);
        keyInput[_fileEncryptionKey.Length + 2] = (byte)((objectNumber >> 16) & 0xFF);
        keyInput[_fileEncryptionKey.Length + 3] = (byte)(generationNumber & 0xFF);
        keyInput[_fileEncryptionKey.Length + 4] = (byte)((generationNumber >> 8) & 0xFF);

        byte[] hash = MD5.HashData(keyInput);

        int objectKeyLength = Math.Min(_keyLengthBytes + 5, 16);
        var objectKey = new byte[objectKeyLength];
        Array.Copy(hash, objectKey, objectKeyLength);

        return objectKey;
    }

    // ==================== AES-128 Encryption ====================

    /// <summary>
    /// Encrypts data using AES-128.
    /// </summary>
    private byte[] EncryptAes128(byte[] data, int objectNumber, int generationNumber)
    {
        byte[] objectKey = ComputeObjectKeyAes128(objectNumber, generationNumber);
        return AesCipher.Encrypt(objectKey, data);
    }

    /// <summary>
    /// Computes the object-specific AES-128 key.
    /// </summary>
    private byte[] ComputeObjectKeyAes128(int objectNumber, int generationNumber)
    {
        // Same as RC4 but with "sAlT" marker
        int keyInputLength = _fileEncryptionKey.Length + 5 + 4;
        var keyInput = new byte[keyInputLength];

        Array.Copy(_fileEncryptionKey, 0, keyInput, 0, _fileEncryptionKey.Length);
        keyInput[_fileEncryptionKey.Length] = (byte)(objectNumber & 0xFF);
        keyInput[_fileEncryptionKey.Length + 1] = (byte)((objectNumber >> 8) & 0xFF);
        keyInput[_fileEncryptionKey.Length + 2] = (byte)((objectNumber >> 16) & 0xFF);
        keyInput[_fileEncryptionKey.Length + 3] = (byte)(generationNumber & 0xFF);
        keyInput[_fileEncryptionKey.Length + 4] = (byte)((generationNumber >> 8) & 0xFF);

        // "sAlT" marker
        keyInput[_fileEncryptionKey.Length + 5] = 0x73; // 's'
        keyInput[_fileEncryptionKey.Length + 6] = 0x41; // 'A'
        keyInput[_fileEncryptionKey.Length + 7] = 0x6C; // 'l'
        keyInput[_fileEncryptionKey.Length + 8] = 0x54; // 'T'

        return MD5.HashData(keyInput);
    }

    // ==================== AES-256 Encryption ====================

    /// <summary>
    /// Computes encryption values for V=5 (AES-256).
    /// ISO 32000-2 Algorithm 8.
    /// </summary>
    private (byte[] key, byte[] O, byte[] U, byte[] OE, byte[] UE, byte[] Perms)
        ComputeEncryptionValuesV5(string userPassword, string ownerPassword, int permissions)
    {
        // Generate random file encryption key
        var fileKey = new byte[32];
        RandomNumberGenerator.Fill(fileKey);

        // Truncate passwords to 127 bytes UTF-8
        byte[] userPwd = TruncatePasswordV5(userPassword);
        byte[] ownerPwd = TruncatePasswordV5(ownerPassword);

        // Generate random salts (8 bytes each)
        var userValidationSalt = new byte[8];
        var userKeySalt = new byte[8];
        var ownerValidationSalt = new byte[8];
        var ownerKeySalt = new byte[8];
        RandomNumberGenerator.Fill(userValidationSalt);
        RandomNumberGenerator.Fill(userKeySalt);
        RandomNumberGenerator.Fill(ownerValidationSalt);
        RandomNumberGenerator.Fill(ownerKeySalt);

        // Compute U value (48 bytes): hash(32) + validationSalt(8) + keySalt(8)
        byte[] userHash = ComputeHashV5(userPwd, userValidationSalt, null);
        var uValue = new byte[48];
        Array.Copy(userHash, 0, uValue, 0, 32);
        Array.Copy(userValidationSalt, 0, uValue, 32, 8);
        Array.Copy(userKeySalt, 0, uValue, 40, 8);

        // Compute UE value (32 bytes): AES-256-CBC encrypt fileKey with hash(pwd + keySalt)
        byte[] userKeyHash = ComputeHashV5(userPwd, userKeySalt, null);
        byte[] ueValue = AesCipher.EncryptNoPrependIV(userKeyHash, fileKey, new byte[16]);

        // Compute O value (48 bytes): hash(32) + validationSalt(8) + keySalt(8)
        // Note: Owner hash includes U value
        byte[] ownerHash = ComputeHashV5(ownerPwd, ownerValidationSalt, uValue);
        var oValue = new byte[48];
        Array.Copy(ownerHash, 0, oValue, 0, 32);
        Array.Copy(ownerValidationSalt, 0, oValue, 32, 8);
        Array.Copy(ownerKeySalt, 0, oValue, 40, 8);

        // Compute OE value (32 bytes)
        byte[] ownerKeyHash = ComputeHashV5(ownerPwd, ownerKeySalt, uValue);
        byte[] oeValue = AesCipher.EncryptNoPrependIV(ownerKeyHash, fileKey, new byte[16]);

        // Compute Perms value (16 bytes)
        byte[] permsValue = ComputePermsValueV5(fileKey, permissions);

        return (fileKey, oValue, uValue, oeValue, ueValue, permsValue);
    }

    /// <summary>
    /// Computes hash for V=5 using Algorithm 2.B (simplified for R=6).
    /// </summary>
    private static byte[] ComputeHashV5(byte[] password, byte[] salt, byte[]? userKey)
    {
        // For R=6, use the extended hash algorithm
        // Simplified implementation - concatenate and SHA-256
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(password);
        sha256.AppendData(salt);
        if (userKey != null)
            sha256.AppendData(userKey);

        byte[] k = sha256.GetHashAndReset();

        // R=6 requires additional rounds with AES-CBC
        // Full implementation of Algorithm 2.B
        var round = 0;
        while (round < 64 || round < k[^1] + 32)
        {
            // K1 = password + K + userKey (repeated 64 times)
            int k1Size = password.Length + k.Length + (userKey?.Length ?? 0);
            var k1 = new byte[k1Size * 64];
            for (var i = 0; i < 64; i++)
            {
                int offset = i * k1Size;
                Array.Copy(password, 0, k1, offset, password.Length);
                Array.Copy(k, 0, k1, offset + password.Length, k.Length);
                if (userKey != null)
                    Array.Copy(userKey, 0, k1, offset + password.Length + k.Length, userKey.Length);
            }

            // AES-CBC encrypt K1 with key=K[0:16], IV=K[16:32]
            var aesKey = new byte[16];
            var aesIv = new byte[16];
            Array.Copy(k, 0, aesKey, 0, 16);
            Array.Copy(k, 16, aesIv, 0, 16);

            byte[] e = AesCipher.EncryptNoPrependIV(aesKey, k1, aesIv);

            // Take first 16 bytes of E as big-endian number mod 3
            var sum = 0;
            for (var i = 0; i < 16; i++)
            {
                sum += e[i];
            }

            // Hash with appropriate algorithm
            k = (sum % 3) switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e)
            };

            round++;
        }

        // Return first 32 bytes
        var result = new byte[32];
        Array.Copy(k, result, 32);
        return result;
    }

    /// <summary>
    /// Computes the Perms value for V=5.
    /// </summary>
    private static byte[] ComputePermsValueV5(byte[] fileKey, int permissions)
    {
        // Build 16-byte plaintext
        var perms = new byte[16];

        // Bytes 0-3: Permissions (little-endian)
        perms[0] = (byte)permissions;
        perms[1] = (byte)(permissions >> 8);
        perms[2] = (byte)(permissions >> 16);
        perms[3] = (byte)(permissions >> 24);

        // Bytes 4-7: 0xFFFFFFFF (extension, always set)
        perms[4] = 0xFF;
        perms[5] = 0xFF;
        perms[6] = 0xFF;
        perms[7] = 0xFF;

        // Byte 8: 'T' if EncryptMetadata true, 'F' otherwise
        // We always encrypt metadata
        perms[8] = (byte)'T';

        // Byte 9: 'a'
        perms[9] = (byte)'a';

        // Byte 10: 'd'
        perms[10] = (byte)'d';

        // Byte 11: 'b'
        perms[11] = (byte)'b';

        // Bytes 12-15: Random
        RandomNumberGenerator.Fill(perms.AsSpan(12, 4));

        // AES-256-ECB encrypt (no IV, single block)
        using var aes = Aes.Create();
        aes.Key = fileKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(perms, 0, 16);
    }

    /// <summary>
    /// Encrypts data using AES-256.
    /// For V=5, the file encryption key is used directly.
    /// </summary>
    private byte[] EncryptAes256(byte[] data)
    {
        return AesCipher.Encrypt(_fileEncryptionKey, data);
    }

    /// <summary>
    /// Pads or truncates a password to exactly 32 bytes (V1-V4).
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
    /// Truncates password to 127 bytes UTF-8 (V5).
    /// </summary>
    private static byte[] TruncatePasswordV5(string password)
    {
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password ?? "");
        if (passwordBytes.Length <= 127)
            return passwordBytes;

        var truncated = new byte[127];
        Array.Copy(passwordBytes, truncated, 127);
        return truncated;
    }
}
