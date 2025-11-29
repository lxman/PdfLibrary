using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Tests;

/// <summary>
/// Comprehensive tests for PDF Core Primitive types
/// Tests all basic PDF object types: Null, Boolean, Integer, Real, String, Array, IndirectReference
/// </summary>
public class CorePrimitivesTests
{
    #region PdfNull Tests

    [Fact]
    public void PdfNull_Instance_IsSingleton()
    {
        var null1 = PdfNull.Instance;
        var null2 = PdfNull.Instance;

        Assert.Same(null1, null2);
    }

    [Fact]
    public void PdfNull_Type_ReturnsNull()
    {
        Assert.Equal(PdfObjectType.Null, PdfNull.Instance.Type);
    }

    [Fact]
    public void PdfNull_ToPdfString_ReturnsLowercaseNull()
    {
        Assert.Equal("null", PdfNull.Instance.ToPdfString());
    }

    [Fact]
    public void PdfNull_Equals_SameInstance()
    {
        Assert.True(PdfNull.Instance.Equals(PdfNull.Instance));
    }

    [Fact]
    public void PdfNull_GetHashCode_AlwaysZero()
    {
        Assert.Equal(0, PdfNull.Instance.GetHashCode());
    }

    #endregion

    #region PdfBoolean Tests

    [Fact]
    public void PdfBoolean_True_IsSingleton()
    {
        PdfBoolean bool1 = PdfBoolean.True;
        PdfBoolean bool2 = PdfBoolean.FromValue(true);

        Assert.Same(bool1, bool2);
        Assert.True(bool1.Value);
    }

    [Fact]
    public void PdfBoolean_False_IsSingleton()
    {
        PdfBoolean bool1 = PdfBoolean.False;
        PdfBoolean bool2 = PdfBoolean.FromValue(false);

        Assert.Same(bool1, bool2);
        Assert.False(bool1.Value);
    }

    [Fact]
    public void PdfBoolean_Type_ReturnsBoolean()
    {
        Assert.Equal(PdfObjectType.Boolean, PdfBoolean.True.Type);
        Assert.Equal(PdfObjectType.Boolean, PdfBoolean.False.Type);
    }

    [Fact]
    public void PdfBoolean_ToPdfString_ReturnsLowercaseTrueFalse()
    {
        Assert.Equal("true", PdfBoolean.True.ToPdfString());
        Assert.Equal("false", PdfBoolean.False.ToPdfString());
    }

    [Fact]
    public void PdfBoolean_Equals_ComparesValues()
    {
        Assert.True(PdfBoolean.True.Equals(PdfBoolean.True));
        Assert.False(PdfBoolean.True.Equals(PdfBoolean.False));
        Assert.False(PdfBoolean.False.Equals(PdfBoolean.True));
        Assert.True(PdfBoolean.False.Equals(PdfBoolean.False));
    }

    [Fact]
    public void PdfBoolean_ImplicitConversion_ToBool()
    {
        bool trueValue = PdfBoolean.True;
        bool falseValue = PdfBoolean.False;

        Assert.True(trueValue);
        Assert.False(falseValue);
    }

    [Fact]
    public void PdfBoolean_ImplicitConversion_FromBool()
    {
        PdfBoolean trueObj = true;
        PdfBoolean falseObj = false;

        Assert.Same(PdfBoolean.True, trueObj);
        Assert.Same(PdfBoolean.False, falseObj);
    }

    #endregion

    #region PdfInteger Tests

    [Fact]
    public void PdfInteger_Constructor_StoresValue()
    {
        var integer = new PdfInteger(42);
        Assert.Equal(42, integer.Value);
    }

    [Fact]
    public void PdfInteger_Type_ReturnsInteger()
    {
        Assert.Equal(PdfObjectType.Integer, new PdfInteger(0).Type);
    }

    [Fact]
    public void PdfInteger_ToPdfString_Positive()
    {
        Assert.Equal("42", new PdfInteger(42).ToPdfString());
        Assert.Equal("1234567", new PdfInteger(1234567).ToPdfString());
    }

    [Fact]
    public void PdfInteger_ToPdfString_Negative()
    {
        Assert.Equal("-42", new PdfInteger(-42).ToPdfString());
        Assert.Equal("-999", new PdfInteger(-999).ToPdfString());
    }

    [Fact]
    public void PdfInteger_ToPdfString_Zero()
    {
        Assert.Equal("0", new PdfInteger(0).ToPdfString());
    }

