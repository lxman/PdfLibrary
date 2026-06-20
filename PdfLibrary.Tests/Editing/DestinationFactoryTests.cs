using PdfLibrary.Builder.Bookmark;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class DestinationFactoryTests
{
    [Fact]
    public void ToPage_SetsPageIndex_AndXyzType_WithNullCoords()
    {
        PdfDestination d = PdfDestination.ToPage(3);
        Assert.Equal(3, d.PageIndex);
        Assert.Equal(PdfDestinationType.XYZ, d.Type);
        Assert.Null(d.Left);
        Assert.Null(d.Top);
        Assert.Null(d.Zoom);
    }

    [Fact]
    public void FitPage_SetsPageIndex_AndFitType()
    {
        PdfDestination d = PdfDestination.FitPage(2);
        Assert.Equal(2, d.PageIndex);
        Assert.Equal(PdfDestinationType.Fit, d.Type);
    }

    [Fact]
    public void FitWidth_SetsPageIndex_FitHType_AndTop()
    {
        PdfDestination d = PdfDestination.FitWidth(1, 700.5);
        Assert.Equal(1, d.PageIndex);
        Assert.Equal(PdfDestinationType.FitH, d.Type);
        Assert.Equal(700.5, d.Top);
    }

    [Fact]
    public void FitWidth_NullTop_SetsNullTop()
    {
        PdfDestination d = PdfDestination.FitWidth(0, null);
        Assert.Equal(PdfDestinationType.FitH, d.Type);
        Assert.Null(d.Top);
    }

    [Fact]
    public void FitHeight_SetsPageIndex_FitVType_AndLeft()
    {
        PdfDestination d = PdfDestination.FitHeight(5, 100.0);
        Assert.Equal(5, d.PageIndex);
        Assert.Equal(PdfDestinationType.FitV, d.Type);
        Assert.Equal(100.0, d.Left);
    }

    [Fact]
    public void At_SetsXyzTypeAndAllCoords()
    {
        PdfDestination d = PdfDestination.At(0, 10.0, 20.0, 1.5);
        Assert.Equal(0, d.PageIndex);
        Assert.Equal(PdfDestinationType.XYZ, d.Type);
        Assert.Equal(10.0, d.Left);
        Assert.Equal(20.0, d.Top);
        Assert.Equal(1.5, d.Zoom);
    }

    [Fact]
    public void At_NullCoords_AllNull()
    {
        PdfDestination d = PdfDestination.At(1, null, null, null);
        Assert.Equal(PdfDestinationType.XYZ, d.Type);
        Assert.Null(d.Left);
        Assert.Null(d.Top);
        Assert.Null(d.Zoom);
    }

    [Fact]
    public void FitRect_SetsFitRTypeAndAllCoords()
    {
        PdfDestination d = PdfDestination.FitRect(2, 10.0, 20.0, 300.0, 400.0);
        Assert.Equal(2, d.PageIndex);
        Assert.Equal(PdfDestinationType.FitR, d.Type);
        Assert.Equal(10.0, d.Left);
        Assert.Equal(20.0, d.Bottom);
        Assert.Equal(300.0, d.Right);
        Assert.Equal(400.0, d.Top);
    }
}
