using FontParser.Reader;

namespace PdfLibrary.Tests.Fonts.Embedded;

public class BigEndianReaderTests
{
    [Fact]
    public void ReadByte_ReturnsCorrectValue()
    {
        // Arrange
        byte[] data = [0x12, 0x34, 0x56];
        var reader = new BigEndianReader(data);

        // Act & Assert
        Assert.Equal(0x12, reader.ReadByte());
        Assert.Equal(0x34, reader.ReadByte());
        Assert.Equal(0x56, reader.ReadByte());
    }

    [Fact]
    public void ReadUShort_BigEndian_ReturnsCorrectValue()
    {
        // Arrange
        byte[] data = [0x12, 0x34, 0x56, 0x78];
        var reader = new BigEndianReader(data);

        // Act
        ushort value1 = reader.ReadUShort();
        ushort value2 = reader.ReadUShort();

        // Assert
        Assert.Equal(0x1234, value1);
        Assert.Equal(0x5678, value2);
    }

    [Fact]
    public void ReadShort_BigEndian_ReturnsCorrectValue()
    {
        // Arrange
        byte[] data = [0xFF, 0xFE]; // -2 in two's complement
        var reader = new BigEndianReader(data);

        // Act
        short value = reader.ReadShort();

        // Assert
        Assert.Equal(-2, value);
    }

    [Fact]
    public void ReadUInt24_BigEndian_ReturnsCorrectValue()
    {
        // Arrange
        byte[] data = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];
        var reader = new BigEndianReader(data);

        // Act
        uint value1 = reader.ReadUInt24();
        uint value2 = reader.ReadUInt24();

        // Assert
        Assert.Equal(0x123456u, value1);
        Assert.Equal(0x789ABCu, value2);
    }

    [Fact]
    public void ReadUInt24_WithSmallValue_ReturnsCorrectValue()
    {
        // Arrange
        byte[] data = [0x00, 0x00, 0xFF]; // 255
        var reader = new BigEndianReader(data);

        // Act
        uint value = reader.ReadUInt24();

        // Assert
        Assert.Equal(255u, value);
    }

    [Fact]
    public void ReadUInt24_WithMaxValue_ReturnsCorrectValue()
    {
        // Arrange
        byte[] data = [0xFF, 0xFF, 0xFF]; // 16,777,215
        var reader = new BigEndianReader(data);

        // Act
        uint value = reader.ReadUInt24();

        // Assert
        Assert.Equal(16777215u, value);
    }

    [Fact]
    public void ReadUInt32_BigEndian_ReturnsCorrectValue()
    {
        // Arrange
        byte[] data = [0x12, 0x34, 0x56, 0x78];
        var reader = new BigEndianReader(data);

        // Act
        uint value = reader.ReadUInt32();

        // Assert
        Assert.Equal(0x12345678u, value);
    }

    [Fact]
    public void ReadInt32_BigEndian_ReturnsCorrectValue()
    {
        // Arrange
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFE]; // -2 in two's complement
        var reader = new BigEndianReader(data);

        // Act
        int value = reader.ReadInt32();

        // Assert
        Assert.Equal(-2, value);
    }

    [Fact]
    public void Seek_UpdatesPosition()
    {
        // Arrange
        byte[] data = [0x00, 0x01, 0x02, 0x03, 0x04];
        var reader = new BigEndianReader(data);

        // Act
        reader.Seek(3);
        byte value = reader.ReadByte();

        // Assert
        Assert.Equal(0x03, value);
    }

    [Fact]
    public void ReadBytes_ReturnsCorrectSubArray()
    {
        // Arrange
        byte[] data = [0x10, 0x20, 0x30, 0x40, 0x50];
        var reader = new BigEndianReader(data);

        // Act
        reader.Seek(1);
        byte[] result = reader.ReadBytes(3);

        // Assert
        Assert.Equal(" 0@"u8.ToArray(), result);
    }

    [Fact]
    public void Position_TracksReadOperations()
    {
        // Arrange
        // Needs 6 bytes: 1 byte + 2 bytes (ushort) + 3 bytes (uint24) = 6 bytes
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        var reader = new BigEndianReader(data);

        // Act & Assert
        Assert.Equal(0, reader.Position);
        reader.ReadByte();
        Assert.Equal(1, reader.Position);
        reader.ReadUShort();
        Assert.Equal(3, reader.Position);
        reader.ReadUInt24();
        Assert.Equal(6, reader.Position); // Should be past end, but position updated
    }

    [Fact]
    public void BytesRemaining_ReturnsCorrectCount()
    {
        // Arrange
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        var reader = new BigEndianReader(data);

        // Act & Assert
        Assert.Equal(5, reader.BytesRemaining);
        reader.ReadByte();
        Assert.Equal(4, reader.BytesRemaining);
        reader.ReadUShort();
        Assert.Equal(2, reader.BytesRemaining);
    }

    [Fact]
    public void ReadUShortArray_ReturnsCorrectValues()
    {
        // Arrange
        byte[] data = [0x00, 0x01, 0x00, 0x02, 0x00, 0x03];
        var reader = new BigEndianReader(data);

        // Act
        ushort[] result = reader.ReadUShortArray(3);

        // Assert
        Assert.Equal(new ushort[] { 1, 2, 3 }, result);
    }

    [Fact]
    public void ReadF16Dot16_ConvertsFixedPointCorrectly()
    {
        // Arrange
        byte[] data = [0x00, 0x01, 0x00, 0x00]; // 1.0 in 16.16 fixed point
        var reader = new BigEndianReader(data);

        // Act
        float value = reader.ReadF16Dot16();

        // Assert
        Assert.Equal(1.0f, value, 0.0001f);
    }

    [Fact]
    public void ReadF16Dot16_ConvertsNegativeFixedPointCorrectly()
    {
        // Arrange
        byte[] data = [0xFF, 0xFF, 0x00, 0x00]; // -1.0 in 16.16 fixed point
        var reader = new BigEndianReader(data);

        // Act
        float value = reader.ReadF16Dot16();

        // Assert
        Assert.Equal(-1.0f, value, 0.0001f);
    }
}
