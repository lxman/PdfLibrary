using System.Linq;
using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Core;

public class PdfStringNoImplicitTests
{
    [Fact]
    public void PdfString_HasNoPublicStringConstructor()
    {
        bool hasStringCtor = typeof(PdfString)
            .GetConstructors()
            .Any(c => c.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        Assert.False(hasStringCtor, "Public PdfString(string) ctor must be removed (use FromText/FromByteLiteral).");
    }

    [Fact]
    public void PdfString_HasNoImplicitStringConversion()
    {
        bool hasImplicit = typeof(PdfString)
            .GetMethods()
            .Any(m => m.Name == "op_Implicit" && m.ReturnType == typeof(PdfString)
                      && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        Assert.False(hasImplicit, "Implicit string→PdfString operator must be removed.");
    }
}
