using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;

namespace PdfLibrary.Tests;

/// <summary>
/// Comprehensive tests for PdfParser (ISO 32000-1:2008 section 7.3)
/// Tests parsing of all PDF object types and structures
/// </summary>
public class PdfParserTests
{
    private static PdfParser CreateParser(string content)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        MemoryStream stream = new(bytes);
        return new PdfParser(stream);
    }

    #region Basic Types

    [Fact]
    public void Parser_ParsesNull()
    {
        var parser = CreateParser("null");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfNull>(obj);
    }

    [Fact]
    public void Parser_ParsesBooleanTrue()
    {
        var parser = CreateParser("true");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfBoolean>(obj);
        Assert.True(((PdfBoolean)obj).Value);
    }

    [Fact]
    public void Parser_ParsesBooleanFalse()
    {
        var parser = CreateParser("false");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfBoolean>(obj);
        Assert.False(((PdfBoolean)obj).Value);
    }

    [Fact]
    public void Parser_ParsesInteger()
    {
        var parser = CreateParser("42");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfInteger>(obj);
        Assert.Equal(42, ((PdfInteger)obj).Value);
    }

    [Fact]
    public void Parser_ParsesNegativeInteger()
    {
        var parser = CreateParser("-123");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfInteger>(obj);
        Assert.Equal(-123, ((PdfInteger)obj).Value);
    }

    [Fact]
    public void Parser_ParsesReal()
    {
        var parser = CreateParser("3.14");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfReal>(obj);
        Assert.Equal(3.14, ((PdfReal)obj).Value);
    }

    [Fact]
    public void Parser_ParsesNegativeReal()
    {
        var parser = CreateParser("-2.5");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfReal>(obj);
        Assert.Equal(-2.5, ((PdfReal)obj).Value);
    }

    [Fact]
    public void Parser_ParsesLiteralString()
    {
        var parser = CreateParser("(Hello World)");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfString>(obj);
        Assert.Equal("Hello World", ((PdfString)obj).Value);
    }

    [Fact]
    public void Parser_ParsesHexString()
    {
        var parser = CreateParser("<48656C6C6F>"); // "Hello" in hex
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfString>(obj);
        Assert.Equal("Hello", ((PdfString)obj).Value);
    }

    [Fact]
    public void Parser_ParsesName()
    {
        var parser = CreateParser("/Type");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfName>(obj);
        Assert.Equal("Type", ((PdfName)obj).Value);
    }

    #endregion

    #region Arrays

    [Fact]
    public void Parser_ParsesEmptyArray()
    {
        var parser = CreateParser("[]");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfArray>(obj);
        Assert.Empty((PdfArray)obj);
    }

    [Fact]
    public void Parser_ParsesArrayWithIntegers()
    {
        var parser = CreateParser("[1 2 3 4 5]");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfArray>(obj);
        var array = (PdfArray)obj;
        Assert.Equal(5, array.Count);
        Assert.Equal(1, ((PdfInteger)array[0]).Value);
        Assert.Equal(5, ((PdfInteger)array[4]).Value);
    }

    [Fact]
    public void Parser_ParsesArrayWithMixedTypes()
    {
        var parser = CreateParser("[1 true (text) /Name 3.14]");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfArray>(obj);
        var array = (PdfArray)obj;
        Assert.Equal(5, array.Count);
        Assert.IsType<PdfInteger>(array[0]);
        Assert.IsType<PdfBoolean>(array[1]);
        Assert.IsType<PdfString>(array[2]);
        Assert.IsType<PdfName>(array[3]);
        Assert.IsType<PdfReal>(array[4]);
    }

    [Fact]
    public void Parser_ParsesNestedArray()
    {
        var parser = CreateParser("[1 [2 3] 4]");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfArray>(obj);
        var array = (PdfArray)obj;
        Assert.Equal(3, array.Count);
        Assert.IsType<PdfInteger>(array[0]);
        Assert.IsType<PdfArray>(array[1]);
        Assert.IsType<PdfInteger>(array[2]);

        var nested = (PdfArray)array[1];
        Assert.Equal(2, nested.Count);
        Assert.Equal(2, ((PdfInteger)nested[0]).Value);
        Assert.Equal(3, ((PdfInteger)nested[1]).Value);
    }

    [Fact]
    public void Parser_ParsesArrayWithIndirectReference()
    {
        var parser = CreateParser("[1 2 0 R 3]");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfArray>(obj);
        var array = (PdfArray)obj;
        Assert.Equal(3, array.Count);
        Assert.IsType<PdfInteger>(array[0]);
        Assert.IsType<PdfIndirectReference>(array[1]);
        Assert.IsType<PdfInteger>(array[2]);

        var reference = (PdfIndirectReference)array[1];
        Assert.Equal(2, reference.ObjectNumber);
        Assert.Equal(0, reference.GenerationNumber);
    }

    [Fact]
    public void Parser_ThrowsOnUnterminatedArray()
    {
        var parser = CreateParser("[1 2 3");
        Assert.Throws<PdfParseException>(() => parser.ReadObject());
    }

    #endregion

    #region Dictionaries

    [Fact]
    public void Parser_ParsesEmptyDictionary()
    {
        var parser = CreateParser("<<>>");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfDictionary>(obj);
        Assert.Empty((PdfDictionary)obj);
    }

    [Fact]
    public void Parser_ParsesDictionaryWithSingleEntry()
    {
        var parser = CreateParser("<</Type /Page>>");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfDictionary>(obj);
        var dict = (PdfDictionary)obj;
        Assert.Single(dict);
        Assert.True(dict.ContainsKey(PdfName.TypeName));
        Assert.IsType<PdfName>(dict[PdfName.TypeName]);
        Assert.Equal("Page", ((PdfName)dict[PdfName.TypeName]).Value);
    }

    [Fact]
    public void Parser_ParsesDictionaryWithMultipleEntries()
    {
        var parser = CreateParser("<</Type /Page /Count 10 /Title (Test)>>");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfDictionary>(obj);
        var dict = (PdfDictionary)obj;
        Assert.Equal(3, dict.Count);
        Assert.True(dict.ContainsKey(PdfName.TypeName));
        Assert.True(dict.ContainsKey(new PdfName("Count")));
        Assert.True(dict.ContainsKey(new PdfName("Title")));
    }

    [Fact]
    public void Parser_ParsesDictionaryWithArrayValue()
    {
        var parser = CreateParser("<</Kids [1 0 R 2 0 R]>>");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfDictionary>(obj);
        var dict = (PdfDictionary)obj;
        Assert.Single(dict);
        Assert.IsType<PdfArray>(dict[new PdfName("Kids")]);
        var kids = (PdfArray)dict[new PdfName("Kids")];
        Assert.Equal(2, kids.Count);
        Assert.IsType<PdfIndirectReference>(kids[0]);
        Assert.IsType<PdfIndirectReference>(kids[1]);
    }

    [Fact]
    public void Parser_ParsesNestedDictionary()
    {
        var parser = CreateParser("<</Outer <</Inner 42>>>>");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfDictionary>(obj);
        var dict = (PdfDictionary)obj;
        Assert.Single(dict);
        Assert.IsType<PdfDictionary>(dict[new PdfName("Outer")]);
        var inner = (PdfDictionary)dict[new PdfName("Outer")];
        Assert.Single(inner);
        Assert.IsType<PdfInteger>(inner[new PdfName("Inner")]);
        Assert.Equal(42, ((PdfInteger)inner[new PdfName("Inner")]).Value);
    }

    [Fact]
    public void Parser_ThrowsOnDictionaryKeyNotName()
    {
        var parser = CreateParser("<<42 /Value>>");
        Assert.Throws<PdfParseException>(() => parser.ReadObject());
    }

    [Fact]
    public void Parser_ThrowsOnUnterminatedDictionary()
    {
        var parser = CreateParser("<</Type /Page");
        Assert.Throws<PdfParseException>(() => parser.ReadObject());
    }

    #endregion

    #region Indirect References

    [Fact]
    public void Parser_ParsesIndirectReference()
    {
        var parser = CreateParser("5 0 R");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfIndirectReference>(obj);
        var reference = (PdfIndirectReference)obj;
        Assert.Equal(5, reference.ObjectNumber);
        Assert.Equal(0, reference.GenerationNumber);
    }

    [Fact]
    public void Parser_ParsesIndirectReferenceWithNonZeroGeneration()
    {
        var parser = CreateParser("10 5 R");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfIndirectReference>(obj);
        var reference = (PdfIndirectReference)obj;
        Assert.Equal(10, reference.ObjectNumber);
        Assert.Equal(5, reference.GenerationNumber);
    }

    [Fact]
    public void Parser_ParsesTwoIntegersNotFollowedByR()
    {
        var parser = CreateParser("10 20");
        PdfObject? obj1 = parser.ReadObject();
        PdfObject? obj2 = parser.ReadObject();

        Assert.NotNull(obj1);
        Assert.IsType<PdfInteger>(obj1);
        Assert.Equal(10, ((PdfInteger)obj1).Value);

        Assert.NotNull(obj2);
        Assert.IsType<PdfInteger>(obj2);
        Assert.Equal(20, ((PdfInteger)obj2).Value);
    }

    #endregion

    #region Indirect Object Definitions

    [Fact]
    public void Parser_ParsesIndirectObjectDefinition()
    {
        var parser = CreateParser("5 0 obj\n42\nendobj");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfInteger>(obj);
        Assert.Equal(42, ((PdfInteger)obj).Value);
        Assert.True(obj.IsIndirect);
        Assert.Equal(5, obj.ObjectNumber);
        Assert.Equal(0, obj.GenerationNumber);
    }

    [Fact]
    public void Parser_ParsesIndirectObjectWithDictionary()
    {
        var parser = CreateParser("3 0 obj\n<</Type /Page>>\nendobj");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfDictionary>(obj);
        Assert.True(obj.IsIndirect);
        Assert.Equal(3, obj.ObjectNumber);
        Assert.Equal(0, obj.GenerationNumber);

        var dict = (PdfDictionary)obj;
        Assert.Single(dict);
        Assert.Equal("Page", ((PdfName)dict[PdfName.TypeName]).Value);
    }

    [Fact]
    public void Parser_ParsesIndirectObjectWithArray()
    {
        var parser = CreateParser("7 2 obj\n[1 2 3]\nendobj");
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfArray>(obj);
        Assert.True(obj.IsIndirect);
        Assert.Equal(7, obj.ObjectNumber);
        Assert.Equal(2, obj.GenerationNumber);

        var array = (PdfArray)obj;
        Assert.Equal(3, array.Count);
    }

    [Fact]
    public void Parser_ThrowsOnMissingEndobj()
    {
        var parser = CreateParser("5 0 obj\n42");
        Assert.Throws<PdfParseException>(() => parser.ReadObject());
    }

    #endregion

    #region Streams

    [Fact]
    public void Parser_ParsesSimpleStream()
    {
        var content = "<</Length 5>>\nstream\nHello\nendstream";
        var parser = CreateParser(content);
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfStream>(obj);
        var stream = (PdfStream)obj;
        Assert.Equal(5, stream.Length);
        Assert.Equal("Hello", Encoding.ASCII.GetString(stream.Data));
    }

    [Fact]
    public void Parser_ParsesStreamWithFilter()
    {
        var content = "<</Length 10 /Filter /FlateDecode>>\nstream\n0123456789\nendstream";
        var parser = CreateParser(content);
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfStream>(obj);
        var stream = (PdfStream)obj;
        Assert.Equal(10, stream.Length);
        Assert.True(stream.Dictionary.ContainsKey(PdfName.Filter));
    }

    [Fact]
    public void Parser_ParsesStreamWithMultipleFilters()
    {
        var content = "<</Length 4 /Filter [/ASCII85Decode /FlateDecode]>>\nstream\ndata\nendstream";
        var parser = CreateParser(content);
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfStream>(obj);
        var stream = (PdfStream)obj;
        Assert.True(stream.Dictionary.ContainsKey(PdfName.Filter));
        Assert.IsType<PdfArray>(stream.Dictionary[PdfName.Filter]);
    }

    [Fact]
    public void Parser_ThrowsOnStreamMissingLength()
    {
        var content = "<<>>\nstream\ndata\nendstream";
        var parser = CreateParser(content);
        Assert.Throws<PdfParseException>(() => parser.ReadObject());
    }

    [Fact]
    public void Parser_ThrowsOnStreamWithIndirectLength()
    {
        var content = "<</Length 5 0 R>>\nstream\ndata\nendstream";
        var parser = CreateParser(content);
        Assert.Throws<PdfParseException>(() => parser.ReadObject());
    }

    #endregion

    #region Complex Integration Tests

    [Fact]
    public void Parser_ParsesMultipleObjectsInSequence()
    {
        var parser = CreateParser("42 true /Name [1 2]");
        List<PdfObject> objects = parser.ReadAllObjects();

        Assert.Equal(4, objects.Count);
        Assert.IsType<PdfInteger>(objects[0]);
        Assert.IsType<PdfBoolean>(objects[1]);
        Assert.IsType<PdfName>(objects[2]);
        Assert.IsType<PdfArray>(objects[3]);
    }

    [Fact]
    public void Parser_ParsesComplexNestedStructure()
    {
        var content = @"<<
            /Type /Page
            /MediaBox [0 0 612 792]
            /Contents 5 0 R
            /Resources <<
                /Font <<
                    /F1 10 0 R
                >>
            >>
        >>";
        var parser = CreateParser(content);
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfDictionary>(obj);
        var dict = (PdfDictionary)obj;

        Assert.True(dict.ContainsKey(PdfName.TypeName));
        Assert.True(dict.ContainsKey(new PdfName("MediaBox")));
        Assert.True(dict.ContainsKey(new PdfName("Contents")));
        Assert.True(dict.ContainsKey(new PdfName("Resources")));

        var mediaBox = (PdfArray)dict[new PdfName("MediaBox")];
        Assert.Equal(4, mediaBox.Count);

        var contents = (PdfIndirectReference)dict[new PdfName("Contents")];
        Assert.Equal(5, contents.ObjectNumber);

        var resources = (PdfDictionary)dict[new PdfName("Resources")];
        Assert.True(resources.ContainsKey(new PdfName("Font")));
    }

    [Fact]
    public void Parser_ParsesRealWorldPageObject()
    {
        var content = @"1 0 obj
<<
    /Type /Page
    /Parent 2 0 R
    /MediaBox [0 0 612 792]
    /Contents 3 0 R
    /Resources <<
        /ProcSet [/PDF /Text]
        /Font <<
            /F1 4 0 R
        >>
    >>
>>
endobj";
        var parser = CreateParser(content);
        PdfObject? obj = parser.ReadObject();

        Assert.NotNull(obj);
        Assert.IsType<PdfDictionary>(obj);
        Assert.True(obj.IsIndirect);
        Assert.Equal(1, obj.ObjectNumber);
        Assert.Equal(0, obj.GenerationNumber);

        var dict = (PdfDictionary)obj;
        Assert.Equal("Page", ((PdfName)dict[PdfName.TypeName]).Value);

        var parent = (PdfIndirectReference)dict[new PdfName("Parent")];
        Assert.Equal(2, parent.ObjectNumber);

        var resources = (PdfDictionary)dict[new PdfName("Resources")];
        var procSet = (PdfArray)resources[new PdfName("ProcSet")];
        Assert.Equal(2, procSet.Count);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void Parser_ReturnsNullOnEmptyStream()
    {
        var parser = CreateParser("");
        PdfObject? obj = parser.ReadObject();
        Assert.Null(obj);
    }

    [Fact]
    public void Parser_ReturnsNullOnEOF()
    {
        var parser = CreateParser("42");
        parser.ReadObject(); // Read the integer
        PdfObject? obj = parser.ReadObject(); // Should return null
        Assert.Null(obj);
    }

    [Fact]
    public void Parser_ThrowsOnInvalidTokenInObjectContext()
    {
        var parser = CreateParser("]");
        Assert.Throws<PdfParseException>(() => parser.ReadObject());
    }

    #endregion
}
