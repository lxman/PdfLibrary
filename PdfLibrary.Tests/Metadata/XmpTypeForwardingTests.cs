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

    /// <summary>NOT a forwarding check: this test project has a direct <c>ProjectReference</c> to
    /// <c>Xmp.csproj</c>, so <c>typeof(XmpPacket)</c> binds at COMPILE time to <c>PdfLibrary.Xmp</c>
    /// regardless of whether <c>PdfLibrary</c>'s forwarders exist or work — it cannot fail, and does
    /// not exercise <c>GetForwardedTypes()</c> or the runtime redirect the way the
    /// <c>[Theory]</c> above does. It only pins that this assembly is what a from-source consumer
    /// resolves the type from, as a compile-binding sanity check.</summary>
    [Fact]
    public void The_types_resolve_from_the_Xmp_assembly_when_referenced_directly()
    {
        Assert.Equal("PdfLibrary.Xmp", typeof(XmpPacket).Assembly.GetName().Name);
    }
}