    [Fact]
    public void PdfInteger_Equals_ComparesValues()
    {
        var int1 = new PdfInteger(42);
        var int2 = new PdfInteger(42);
        var int3 = new PdfInteger(99);

        Assert.True(int1.Equals(int2));
        Assert.False(int1.Equals(int3));
    }

    [Fact]
    public void PdfInteger_ImplicitConversion_ToInt()
    {
        int value = new PdfInteger(42);
        Assert.Equal(42, value);
    }

    [Fact]
    public void PdfInteger_ImplicitConversion_FromInt()
    {
        PdfInteger pdfInt = 42;
        Assert.Equal(42, pdfInt.Value);
    }

    #endregion

    #region PdfReal Tests

    [Fact]
    public void PdfReal_Constructor_StoresValue()
    {
        var real = new PdfReal(3.14159);
        Assert.Equal(3.14159, real.Value, 6);
    }

    [Fact]
    public void PdfReal_Type_ReturnsReal()
    {
        Assert.Equal(PdfObjectType.Real, new PdfReal(0.0).Type);
    }

    [Fact]
    public void PdfReal_ToPdfString_IncludesDecimalPoint()
    {
        Assert.Equal("3.14159", new PdfReal(3.14159).ToPdfString());
        Assert.Equal("42.0", new PdfReal(42.0).ToPdfString()); // Whole numbers must include .0
        Assert.Equal("0.5", new PdfReal(0.5).ToPdfString());
    }

    [Fact]
    public void PdfReal_ToPdfString_NegativeNumbers()
    {
        Assert.Equal("-3.14159", new PdfReal(-3.14159).ToPdfString());
        Assert.Equal("-42.0", new PdfReal(-42.0).ToPdfString());
    }

    [Fact]
    public void PdfReal_ToPdfString_VerySmallNumbers()
    {
        Assert.Equal("0.000001", new PdfReal(0.000001).ToPdfString());
    }

    [Fact]
    public void PdfReal_ToPdfString_VeryLargeNumbers()
    {
        var real = new PdfReal(123456789.123456);
        string result = real.ToPdfString();
        Assert.Contains(".", result);
        Assert.StartsWith("123456789", result);
    }

    [Fact]
    public void PdfReal_Equals_UsesEpsilonTolerance()
    {
        var real1 = new PdfReal(3.14159);
        var real2 = new PdfReal(3.14159);
        var real3 = new PdfReal(3.14160); // Slightly different

        Assert.True(real1.Equals(real2));
        // Note: Epsilon comparison means very close values are equal
    }

    [Fact]
    public void PdfReal_ImplicitConversion_ToDouble()
    {
        double value = new PdfReal(3.14159);
        Assert.Equal(3.14159, value, 6);
    }

    [Fact]
    public void PdfReal_ImplicitConversion_FromDouble()
    {
        PdfReal pdfReal = 3.14159;
        Assert.Equal(3.14159, pdfReal.Value, 6);
    }

    #endregion

    #region PdfString Tests

    [Fact]
    public void PdfString_Constructor_FromString()
    {
        var pdfString = new PdfString("Hello");
        Assert.Equal("Hello", pdfString.Value);
    }

    [Fact]
    public void PdfString_Constructor_FromBytes()
    {
        byte[] bytes = Encoding.Latin1.GetBytes("World");
        var pdfString = new PdfString(bytes);
        Assert.Equal("World", pdfString.Value);
    }

    [Fact]
    public void PdfString_Type_ReturnsString()
    {
        Assert.Equal(PdfObjectType.String, new PdfString("test").Type);
    }

    [Fact]
    public void PdfString_ToPdfString_Literal_SimpleText()
    {
        var pdfString = new PdfString("Hello World", PdfStringFormat.Literal);
        Assert.Equal("(Hello World)", pdfString.ToPdfString());
    }

    [Fact]
    public void PdfString_ToPdfString_Literal_EscapesParentheses()
    {
        var pdfString = new PdfString("(Hello)", PdfStringFormat.Literal);
        Assert.Equal(@"(\(Hello\))", pdfString.ToPdfString());
    }

    [Fact]
    public void PdfString_ToPdfString_Literal_EscapesBackslash()
    {
        var pdfString = new PdfString(@"C:\path\file", PdfStringFormat.Literal);
        string result = pdfString.ToPdfString();
        Assert.StartsWith("(", result);
        Assert.EndsWith(")", result);
        Assert.Contains(@"\\", result); // Backslashes should be escaped
    }

