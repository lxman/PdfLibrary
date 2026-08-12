using System.Linq;
using System.Reflection;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>Lxman.PdfLibrary 2.5.2 shipped XmpPacket and friends as public types IN PdfLibrary.dll.
/// Moving them to the Xmp assembly is binary-breaking without forwarders: anything compiled against
/// 2.5.2 gets a TypeLoadException. Nothing else in this repo can catch that, because everything here
/// recompiles from source — hence this test.</summary>
public class XmpTypeForwardingTests
{
    [Theory]
    [InlineData("PdfLibrary.Metadata.XmpPacket")]
    [InlineData("PdfLibrary.Metadata.XmpProperty")]
    [InlineData("PdfLibrary.Metadata.XmpSchemas")]
    [InlineData("PdfLibrary.Metadata.XmpValueKind")]
    public void PdfLibrary_still_forwards_the_moved_public_xmp_types(string fullName)
    {
        Assembly pdfLibrary = typeof(PdfLibrary.Structure.PdfDocument).Assembly;

        string[] forwarded = pdfLibrary.GetForwardedTypes().Select(t => t.FullName!).ToArray();

        Assert.Contains(fullName, forwarded);
    }

    [Fact]
    public void The_forwarded_types_resolve_out_of_the_Xmp_assembly()
    {
        Assert.Equal("PdfLibrary.Xmp", typeof(XmpPacket).Assembly.GetName().Name);
    }
}
