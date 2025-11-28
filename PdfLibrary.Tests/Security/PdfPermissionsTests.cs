using PdfLibrary.Security;

namespace PdfLibrary.Tests.Security;

public class PdfPermissionsTests
{
    [Fact]
    public void AllPermissions_ShouldHaveAllFlagsSet()
    {
        // Arrange & Act
        var permissions = PdfPermissions.AllPermissions;

        // Assert
        Assert.True(permissions.CanPrint);
        Assert.True(permissions.CanPrintHighQuality);
        Assert.True(permissions.CanModifyContents);
        Assert.True(permissions.CanCopyContent);
        Assert.True(permissions.CanModifyAnnotations);
        Assert.True(permissions.CanFillForms);
        Assert.True(permissions.CanExtractForAccessibility);
        Assert.True(permissions.CanAssembleDocument);
    }

    [Fact]
    public void Constructor_WithZero_ShouldHaveNoPermissions()
    {
        // Arrange & Act
        var permissions = new PdfPermissions(0);

        // Assert
        Assert.False(permissions.CanPrint);
        Assert.False(permissions.CanPrintHighQuality);
        Assert.False(permissions.CanModifyContents);
        Assert.False(permissions.CanCopyContent);
        Assert.False(permissions.CanModifyAnnotations);
        Assert.False(permissions.CanFillForms);
        Assert.False(permissions.CanExtractForAccessibility);
        Assert.False(permissions.CanAssembleDocument);
    }

    [Fact]
    public void Constructor_WithPrintBit_ShouldAllowPrint()
    {
        // Arrange - bit 3 is print (1 << 2 = 4)
        int pValue = 0b00000100;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanPrint);
        Assert.False(permissions.CanCopyContent);
        Assert.False(permissions.CanModifyContents);
    }

    [Fact]
    public void Constructor_WithCopyBit_ShouldAllowCopy()
    {
        // Arrange - bit 5 is copy (1 << 4 = 16)
        int pValue = 0b00010000;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanCopyContent);
        Assert.False(permissions.CanPrint);
    }

    [Fact]
    public void Constructor_WithModifyBit_ShouldAllowModify()
    {
        // Arrange - bit 4 is modify (1 << 3 = 8)
        int pValue = 0b00001000;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanModifyContents);
        Assert.False(permissions.CanPrint);
    }

    [Fact]
    public void Constructor_WithAnnotationsBit_ShouldAllowAnnotations()
    {
        // Arrange - bit 6 is annotations (1 << 5 = 32)
        int pValue = 0b00100000;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanModifyAnnotations);
    }

    [Fact]
    public void Constructor_WithFillFormsBit_ShouldAllowFillForms()
    {
        // Arrange - bit 9 is fill forms (1 << 8 = 256)
        int pValue = 0b100000000;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanFillForms);
    }

    [Fact]
    public void Constructor_WithAccessibilityBit_ShouldAllowAccessibility()
    {
        // Arrange - bit 10 is accessibility (1 << 9 = 512)
        int pValue = 0b1000000000;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanExtractForAccessibility);
    }

    [Fact]
    public void Constructor_WithAssembleBit_ShouldAllowAssemble()
    {
        // Arrange - bit 11 is assemble (1 << 10 = 1024)
        int pValue = 0b10000000000;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanAssembleDocument);
    }

    [Fact]
    public void Constructor_WithPrintHighQualityBit_ShouldAllowPrintHighQuality()
    {
        // Arrange - bit 12 is print high quality (1 << 11 = 2048)
        int pValue = 0b100000000000;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanPrintHighQuality);
    }

    [Fact]
    public void Constructor_WithTypicalRestrictedValue_ShouldParseCorrectly()
    {
        // Arrange - typical "print only" value: -3904 (0xFFFF0CC0)
        // This allows: print, print HQ, accessibility
        int pValue = unchecked((int)0xFFFFF0C0);

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert - only some permissions should be set
        Assert.False(permissions.CanCopyContent);
        Assert.False(permissions.CanModifyContents);
    }

    [Fact]
    public void Constructor_WithAllBitsSet_ShouldParseCorrectly()
    {
        // Arrange - all permission bits set: 0x0F3C (bits 3-12)
        int pValue = unchecked((int)0xFFFFFFFF);

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.True(permissions.CanPrint);
        Assert.True(permissions.CanPrintHighQuality);
        Assert.True(permissions.CanModifyContents);
        Assert.True(permissions.CanCopyContent);
        Assert.True(permissions.CanModifyAnnotations);
        Assert.True(permissions.CanFillForms);
        Assert.True(permissions.CanExtractForAccessibility);
        Assert.True(permissions.CanAssembleDocument);
    }

    [Fact]
    public void RawValue_ShouldReturnOriginalValue()
    {
        // Arrange
        int pValue = -3904;

        // Act
        var permissions = new PdfPermissions(pValue);

        // Assert
        Assert.Equal(pValue, permissions.RawValue);
    }

    [Fact]
    public void ToString_WithNoPermissions_ShouldReturnNone()
    {
        // Arrange
        var permissions = new PdfPermissions(0);

        // Act
        string result = permissions.ToString();

        // Assert
        Assert.Equal("None", result);
    }

    [Fact]
    public void ToString_WithSomePermissions_ShouldListThem()
    {
        // Arrange - print and copy
        int pValue = 0b00010100; // bits 3 and 5

        // Act
        var permissions = new PdfPermissions(pValue);
        string result = permissions.ToString();

        // Assert
        Assert.Contains("Print", result);
        Assert.Contains("Copy", result);
    }

    [Theory]
    [InlineData(PdfPermissionFlags.Print, true, false, false, false)]
    [InlineData(PdfPermissionFlags.CopyContent, false, true, false, false)]
    [InlineData(PdfPermissionFlags.ModifyContents, false, false, true, false)]
    [InlineData(PdfPermissionFlags.ModifyAnnotations, false, false, false, true)]
    public void Flags_ShouldMatchIndividualProperties(
        PdfPermissionFlags flag,
        bool expectPrint,
        bool expectCopy,
        bool expectModify,
        bool expectAnnotate)
    {
        // Arrange
        var permissions = new PdfPermissions((int)flag);

        // Assert
        Assert.Equal(expectPrint, permissions.CanPrint);
        Assert.Equal(expectCopy, permissions.CanCopyContent);
        Assert.Equal(expectModify, permissions.CanModifyContents);
        Assert.Equal(expectAnnotate, permissions.CanModifyAnnotations);
    }
}