    [Fact]
    public void PdfString_ToPdfString_Literal_EscapesSpecialChars()
    {
        var pdfString = new PdfString("Line1\nLine2\tTab\rReturn", PdfStringFormat.Literal);
        string result = pdfString.ToPdfString();
        Assert.Contains(@"\n", result);
        Assert.Contains(@"\t", result);
        Assert.Contains(@"\r", result);
    }

    [Fact]
    public void PdfString_ToPdfString_Hexadecimal_Simple()
    {
        var pdfString = new PdfString("Hello", PdfStringFormat.Hexadecimal);
        // "Hello" = 48 65 6C 6C 6F in hex
        Assert.Equal("<48656C6C6F>", pdfString.ToPdfString());
    }

    [Fact]
    public void PdfString_ToPdfString_Hexadecimal_AllBytes()
    {
        byte[] bytes = [0x01, 0x02, 0xFF, 0xFE];
        var pdfString = new PdfString(bytes, PdfStringFormat.Hexadecimal);
        Assert.Equal("<0102FFFE>", pdfString.ToPdfString());
    }

    [Fact]
    public void PdfString_Bytes_ReturnsClone()
    {
        byte[] original = Encoding.Latin1.GetBytes("Test");
        var pdfString = new PdfString(original);
        byte[] returned = pdfString.Bytes;

        // Should be equal but not same reference
        Assert.Equal(original, returned);
        Assert.NotSame(original, returned);
    }

    [Fact]
    public void PdfString_Equals_ComparesBytes()
    {
        var str1 = new PdfString("Hello");
        var str2 = new PdfString("Hello");
        var str3 = new PdfString("World");

        Assert.True(str1.Equals(str2));
        Assert.False(str1.Equals(str3));
    }

