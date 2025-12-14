using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

// ==================== CONFIGURATION ====================
const string outputPath = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\watermarked.pdf";

Console.WriteLine("Watermark Example\n");
Console.WriteLine($"Creating document with watermark: {outputPath}\n");

// ==================== CREATE PDF WITH WATERMARK ====================
PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle("Confidential Document")
        .SetAuthor("PdfLibrary Examples")
        .SetSubject("Document Security Example"))

    .AddPage(p =>
    {
        p.FromTopLeft();

        // ==================== DIAGONAL WATERMARK (DRAWN FIRST - APPEARS BEHIND) ====================
        // Large semi-transparent "CONFIDENTIAL" across the page
        // Rotated 45 degrees and centered horizontally
        // For rotated text, x-position is shifted left by approximately (textWidth * cos(45°) / 2)
        // to center the rotated text bounding box on the page

        p.AddText("CONFIDENTIAL", 135, 450) // Adjusted for centering rotated text
            .Font("Helvetica-Bold", 80)
            .WithColor(PdfColor.FromRgb(220, 220, 220)) // Light gray
            .Rotate(45); // 45 degree diagonal

        // Second watermark at bottom
        p.AddText("DO NOT DISTRIBUTE", 125, 600) // Adjusted for centering rotated text
            .Font("Helvetica-Bold", 60)
            .WithColor(PdfColor.FromRgb(240, 200, 200)) // Very light red
            .Rotate(45);

        // ==================== MAIN CONTENT (DRAWN SECOND - APPEARS ON TOP) ====================
        p.AddText("CONFIDENTIAL DOCUMENT", 72, 50)
            .Font("Helvetica-Bold", 24)
            .WithColor(PdfColor.Black);

        p.AddText("Internal Use Only", 72, 85)
            .Font("Helvetica", 14)
            .WithColor(PdfColor.DarkGray);

        // Horizontal line
        p.AddRectangle(72, 105, 468, 1, PdfColor.LightGray);

        // Document body text
        p.AddText("Subject: Quarterly Financial Report Q4 2024", 72, 130)
            .Font("Helvetica-Bold", 12);

        p.AddText("Date: January 13, 2025", 72, 150)
            .Font("Helvetica", 10);

        p.AddText("Classification: Confidential - Do Not Distribute", 72, 165)
            .Font("Helvetica", 10)
            .WithColor(PdfColor.Red);

        // Separator
        p.AddRectangle(72, 185, 468, 1, PdfColor.LightGray);

        // Paragraph 1
        p.AddText("Executive Summary", 72, 210)
            .Font("Helvetica-Bold", 11);

        var para1Lines = new[]
        {
            "This confidential report contains sensitive financial information regarding our Q4 2024",
            "performance. The data herein is proprietary and should not be shared outside the",
            "executive leadership team without explicit written authorization.",
            "",
            "Key highlights for Q4 2024:",
            "• Revenue growth of 23% year-over-year",
            "• Operating margin improved to 18.5%",
            "• Successful product launch in three new markets",
            "• Customer retention rate at 94%"
        };

        double lineY = 230;
        foreach (var line in para1Lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                lineY += 12; // Extra spacing for empty lines
            }
            else
            {
                p.AddText(line, 72, lineY)
                    .Font("Helvetica", 10);
                lineY += 14;
            }
        }

        // Paragraph 2
        lineY += 10;
        p.AddText("Strategic Initiatives", 72, lineY)
            .Font("Helvetica-Bold", 11);

        lineY += 20;
        var para2Lines = new[]
        {
            "Our strategic focus for Q1 2025 includes:",
            "1. Expansion into the European market with localized product offerings",
            "2. R&D investment in AI-powered features for our flagship product",
            "3. Partnership negotiations with three potential strategic partners",
            "4. Implementation of enhanced security protocols across all operations"
        };

        foreach (var line in para2Lines)
        {
            p.AddText(line, 72, lineY)
                .Font("Helvetica", 10);
            lineY += 14;
        }

        // ==================== FOOTER ====================
        p.AddRectangle(72, 720, 468, 1, PdfColor.LightGray);

        p.AddText("Page 1 of 1 | Confidential | For Internal Use Only", 72, 735)
            .Font("Helvetica", 8)
            .WithColor(PdfColor.DarkGray);
    })

    .Save(outputPath);

Console.WriteLine($"  ✓ Document with watermark created successfully!");
Console.WriteLine($"\nFile: {outputPath}");
Console.WriteLine("\nWatermarks applied:");
Console.WriteLine("  • Diagonal 'CONFIDENTIAL' in light gray");
Console.WriteLine("  • Diagonal 'DO NOT DISTRIBUTE' in light red");
Console.WriteLine("\nDone!");
