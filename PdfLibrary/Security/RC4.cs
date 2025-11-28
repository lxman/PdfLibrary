namespace PdfLibrary.Security;

/// <summary>
/// RC4 stream cipher implementation for PDF decryption.
/// RC4 is used in PDF encryption versions 1-3 (40-128 bit keys).
/// Note: RC4 is considered insecure for new applications but is required for legacy PDF support.
/// </summary>
public sealed class RC4
{
    private readonly byte[] _s = new byte[256];
    private int _i;
    private int _j;

    /// <summary>
    /// Initializes RC4 with the given key using Key Scheduling Algorithm (KSA).
    /// </summary>
    /// <param name="key">Encryption key (typically 5-16 bytes for PDF)</param>
    public RC4(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
            throw new ArgumentException("Key cannot be empty", nameof(key));

        // Initialize S-box with identity permutation
        for (var i = 0; i < 256; i++)
            _s[i] = (byte)i;

        // Key Scheduling Algorithm (KSA)
        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + _s[i] + key[i % key.Length]) & 0xFF;
            (_s[i], _s[j]) = (_s[j], _s[i]); // Swap
        }

        _i = 0;
        _j = 0;
    }

    /// <summary>
    /// Encrypts or decrypts data in place using Pseudo-Random Generation Algorithm (PRGA).
    /// RC4 is symmetric, so encryption and decryption are the same operation.
    /// </summary>
    /// <param name="data">Data to encrypt/decrypt (modified in place)</param>
    public void Process(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        for (var k = 0; k < data.Length; k++)
        {
            _i = (_i + 1) & 0xFF;
            _j = (_j + _s[_i]) & 0xFF;
            (_s[_i], _s[_j]) = (_s[_j], _s[_i]); // Swap

            int keyByte = _s[(_s[_i] + _s[_j]) & 0xFF];
            data[k] ^= (byte)keyByte;
        }
    }

    /// <summary>
    /// Encrypts or decrypts data, returning a new array.
    /// </summary>
    /// <param name="data">Input data</param>
    /// <returns>Encrypted/decrypted output</returns>
    public byte[] ProcessCopy(byte[] data)
    {
        var result = new byte[data.Length];
        Array.Copy(data, result, data.Length);
        Process(result);
        return result;
    }
}