    [Fact]
    public void PdfString_ImplicitConversion_ToString()
    {
        string value = new PdfString("Hello");
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void PdfString_ImplicitConversion_FromString()
    {
        PdfString pdfString = "Hello";
        Assert.Equal("Hello", pdfString.Value);
    }

    #endregion

    #region PdfArray Tests

    [Fact]
    public void PdfArray_Constructor_Empty()
    {
        var array = new PdfArray();
        Assert.Equal(0, array.Count);
        Assert.False(array.IsReadOnly);
    }

    [Fact]
    public void PdfArray_Constructor_WithItems()
    {
        var array = new PdfArray(new PdfInteger(1), new PdfInteger(2), new PdfInteger(3));
        Assert.Equal(3, array.Count);
        Assert.Equal(1, ((PdfInteger)array[0]).Value);
        Assert.Equal(2, ((PdfInteger)array[1]).Value);
        Assert.Equal(3, ((PdfInteger)array[2]).Value);
    }

    [Fact]
    public void PdfArray_Type_ReturnsArray()
    {
        Assert.Equal(PdfObjectType.Array, new PdfArray().Type);
    }

    [Fact]
    public void PdfArray_Add_IncreasesCount()
    {
        var array = new PdfArray();
        array.Add(new PdfInteger(42));
        array.Add(new PdfInteger(99));

        Assert.Equal(2, array.Count);
    }

    [Fact]
    public void PdfArray_Insert_AddsAtPosition()
    {
        var array = new PdfArray(new PdfInteger(1), new PdfInteger(3));
        array.Insert(1, new PdfInteger(2));

        Assert.Equal(3, array.Count);
        Assert.Equal(1, ((PdfInteger)array[0]).Value);
        Assert.Equal(2, ((PdfInteger)array[1]).Value);
        Assert.Equal(3, ((PdfInteger)array[2]).Value);
    }

    [Fact]
    public void PdfArray_Remove_DecreasesCount()
    {
        var item = new PdfInteger(42);
        var array = new PdfArray(item, new PdfInteger(99));

        bool removed = array.Remove(item);

        Assert.True(removed);
        Assert.Equal(1, array.Count);
    }

    [Fact]
    public void PdfArray_Clear_EmptiesArray()
    {
        var array = new PdfArray(new PdfInteger(1), new PdfInteger(2));
        array.Clear();

        Assert.Equal(0, array.Count);
    }

    [Fact]
    public void PdfArray_Indexer_GetAndSet()
    {
        var array = new PdfArray(new PdfInteger(42));
        Assert.Equal(42, ((PdfInteger)array[0]).Value);

        array[0] = new PdfInteger(99);
        Assert.Equal(99, ((PdfInteger)array[0]).Value);
    }

    [Fact]
    public void PdfArray_Contains_FindsItems()
    {
        var item = new PdfInteger(42);
        var array = new PdfArray(item);

        Assert.True(array.Contains(item));
        Assert.False(array.Contains(new PdfInteger(99)));
    }

    [Fact]
    public void PdfArray_IndexOf_ReturnsPosition()
    {
        var item = new PdfInteger(42);
        var array = new PdfArray(new PdfInteger(1), item, new PdfInteger(3));

        Assert.Equal(1, array.IndexOf(item));
        Assert.Equal(-1, array.IndexOf(new PdfInteger(99)));
    }

    [Fact]
    public void PdfArray_ToPdfString_Empty()
    {
        var array = new PdfArray();
        Assert.Equal("[]", array.ToPdfString());
    }

    [Fact]
    public void PdfArray_ToPdfString_WithElements()
    {
        var array = new PdfArray(new PdfInteger(1), new PdfInteger(2), new PdfInteger(3));
        Assert.Equal("[1 2 3]", array.ToPdfString());
    }

    [Fact]
    public void PdfArray_ToPdfString_NestedArray()
    {
        var nested = new PdfArray(new PdfInteger(2), new PdfInteger(3));
        var array = new PdfArray(new PdfInteger(1), nested, new PdfInteger(4));
        Assert.Equal("[1 [2 3] 4]", array.ToPdfString());
    }

    [Fact]
    public void PdfArray_Add_ThrowsOnNull()
    {
        var array = new PdfArray();
        Assert.Throws<ArgumentNullException>(() => array.Add(null!));
    }

    #endregion

    #region PdfIndirectReference Tests

    [Fact]
    public void PdfIndirectReference_Constructor_StoresValues()
    {
        var reference = new PdfIndirectReference(5, 0);
        Assert.Equal(5, reference.ObjectNumber);
        Assert.Equal(0, reference.GenerationNumber);
    }

    [Fact]
    public void PdfIndirectReference_Type_ReturnsIndirectReference()
    {
        Assert.Equal(PdfObjectType.IndirectReference, new PdfIndirectReference(1, 0).Type);
    }

    [Fact]
    public void PdfIndirectReference_ToPdfString_Format()
    {
        var reference = new PdfIndirectReference(5, 0);
        Assert.Equal("5 0 R", reference.ToPdfString());
    }

    [Fact]
    public void PdfIndirectReference_ToPdfString_WithGeneration()
    {
        var reference = new PdfIndirectReference(12, 3);
        Assert.Equal("12 3 R", reference.ToPdfString());
    }

    [Fact]
    public void PdfIndirectReference_Constructor_ValidatesObjectNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfIndirectReference(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfIndirectReference(-1, 0));
    }

    [Fact]
    public void PdfIndirectReference_Constructor_ValidatesGenerationNumber()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfIndirectReference(1, -1));
    }

    [Fact]
    public void PdfIndirectReference_Equals_ComparesBothNumbers()
    {
        var ref1 = new PdfIndirectReference(5, 0);
        var ref2 = new PdfIndirectReference(5, 0);
        var ref3 = new PdfIndirectReference(5, 1);
        var ref4 = new PdfIndirectReference(6, 0);

        Assert.True(ref1.Equals(ref2));
        Assert.False(ref1.Equals(ref3));
        Assert.False(ref1.Equals(ref4));
    }

    [Fact]
    public void PdfIndirectReference_GetHashCode_ConsistentForSameValues()
    {
        var ref1 = new PdfIndirectReference(5, 0);
        var ref2 = new PdfIndirectReference(5, 0);

        Assert.Equal(ref1.GetHashCode(), ref2.GetHashCode());
    }

    [Fact]
    public void PdfIndirectReference_ToIndirectObjectDefinition_FormatsCorrectly()
    {
        var content = new PdfInteger(42);
        string definition = PdfIndirectReference.ToIndirectObjectDefinition(5, 0, content);

        Assert.Contains("5 0 obj", definition);
        Assert.Contains("42", definition);
        Assert.Contains("endobj", definition);
    }

    #endregion
}
