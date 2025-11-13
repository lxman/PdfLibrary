using PdfLibrary.Core;
using PdfLibrary.Structure;

PdfDocument doc = PdfDocument.Load(@"PDFs\2_0\Simple PDF 2.0 file.pdf");
IReadOnlyDictionary<int, PdfObject> objects = doc.Objects;

Console.WriteLine(objects.Count);