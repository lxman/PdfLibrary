using PdfLibrary.Builder;
using PdfLibrary.Builder.Layer;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Tests.Builder;

public class PdfLayerTests
{
    [Fact]
    public void DefineLayer_CreatesLayerWithName()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test Layer", out PdfLayer layer);

        // Assert
        Assert.NotNull(layer);
        Assert.Equal("Test Layer", layer.Name);
        Assert.True(layer.IsVisibleByDefault);
        Assert.False(layer.IsLocked);
    }

    [Fact]
    public void DefineLayer_WithConfiguration_AppliesSettings()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Hidden Layer", config => config.Hidden().Locked().NeverPrint(), out PdfLayer layer);

        // Assert
        Assert.False(layer.IsVisibleByDefault);
        Assert.True(layer.IsLocked);
        Assert.Equal(false, layer.PrintState);
    }

    [Fact]
    public void DefineLayer_MultipleLayers_CanBeCreated()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Layer 1", out PdfLayer layer1)
            .DefineLayer("Layer 2", out PdfLayer layer2)
            .DefineLayer("Layer 3", out PdfLayer layer3);

        // Assert - each layer has a different name
        Assert.Equal("Layer 1", layer1.Name);
        Assert.Equal("Layer 2", layer2.Name);
        Assert.Equal("Layer 3", layer3.Name);
    }

    [Fact]
    public void PdfLayerBuilder_Hidden_SetsVisibleByDefaultToFalse()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.Hidden(), out PdfLayer layer);

        // Assert
        Assert.False(layer.IsVisibleByDefault);
    }

    [Fact]
    public void PdfLayerBuilder_Visible_SetsVisibleByDefaultToTrue()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.Hidden().Visible(), out PdfLayer layer);

        // Assert
        Assert.True(layer.IsVisibleByDefault);
    }

    [Fact]
    public void PdfLayerBuilder_Locked_SetsIsLockedToTrue()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.Locked(), out PdfLayer layer);

        // Assert
        Assert.True(layer.IsLocked);
    }

    [Fact]
    public void PdfLayerBuilder_WithIntent_SetsIntent()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.WithIntent(PdfLayerIntent.Design), out PdfLayer layer);

        // Assert
        Assert.Equal(PdfLayerIntent.Design, layer.Intent);
    }

    [Fact]
    public void PdfLayerBuilder_NeverPrint_SetsPrintStateToFalse()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.NeverPrint(), out PdfLayer layer);

        // Assert
        Assert.Equal(false, layer.PrintState);
    }

    [Fact]
    public void PdfLayerBuilder_AlwaysPrint_SetsPrintStateToTrue()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.AlwaysPrint(), out PdfLayer layer);

        // Assert
        Assert.Equal(true, layer.PrintState);
    }

    [Fact]
    public void PdfLayerBuilder_PrintWhenVisible_SetsPrintStateToNull()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.AlwaysPrint().PrintWhenVisible(), out PdfLayer layer);

        // Assert
        Assert.Null(layer.PrintState);
    }

    [Fact]
    public void PdfLayerBuilder_NeverExport_SetsExportStateToFalse()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.NeverExport(), out PdfLayer layer);

        // Assert
        Assert.Equal(false, layer.ExportState);
    }

    [Fact]
    public void PdfLayerBuilder_AlwaysExport_SetsExportStateToTrue()
    {
        // Act
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test", config => config.AlwaysExport(), out PdfLayer layer);

        // Assert
        Assert.Equal(true, layer.ExportState);
    }

    [Fact]
    public void DefineLayer_CanChainMultipleLayers()
    {
        // Act - verify fluent chaining works
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Layer 1", out PdfLayer layer1)
            .DefineLayer("Layer 2", out PdfLayer layer2)
            .DefineLayer("Layer 3", out PdfLayer layer3)
            .AddPage(page => page
                .Layer(layer1, c => c.AddText("Test", 100, 700)));

        // Assert
        byte[] pdf = builder.ToByteArray();
        Assert.True(pdf.Length > 0);
    }

    [Fact]
    public void Save_WithLayers_ProducesValidPdf()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Background", out PdfLayer background)
            .DefineLayer("Foreground", out PdfLayer foreground)
            .AddPage(page => page
                .Layer(background, content => content
                    .AddRectangle(0, 0, 612, 792, PdfColor.LightGray))
                .Layer(foreground, content => content
                    .AddText("Hello, World!", 100, 700)));

        // Act
        byte[] pdfData = builder.ToByteArray();

        // Assert
        Assert.NotNull(pdfData);
        Assert.True(pdfData.Length > 0);

        // Verify PDF header
        string header = System.Text.Encoding.ASCII.GetString(pdfData, 0, 8);
        Assert.StartsWith("%PDF-", header);
    }

    [Fact]
    public void Save_WithLayers_ContainsOCProperties()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test Layer", out PdfLayer layer)
            .AddPage(page => page
                .Layer(layer, content => content
                    .AddText("Layered text", 100, 700)));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/OCProperties", pdfContent);
        Assert.Contains("/OCGs", pdfContent);
        Assert.Contains("Test Layer", pdfContent);
    }

    [Fact]
    public void Save_WithLayers_ContainsBDCandEMCOperators()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Test Layer", out PdfLayer layer)
            .AddPage(page => page
                .Layer(layer, content => content
                    .AddText("Layered text", 100, 700)));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("BDC", pdfContent);
        Assert.Contains("EMC", pdfContent);
    }

    [Fact]
    public void Save_WithHiddenLayer_ContainsOFFArray()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Hidden Layer", config => config.Hidden(), out PdfLayer layer)
            .AddPage(page => page
                .Layer(layer, content => content
                    .AddText("Hidden text", 100, 700)));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/OFF", pdfContent);
    }

    [Fact]
    public void Save_WithLockedLayer_ContainsLockedArray()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Locked Layer", config => config.Locked(), out PdfLayer layer)
            .AddPage(page => page
                .Layer(layer, content => content
                    .AddText("Locked text", 100, 700)));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("/Locked", pdfContent);
    }

    [Fact]
    public void Save_WithMultipleLayers_AllLayersInOCGs()
    {
        // Arrange
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Layer A", out PdfLayer layerA)
            .DefineLayer("Layer B", out PdfLayer layerB)
            .AddPage(page => page
                .Layer(layerA, content => content
                    .AddText("Layer A text", 100, 700))
                .Layer(layerB, content => content
                    .AddText("Layer B text", 100, 650)));

        // Act
        byte[] pdfData = builder.ToByteArray();
        string pdfContent = System.Text.Encoding.ASCII.GetString(pdfData);

        // Assert
        Assert.Contains("Layer A", pdfContent);
        Assert.Contains("Layer B", pdfContent);
    }

    [Fact]
    public void PageContent_WithoutLayers_StillWorks()
    {
        // Arrange & Act - mixing layered and non-layered content
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .DefineLayer("Layer", out PdfLayer layer)
            .AddPage(page =>
            {
                page.AddText("Non-layered text", 100, 750);
                page.Layer(layer, content => content
                    .AddText("Layered text", 100, 700));
                page.AddText("More non-layered text", 100, 650);
            });

        byte[] pdfData = builder.ToByteArray();

        // Assert
        Assert.NotNull(pdfData);
        Assert.True(pdfData.Length > 0);
    }
}
