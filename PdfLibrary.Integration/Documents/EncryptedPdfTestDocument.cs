using System.Security.Cryptography;
using System.Text;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Generates encrypted PDF test documents for security testing.
/// Creates PDFs with various encryption methods: RC4-40, RC4-128, AES-128, AES-256.
/// </summary>
public class EncryptedPdfTestDocument : ITestDocument
{
    public string Name { get; }
    public string Description { get; }

    /// <summary>
    /// Encryption configuration for test PDF generation
    /// </summary>
    public enum EncryptionType
    {
        /// <summary>RC4 40-bit encryption (V=1, R=2)</summary>
        Rc4_40,
        /// <summary>RC4 128-bit encryption (V=2, R=3)</summary>
        Rc4_128,
        /// <summary>AES 128-bit encryption (V=4, R=4)</summary>
        Aes128,
        /// <summary>AES 256-bit encryption (V=5, R=6)</summary>
        Aes256
    }

    private readonly EncryptionType _encryptionType;
    private readonly string _userPassword;
    private readonly string _ownerPassword;
    private readonly int _permissions;

    /// <summary>
    /// Creates a new encrypted PDF test document generator.
    /// </summary>
    /// <param name="encryptionType">Type of encryption to use</param>
    /// <param name="userPassword">User password (empty string for open access)</param>
    /// <param name="ownerPassword">Owner password (controls permissions)</param>
    /// <param name="permissions">PDF permission flags (-4 = all permissions)</param>
    /// <param name="name">Optional custom name for the test document</param>
    public EncryptedPdfTestDocument(
        EncryptionType encryptionType = EncryptionType.Aes128,
        string userPassword = "",
        string ownerPassword = "owner",
        int permissions = -4,
        string? name = null)
    {
        _encryptionType = encryptionType;
        _userPassword = userPassword;
        _ownerPassword = ownerPassword;
        _permissions = permissions;

        string passwordDesc = string.IsNullOrEmpty(userPassword) ? "EmptyPassword" : "WithPassword";
        Name = name ?? $"Encrypted{encryptionType}_{passwordDesc}";
        Description = $"Encrypted PDF using {encryptionType} ({(string.IsNullOrEmpty(userPassword) ? "no user password" : "with user password")})";
    }

