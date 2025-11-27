namespace PdfLibrary.Integration;

/// <summary>
/// Interface for test document generators
/// </summary>
public interface ITestDocument
{
    /// <summary>
    /// Name of the test document (used for filename)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this test document covers
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Generate the test PDF document
    /// </summary>
    /// <param name="outputPath">Full path where the PDF should be saved</param>
    void Generate(string outputPath);
}
