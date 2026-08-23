using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Core;

public class PdfStreamSetEncodedDataTests
{
    // /DecodeParms describes the filter chain being REPLACED. SetEncodedData collapses /Filter to a
    // single name, so any surviving parms entry is positionally meaningless at best and actively
    // misread at worst -- an LZW /EarlyChange would be reinterpreted as a Flate predictor parameter.
    [Fact]
    public void SetEncodedData_removes_stale_DecodeParms()
    {
        byte[] payload = "hello stream filters"u8.ToArray();
        var stream = new PdfStream(new PdfDictionary(), payload);
        stream.Dictionary[PdfName.Filter] = new PdfName("LZWDecode");
        var parms = new PdfDictionary();
        parms[new PdfName("EarlyChange")] = new PdfInteger(0);
        stream.Dictionary[PdfName.DecodeParms] = parms;

        stream.SetEncodedData(payload, "FlateDecode");

        Assert.Null(stream.Dictionary.Get("DecodeParms"));
        Assert.Equal("FlateDecode", Assert.IsType<PdfName>(stream.Dictionary.Get("Filter")).Value);
        Assert.Equal(payload, stream.GetDecodedData());
    }

    [Fact]
    public void SetEncodedData_on_a_stream_with_no_DecodeParms_is_unaffected()
    {
        byte[] payload = "no parms here"u8.ToArray();
        var stream = new PdfStream(new PdfDictionary(), payload);

        stream.SetEncodedData(payload, "FlateDecode");

        Assert.Null(stream.Dictionary.Get("DecodeParms"));
        Assert.Equal(payload, stream.GetDecodedData());
    }
}