    public void Generate(string outputPath)
    {
        // Generate document ID
        var documentId = new byte[16];
        RandomNumberGenerator.Fill(documentId);

        // Generate encryption key and compute O/U values
        (int keyLength, int version, int revision) = GetEncryptionParams();
        byte[] encryptionKey = ComputeEncryptionKey(documentId, keyLength, revision);
        byte[] oValue = ComputeOValue(encryptionKey, keyLength, revision);
        byte[] uValue = ComputeUValue(encryptionKey, documentId, revision);

        using FileStream stream = File.Create(outputPath);
        using var writer = new StreamWriter(stream, Encoding.ASCII);
        writer.NewLine = "\n";

        // Write PDF header
        writer.WriteLine("%PDF-1.7");
        writer.WriteLine("%âãÏÓ");
        writer.Flush();

        var objectOffsets = new long[10];
        var objNum = 1;

        // Object 1: Catalog
        objectOffsets[objNum] = stream.Position;
        writer.WriteLine($"{objNum} 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();
        objNum++;

        // Object 2: Pages
        objectOffsets[objNum] = stream.Position;
        writer.WriteLine($"{objNum} 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();
        objNum++;

        // Object 3: Page
        objectOffsets[objNum] = stream.Position;
        writer.WriteLine($"{objNum} 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R");
        writer.WriteLine("   /MediaBox [0 0 612 792]");
        writer.WriteLine("   /Contents 4 0 R");
        writer.WriteLine("   /Resources << /Font << /F1 5 0 R >> >>");
        writer.WriteLine(">>");
        writer.WriteLine("endobj");
        writer.Flush();
        objNum++;

        // Object 4: Content stream (encrypted)
        var contentOperators = "BT /F1 24 Tf 100 700 Td (Encrypted PDF Test) Tj ET";
        byte[] contentBytes = Encoding.ASCII.GetBytes(contentOperators);
        byte[] encryptedContent = EncryptData(contentBytes, encryptionKey, objNum, 0);

        objectOffsets[objNum] = stream.Position;
        writer.WriteLine($"{objNum} 0 obj");
        writer.WriteLine($"<< /Length {encryptedContent.Length} >>");
        writer.WriteLine("stream");
        writer.Flush();
        stream.Write(encryptedContent);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();
        objNum++;

        // Object 5: Font
        objectOffsets[objNum] = stream.Position;
        writer.WriteLine($"{objNum} 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        writer.WriteLine("endobj");
        writer.Flush();
        objNum++;

        // Object 6: Encrypt dictionary
        int encryptObj = objNum;
        objectOffsets[objNum] = stream.Position;
        writer.WriteLine($"{objNum} 0 obj");
        WriteEncryptDictionary(writer, oValue, uValue, version, revision, keyLength);
        writer.WriteLine("endobj");
        writer.Flush();
        objNum++;

        // Write xref
        long xrefOffset = stream.Position;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {objNum}");
        writer.WriteLine("0000000000 65535 f ");
        for (var i = 1; i < objNum; i++)
        {
            writer.WriteLine($"{objectOffsets[i]:D10} 00000 n ");
        }

        // Write trailer
        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {objNum}");
        writer.WriteLine("   /Root 1 0 R");
        writer.WriteLine($"   /Encrypt {encryptObj} 0 R");
        writer.Write("   /ID [<");
        writer.Write(Convert.ToHexString(documentId));
        writer.Write("><");
        writer.Write(Convert.ToHexString(documentId));
        writer.WriteLine(">]");
        writer.WriteLine(">>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset);
        writer.WriteLine("%%EOF");
    }

    private (int keyLength, int version, int revision) GetEncryptionParams()
    {
        return _encryptionType switch
        {
            EncryptionType.Rc4_40 => (40, 1, 2),
            EncryptionType.Rc4_128 => (128, 2, 3),
            EncryptionType.Aes128 => (128, 4, 4),
            EncryptionType.Aes256 => (256, 5, 6),
            _ => throw new ArgumentException($"Unknown encryption type: {_encryptionType}")
        };
    }

    private void WriteEncryptDictionary(StreamWriter writer, byte[] oValue, byte[] uValue, int version, int revision, int keyLength)
    {
        writer.WriteLine("<< /Filter /Standard");
        writer.WriteLine($"   /V {version}");
        writer.WriteLine($"   /R {revision}");
        writer.WriteLine($"   /Length {keyLength}");
        writer.Write("   /O <");
        writer.Write(Convert.ToHexString(oValue));
        writer.WriteLine(">");
        writer.Write("   /U <");
        writer.Write(Convert.ToHexString(uValue));
        writer.WriteLine(">");
        writer.WriteLine($"   /P {_permissions}");

        if (version == 4)
        {
            // AES-128 crypt filters
            writer.WriteLine("   /CF << /StdCF << /CFM /AESV2 /Length 16 /AuthEvent /DocOpen >> >>");
            writer.WriteLine("   /StmF /StdCF");
            writer.WriteLine("   /StrF /StdCF");
        }
        else if (version == 5)
        {
            // AES-256 crypt filters
            writer.WriteLine("   /CF << /StdCF << /CFM /AESV3 /Length 32 /AuthEvent /DocOpen >> >>");
            writer.WriteLine("   /StmF /StdCF");
            writer.WriteLine("   /StrF /StdCF");
            // V=5 requires additional values (OE, UE, Perms)
            var oe = new byte[32];
            var ue = new byte[32];
            var perms = new byte[16];
            RandomNumberGenerator.Fill(oe);
            RandomNumberGenerator.Fill(ue);
            RandomNumberGenerator.Fill(perms);
            writer.Write("   /OE <");
            writer.Write(Convert.ToHexString(oe));
            writer.WriteLine(">");
            writer.Write("   /UE <");
            writer.Write(Convert.ToHexString(ue));
            writer.WriteLine(">");
            writer.Write("   /Perms <");
            writer.Write(Convert.ToHexString(perms));
            writer.WriteLine(">");
        }

        writer.WriteLine(">>");
    }

    private byte[] ComputeEncryptionKey(byte[] documentId, int keyLengthBits, int revision)
    {
        int keyLength = keyLengthBits / 8;

        // PDF encryption key algorithm (Algorithm 2 from ISO 32000-1)
        using var md5 = MD5.Create();

        // Step a: Pad password to 32 bytes
        byte[] paddedPassword = PadPassword(_userPassword);

        // Step b-g: Create hash input
        using var hashInput = new MemoryStream();
        hashInput.Write(paddedPassword);

        // Step c: Pass O value (compute it first for this step)
        byte[] oValue = ComputeOValue(null!, keyLengthBits, revision);
        hashInput.Write(oValue);

        // Step d: Pass P value as 4 bytes little-endian
        hashInput.Write(BitConverter.GetBytes(_permissions));

        // Step e: Pass document ID
        hashInput.Write(documentId);

        // Compute MD5
        byte[] hash = md5.ComputeHash(hashInput.ToArray());

        // Step f: For R >= 3, iterate 50 times
        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = md5.ComputeHash(hash, 0, keyLength);
            }
        }

        // Return first n bytes as encryption key
        return hash[..keyLength];
    }

    private byte[] ComputeOValue(byte[]? encryptionKey, int keyLengthBits, int revision)
    {
        int keyLength = keyLengthBits / 8;
        using var md5 = MD5.Create();

        // Algorithm 3 from ISO 32000-1
        // Step a: Pad owner password
        byte[] paddedOwner = PadPassword(_ownerPassword);

        // Step b: MD5 hash
        byte[] hash = md5.ComputeHash(paddedOwner);

        // Step c: For R >= 3, iterate 50 times
        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = md5.ComputeHash(hash);
            }
        }

        // Step d: Use first n bytes as RC4 key
        byte[] rc4Key = hash[..keyLength];

        // Step e: Pad user password
        byte[] paddedUser = PadPassword(_userPassword);

        // Step f: RC4 encrypt
        byte[] result = Rc4Encrypt(paddedUser, rc4Key);

        // Step g: For R >= 3, iterate with modified keys
        if (revision >= 3)
        {
            for (var i = 1; i <= 19; i++)
            {
                var iterKey = new byte[keyLength];
                for (var j = 0; j < keyLength; j++)
                {
                    iterKey[j] = (byte)(rc4Key[j] ^ i);
                }
                result = Rc4Encrypt(result, iterKey);
            }
        }

        return result;
    }

