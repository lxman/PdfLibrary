# PdfLibrary

A .NET 10 library for creating, parsing, and processing PDF files, implementing both PDF 1.7 (ISO 32000-1:2008) and PDF 2.0 (ISO 32000-2:2020) specifications.

## Project Status

### Completed
- ✅ Core PDF object model with all 8 primitive types
- ✅ PDF version detection and handling (1.x and 2.x)

### In Progress
- 🔨 PDF file structure (header, body, xref, trailer)
- 🔨 Lexer for tokenizing PDF syntax
- 🔨 Parser for PDF objects
- 🔨 Cross-reference table parsing
- 🔨 Stream encoding/decoding

## PDF Object Model

The library implements all 8 basic PDF object types as defined in ISO 32000:

### 1. Null Object (`PdfNull`)
```csharp
PdfNull nullObj = PdfNull.Instance;
// Output: null
```

### 2. Boolean Object (`PdfBoolean`)
```csharp
PdfBoolean trueObj = PdfBoolean.True;
PdfBoolean falseObj = PdfBoolean.False;
// Output: true, false
```

### 3. Numeric Objects

#### Integer (`PdfInteger`)
```csharp
PdfInteger num = new PdfInteger(42);
// or
PdfInteger num = 42; // implicit conversion
// Output: 42
```

#### Real (`PdfReal`)
```csharp
PdfReal real = new PdfReal(3.14159);
// or
PdfReal real = 3.14159; // implicit conversion
// Output: 3.14159
```

### 4. String Object (`PdfString`)
```csharp
// Literal string (enclosed in parentheses)
PdfString literal = new PdfString("Hello, PDF!");
// Output: (Hello, PDF!)

// Hexadecimal string (enclosed in angle brackets)
PdfString hex = new PdfString("Hello", PdfStringFormat.Hexadecimal);
// Output: <48656C6C6F>

// Automatic escaping of special characters
PdfString escaped = new PdfString("Line 1\nLine 2");
// Output: (Line 1\nLine 2)
```

### 5. Name Object (`PdfName`)
```csharp
PdfName name = new PdfName("Type");
// Output: /Type

// Names with spaces (automatically encoded)
PdfName spaceName = new PdfName("Lime Green");
// Output: /Lime#20Green

// Common names provided as static properties
PdfName type = PdfName.TypeName; // Represents /Type in PDF
PdfName subtype = PdfName.Subtype;
PdfName length = PdfName.Length;
```

### 6. Array Object (`PdfArray`)
```csharp
// Heterogeneous array
PdfArray array = new PdfArray
{
    new PdfInteger(549),
    new PdfReal(3.14),
    PdfBoolean.False,
    new PdfString("Ralph"),
    new PdfName("SomeName")
};
// Output: [549 3.14 false (Ralph) /SomeName]
```

### 7. Dictionary Object (`PdfDictionary`)
```csharp
PdfDictionary dict = new PdfDictionary
{
    [PdfName.TypeName] = new PdfName("Example"),
    [new PdfName("Version")] = new PdfReal(0.01),
    [new PdfName("IntegerItem")] = new PdfInteger(12),
    [new PdfName("StringItem")] = new PdfString("a string")
};
// Output: <</Type /Example /Version 0.01 /IntegerItem 12 /StringItem (a string)>>

// Nested dictionaries
dict[new PdfName("Subdictionary")] = new PdfDictionary
{
    [new PdfName("Item1")] = new PdfReal(0.4),
    [new PdfName("Item2")] = PdfBoolean.True
};
```

### 8. Stream Object (`PdfStream`)
```csharp
byte[] data = Encoding.UTF8.GetBytes("Stream content here");
PdfStream stream = new PdfStream(data);

// Add stream dictionary entries
stream.Dictionary[PdfName.Filter] = new PdfName("FlateDecode");

// Access stream data
byte[] streamData = stream.Data;
int length = stream.Length;
```

### Indirect References (`PdfIndirectReference`)
```csharp
// Create reference: 12 0 R
PdfIndirectReference reference = new PdfIndirectReference(
    objectNumber: 12,
    generationNumber: 0
);
// Output: 12 0 R

// Create indirect object definition
string definition = PdfIndirectReference.ToIndirectObjectDefinition(
    objectNumber: 12,
    generationNumber: 0,
    content: new PdfString("Brillig")
);
// Output: 12 0 obj
//         (Brillig)
//         endobj
```

## PDF Versions

The library supports version detection and feature checking:

```csharp
// Common versions
PdfVersion pdf17 = PdfVersion.Pdf17; // ISO 32000-1:2008
PdfVersion pdf20 = PdfVersion.Pdf20; // ISO 32000-2:2020

// Parse from header
PdfVersion version = PdfVersion.Parse("%PDF-1.7");
PdfVersion version2 = PdfVersion.Parse("2.0");

// Version comparison
if (version >= PdfVersion.Pdf17)
{
    Console.WriteLine("Supports PDF 1.7 features");
}

// Check feature support
if (pdf20.Supports(PdfVersion.Pdf17))
{
    Console.WriteLine("PDF 2.0 supports all PDF 1.7 features");
}

// Get header string
string header = pdf17.ToHeaderString(); // %PDF-1.7
```

## Architecture

The library follows the PDF specification structure:

```
PdfLibrary/
├── Core/
│   ├── PdfObject.cs              # Base class for all PDF objects
│   ├── PdfVersion.cs             # Version detection & handling
│   └── Primitives/
│       ├── PdfNull.cs            # Null object
│       ├── PdfBoolean.cs         # Boolean objects
│       ├── PdfInteger.cs         # Integer numbers
│       ├── PdfReal.cs            # Real numbers
│       ├── PdfString.cs          # String objects (literal & hex)
│       ├── PdfName.cs            # Name objects
│       ├── PdfArray.cs           # Array objects
│       ├── PdfDictionary.cs      # Dictionary objects
│       ├── PdfStream.cs          # Stream objects
│       └── PdfIndirectReference.cs # Indirect object references
```

## Design Principles

1. **Spec Compliance**: All implementations strictly follow ISO 32000-1:2008 (PDF 1.7) and ISO 32000-2:2020 (PDF 2.0)
2. **Type Safety**: Strong typing with compile-time checks
3. **Immutability**: Where appropriate (e.g., PdfNull, PdfBoolean)
4. **Convenience**: Implicit conversions and common names as static properties
5. **Extensibility**: Easy to add filters, encoders, and custom functionality

## Reference Documentation

The library was developed using the official PDF ISO standards:
- PDF 1.7: ISO 32000-1:2008
- PDF 2.0: ISO 32000-2:2020 (with Errata Collection 2)
- PDF 2.0 Application Notes (AN001-AN003)
- ISO Technical Specifications (TS 32001-32005)

All implementations include section references to the specifications.

## Next Steps

- Implement lexer for tokenizing PDF byte streams
- Build parser for reading PDF files
- Implement cross-reference table (xref) parsing
- Add filter/decoder support (FlateDecode, ASCIIHexDecode, etc.)
- Implement PDF file structure (header, body, xref, trailer)
- Add document-level operations (pages, fonts, images)

## License

TBD
