using PdfLibrary.Security;

namespace PdfLibrary.Tests.Security;

public class RC4Tests
{
    [Fact]
    public void Process_ShouldBeSymmetric()
    {
        // Arrange
        byte[] key = "SecretKey"u8.ToArray();
        byte[] plaintext = "Hello, World!"u8.ToArray();
        byte[] data = (byte[])plaintext.Clone();

        // Act - encrypt
        var rc4Encrypt = new RC4(key);
        rc4Encrypt.Process(data);

        // Verify data changed
        Assert.NotEqual(plaintext, data);

        // Act - decrypt (create new RC4 instance with same key)
        var rc4Decrypt = new RC4(key);
        rc4Decrypt.Process(data);

        // Assert - should be back to original
        Assert.Equal(plaintext, data);
    }

    [Fact]
    public void ProcessCopy_ShouldNotModifyOriginal()
    {
        // Arrange
        byte[] key = "TestKey"u8.ToArray();
        byte[] original = "Original Data"u8.ToArray();
        byte[] originalCopy = (byte[])original.Clone();

        // Act
        var rc4 = new RC4(key);
        byte[] encrypted = rc4.ProcessCopy(original);

        // Assert - original should be unchanged
        Assert.Equal(originalCopy, original);
        Assert.NotEqual(original, encrypted);
    }

    [Fact]
    public void ProcessCopy_EncryptThenDecrypt_ShouldReturnOriginal()
    {
        // Arrange
        byte[] key = "AnotherKey"u8.ToArray();
        byte[] plaintext = "Test data for RC4 encryption"u8.ToArray();

        // Act
        var rc4Encrypt = new RC4(key);
        byte[] encrypted = rc4Encrypt.ProcessCopy(plaintext);

        var rc4Decrypt = new RC4(key);
        byte[] decrypted = rc4Decrypt.ProcessCopy(encrypted);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Constructor_WithNullKey_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RC4(null!));
    }

    [Fact]
    public void Constructor_WithEmptyKey_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new RC4([]));
    }

    [Fact]
    public void Process_WithNullData_ShouldThrow()
    {
        // Arrange
        var rc4 = new RC4([1, 2, 3]);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => rc4.Process(null!));
    }

    [Fact]
    public void Process_WithEmptyData_ShouldNotThrow()
    {
        // Arrange
        var rc4 = new RC4([1, 2, 3]);
        byte[] emptyData = [];

        // Act & Assert - should not throw
        rc4.Process(emptyData);
    }

    [Fact]
    public void Process_With40BitKey_ShouldWork()
    {
        // Arrange - PDF uses 40-bit (5-byte) keys for V=1 encryption
        byte[] key = [0x01, 0x02, 0x03, 0x04, 0x05];
        byte[] data = "Test"u8.ToArray();
        byte[] originalData = (byte[])data.Clone();

        // Act
        var rc4Encrypt = new RC4(key);
        rc4Encrypt.Process(data);

        var rc4Decrypt = new RC4(key);
        rc4Decrypt.Process(data);

        // Assert
        Assert.Equal(originalData, data);
    }

    [Fact]
    public void Process_With128BitKey_ShouldWork()
    {
        // Arrange - PDF uses 128-bit (16-byte) keys for V=2/V=3 encryption
        byte[] key = new byte[16];
        for (int i = 0; i < 16; i++) key[i] = (byte)i;

        byte[] data = "Test data for 128-bit RC4"u8.ToArray();
        byte[] originalData = (byte[])data.Clone();

        // Act
        var rc4Encrypt = new RC4(key);
        rc4Encrypt.Process(data);

        var rc4Decrypt = new RC4(key);
        rc4Decrypt.Process(data);

        // Assert
        Assert.Equal(originalData, data);
    }

    [Fact]
    public void Process_KnownTestVector_ShouldMatchExpected()
    {
        // Arrange - RFC 6229 test vector
        // Key: "Key" (0x4B, 0x65, 0x79)
        // Plaintext: "Plaintext" (0x50, 0x6C, 0x61, 0x69, 0x6E, 0x74, 0x65, 0x78, 0x74)
        byte[] key = "Key"u8.ToArray();
        byte[] plaintext = "Plaintext"u8.ToArray();

        // Known RC4 output for "Key" encrypting "Plaintext"
        // Computed using reference implementation
        byte[] expectedCiphertext = [0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3];

        // Act
        var rc4 = new RC4(key);
        byte[] ciphertext = rc4.ProcessCopy(plaintext);

        // Assert
        Assert.Equal(expectedCiphertext, ciphertext);
    }

    [Fact]
    public void Process_LargeData_ShouldWork()
    {
        // Arrange
        byte[] key = "LargeDataKey"u8.ToArray();
        byte[] largeData = new byte[10000];
        Random.Shared.NextBytes(largeData);
        byte[] originalData = (byte[])largeData.Clone();

        // Act
        var rc4Encrypt = new RC4(key);
        rc4Encrypt.Process(largeData);

        var rc4Decrypt = new RC4(key);
        rc4Decrypt.Process(largeData);

        // Assert
        Assert.Equal(originalData, largeData);
    }

    [Fact]
    public void Process_DifferentKeys_ShouldProduceDifferentOutput()
    {
        // Arrange
        byte[] key1 = "Key1"u8.ToArray();
        byte[] key2 = "Key2"u8.ToArray();
        byte[] data = "Same plaintext"u8.ToArray();

        // Act
        var rc4_1 = new RC4(key1);
        byte[] encrypted1 = rc4_1.ProcessCopy(data);

        var rc4_2 = new RC4(key2);
        byte[] encrypted2 = rc4_2.ProcessCopy(data);

        // Assert
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Process_StateIsPreserved_ShouldContinueKeystream()
    {
        // Arrange - process data in chunks vs all at once
        byte[] key = "ChunkKey"u8.ToArray();
        byte[] fullData = "This is a longer piece of test data for chunked processing"u8.ToArray();

        // Process all at once
        var rc4Full = new RC4(key);
        byte[] encryptedFull = rc4Full.ProcessCopy(fullData);

        // Process in two chunks
        byte[] chunk1 = fullData[..20];
        byte[] chunk2 = fullData[20..];

        var rc4Chunked = new RC4(key);
        rc4Chunked.Process(chunk1);
        rc4Chunked.Process(chunk2);

        // Combine chunks
        byte[] encryptedChunked = new byte[fullData.Length];
        Array.Copy(chunk1, 0, encryptedChunked, 0, chunk1.Length);
        Array.Copy(chunk2, 0, encryptedChunked, chunk1.Length, chunk2.Length);

        // Assert - both methods should produce same result
        Assert.Equal(encryptedFull, encryptedChunked);
    }
}