    private byte[] ComputeUValue(byte[] encryptionKey, byte[] documentId, int revision)
    {
        if (revision == 2)
        {
            // Algorithm 4: Simply RC4 encrypt the padding string
            return Rc4Encrypt(PdfPasswordPadding, encryptionKey);
        }
        else
        {
            // Algorithm 5 for R >= 3
            using var md5 = MD5.Create();

            // Step a: Create MD5 hash of padding + document ID
            using var hashInput = new MemoryStream();
            hashInput.Write(PdfPasswordPadding);
            hashInput.Write(documentId);
            byte[] hash = md5.ComputeHash(hashInput.ToArray());

            // Step b: RC4 encrypt with key
            byte[] result = Rc4Encrypt(hash, encryptionKey);

            // Step c: Iterate with XORed keys
            for (var i = 1; i <= 19; i++)
            {
                var iterKey = new byte[encryptionKey.Length];
                for (var j = 0; j < encryptionKey.Length; j++)
                {
                    iterKey[j] = (byte)(encryptionKey[j] ^ i);
                }
                result = Rc4Encrypt(result, iterKey);
            }

            // Step d: Pad to 32 bytes with arbitrary data
            var uValue = new byte[32];
            Array.Copy(result, uValue, 16);
            // Fill remaining 16 bytes with arbitrary padding
            for (var i = 16; i < 32; i++)
            {
                uValue[i] = (byte)(i - 16);
            }

            return uValue;
        }
    }

    private byte[] EncryptData(byte[] data, byte[] encryptionKey, int objectNumber, int generationNumber)
    {
        // Algorithm 1 from ISO 32000-1: Compute object-specific key
        using var md5 = MD5.Create();
        using var keyInput = new MemoryStream();

        keyInput.Write(encryptionKey);
        keyInput.WriteByte((byte)(objectNumber & 0xFF));
        keyInput.WriteByte((byte)((objectNumber >> 8) & 0xFF));
        keyInput.WriteByte((byte)((objectNumber >> 16) & 0xFF));
        keyInput.WriteByte((byte)(generationNumber & 0xFF));
        keyInput.WriteByte((byte)((generationNumber >> 8) & 0xFF));

        if (_encryptionType == EncryptionType.Aes128)
        {
            // For AES, append "sAlT"
            keyInput.Write("sAlT"u8);
        }

        byte[] objectKey = md5.ComputeHash(keyInput.ToArray());
        int keyLength = Math.Min(encryptionKey.Length + 5, 16);
        byte[] finalKey = objectKey[..keyLength];

        if (_encryptionType == EncryptionType.Aes128 || _encryptionType == EncryptionType.Aes256)
        {
            return AesEncrypt(data, finalKey);
        }
        else
        {
            return Rc4Encrypt(data, finalKey);
        }
    }

    private static byte[] Rc4Encrypt(byte[] data, byte[] key)
    {
        // RC4 Key Scheduling Algorithm (KSA)
        var s = new byte[256];
        for (var i = 0; i < 256; i++) s[i] = (byte)i;

        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        // RC4 Pseudo-Random Generation Algorithm (PRGA)
        var result = new byte[data.Length];
        int x = 0, y = 0;
        for (var k = 0; k < data.Length; k++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            result[k] = (byte)(data[k] ^ s[(s[x] + s[y]) & 0xFF]);
        }

        return result;
    }

    private static byte[] AesEncrypt(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key.Length == 16 ? key : key[..16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Generate random IV
        var iv = new byte[16];
        RandomNumberGenerator.Fill(iv);
        aes.IV = iv;

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);

        // Prepend IV to encrypted data (as PDF expects)
        var result = new byte[iv.Length + encrypted.Length];
        Array.Copy(iv, result, iv.Length);
        Array.Copy(encrypted, 0, result, iv.Length, encrypted.Length);

        return result;
    }

    private static byte[] PadPassword(string password)
    {
        byte[] passwordBytes = Encoding.ASCII.GetBytes(password ?? "");
        var padded = new byte[32];

        int copyLength = Math.Min(passwordBytes.Length, 32);
        Array.Copy(passwordBytes, padded, copyLength);

        // Fill rest with standard padding
        var padIndex = 0;
        for (int i = copyLength; i < 32; i++)
        {
            padded[i] = PdfPasswordPadding[padIndex++];
        }

        return padded;
    }

    /// <summary>
    /// Standard PDF password padding string (from ISO 32000-1)
    /// </summary>
    private static readonly byte[] PdfPasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];
}
