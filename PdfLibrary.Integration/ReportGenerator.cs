using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PdfLibrary.Integration;

/// <summary>
/// Generates HTML reports for test results
/// </summary>
public static class ReportGenerator
{
    public static void GenerateHtmlReport(List<TestResult> results, string outputPath, string imageDir)
    {
        var html = new StringBuilder();

        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html><head>");
        html.AppendLine("<title>PDF Rendering Test Report</title>");
        html.AppendLine("<style>");
        html.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
        html.AppendLine("h1 { color: #333; }");
        html.AppendLine(".summary { background: white; padding: 15px; border-radius: 5px; margin-bottom: 20px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }");
        html.AppendLine(".pass { color: #28a745; }");
        html.AppendLine(".fail { color: #dc3545; }");
        html.AppendLine(".test-card { background: white; padding: 15px; border-radius: 5px; margin-bottom: 15px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }");
        html.AppendLine(".test-card h3 { margin-top: 0; }");
        html.AppendLine(".test-card .description { color: #666; font-size: 14px; margin-bottom: 10px; }");
        html.AppendLine(".images { display: flex; gap: 15px; flex-wrap: wrap; }");
        html.AppendLine(".image-box { text-align: center; }");
        html.AppendLine(".image-box img { max-width: 350px; border: 1px solid #ccc; border-radius: 3px; }");
        html.AppendLine(".image-box p { margin: 5px 0; font-size: 12px; color: #666; }");
        html.AppendLine(".percentage { font-size: 14px; color: #666; }");
        html.AppendLine("</style>");
        html.AppendLine("</head><body>");

        html.AppendLine("<h1>PDF Rendering Test Report</h1>");

        // Summary
        int passed = results.Count(r => r.Passed);
        int failed = results.Count(r => !r.Passed);
        string overallClass = failed == 0 ? "pass" : "fail";

        html.AppendLine("<div class='summary'>");
        html.AppendLine($"<h2 class='{overallClass}'>{(failed == 0 ? "All Tests Passed" : $"{failed} Test(s) Failed")}</h2>");
        html.AppendLine($"<p><strong>Total:</strong> {results.Count} | <span class='pass'>Passed: {passed}</span> | <span class='fail'>Failed: {failed}</span></p>");
        html.AppendLine($"<p><strong>Generated:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        html.AppendLine("</div>");

        // Individual results
        foreach (var result in results)
        {
            string statusClass = result.Passed ? "pass" : "fail";
            string statusText = result.Passed ? "PASS" : "FAIL";

            html.AppendLine("<div class='test-card'>");
            html.AppendLine($"<h3>{result.Name} - <span class='{statusClass}'>{statusText}</span></h3>");
            html.AppendLine($"<p class='description'>{result.Description}</p>");

            if (result.ErrorMessage != null)
            {
                html.AppendLine($"<p class='fail'><em>{result.ErrorMessage}</em></p>");
            }
            else
            {
                html.AppendLine($"<p class='percentage'>Match: {result.MatchPercentage:F2}%</p>");
            }

            html.AppendLine("<div class='images'>");

            // Golden image
            string goldenImg = $"{result.Name}_golden.png";
            if (File.Exists(Path.Combine(imageDir, goldenImg)))
            {
                html.AppendLine("<div class='image-box'>");
                html.AppendLine($"<img src='{goldenImg}' alt='Golden' />");
                html.AppendLine("<p>Golden (Expected)</p>");
                html.AppendLine("</div>");
            }

            // Actual image
            string actualImg = $"{result.Name}_actual.png";
            if (File.Exists(Path.Combine(imageDir, actualImg)))
            {
                html.AppendLine("<div class='image-box'>");
                html.AppendLine($"<img src='{actualImg}' alt='Actual' />");
                html.AppendLine("<p>Actual (Current)</p>");
                html.AppendLine("</div>");
            }

            // Diff image (only show for failures)
            if (!result.Passed)
            {
                string diffImg = $"{result.Name}_diff.png";
                if (File.Exists(Path.Combine(imageDir, diffImg)))
                {
                    html.AppendLine("<div class='image-box'>");
                    html.AppendLine($"<img src='{diffImg}' alt='Difference' />");
                    html.AppendLine("<p>Difference</p>");
                    html.AppendLine("</div>");
                }
            }

            html.AppendLine("</div>"); // images
            html.AppendLine("</div>"); // test-card
        }

        html.AppendLine("</body></html>");

        File.WriteAllText(outputPath, html.ToString());
    }
}
