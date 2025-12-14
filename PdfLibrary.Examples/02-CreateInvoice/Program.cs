using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

// ==================== CONFIGURATION ====================
const string outputPath = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\invoice.pdf";

Console.WriteLine("Invoice Generator Example\n");
Console.WriteLine($"Creating invoice: {outputPath}\n");

// Invoice data
var invoiceNumber = "INV-2025-00123";
var invoiceDate = "January 13, 2025";
var dueDate = "February 13, 2025";

var billTo = new[]
{
    "Acme Corporation",
    "123 Business Ave",
    "Suite 100",
    "New York, NY 10001"
};

var items = new[]
{
    new { Description = "Professional Services - Software Development", Quantity = 40, Rate = 150.00m },
    new { Description = "Cloud Infrastructure Management", Quantity = 1, Rate = 500.00m },
    new { Description = "Technical Consulting", Quantity = 8, Rate = 175.00m },
    new { Description = "Code Review and QA", Quantity = 12, Rate = 125.00m }
};

// Calculate totals
decimal subtotal = items.Sum(i => i.Quantity * i.Rate);
decimal taxRate = 0.08m; // 8% sales tax
decimal tax = subtotal * taxRate;
decimal total = subtotal + tax;

// ==================== CREATE PDF ====================
PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .SetTitle($"Invoice {invoiceNumber}")
        .SetAuthor("PdfLibrary Examples")
        .SetSubject("Invoice")
        .SetKeywords("invoice, billing, payment"))

    .AddPage(p =>
    {
        p.FromTopLeft(); // Use top-left origin

        // ==================== HEADER ====================
        // Company logo (top right)
        var logoPath = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\company-logo.jpg";
        if (File.Exists(logoPath))
        {
            p.AddImageFromFile(logoPath, 390, 50, 150, 45);
        }

        p.AddText("INVOICE", 72, 50)
            .Font("Helvetica-Bold", 32)
            .WithColor(PdfColor.FromHex("#2C3E50"));

        // Company name
        p.AddText("PdfLibrary Services Inc.", 72, 90)
            .Font("Helvetica-Bold", 14)
            .WithColor(PdfColor.Black);

        p.AddText("456 Technology Drive, San Francisco, CA 94105", 72, 108)
            .Font("Helvetica", 10)
            .WithColor(PdfColor.DarkGray);

        p.AddText("Phone: (555) 123-4567 | Email: billing@pdflibrary.com", 72, 122)
            .Font("Helvetica", 10)
            .WithColor(PdfColor.DarkGray);

        // Horizontal line separator
        p.AddRectangle(72, 140, 468, 1, PdfColor.LightGray);

        // ==================== INVOICE INFO (Right aligned) ====================
        p.AddText($"Invoice #: {invoiceNumber}", 380, 160)
            .Font("Helvetica-Bold", 10);

        p.AddText($"Date: {invoiceDate}", 380, 175)
            .Font("Helvetica", 10);

        p.AddText($"Due Date: {dueDate}", 380, 190)
            .Font("Helvetica", 10)
            .WithColor(PdfColor.Red);

        // ==================== BILL TO ====================
        p.AddText("BILL TO:", 72, 160)
            .Font("Helvetica-Bold", 11)
            .WithColor(PdfColor.FromHex("#2C3E50"));

        double billToY = 178;
        foreach (var line in billTo)
        {
            p.AddText(line, 72, billToY)
                .Font("Helvetica", 10);
            billToY += 14;
        }

        // ==================== LINE ITEMS TABLE ====================
        double tableTop = 250;
        double tableLeft = 72;
        double tableWidth = 468;

        // Table header background
        p.AddRectangle(tableLeft, tableTop, tableWidth, 25, PdfColor.FromHex("#34495E"));

        // Table headers (vertically centered in the 25px header)
        p.AddText("Description", tableLeft + 10, tableTop + 12)
            .Font("Helvetica-Bold", 10)
            .WithColor(PdfColor.White);

        p.AddText("Qty", tableLeft + 320, tableTop + 12)
            .Font("Helvetica-Bold", 10)
            .WithColor(PdfColor.White);

        p.AddText("Rate", tableLeft + 370, tableTop + 12)
            .Font("Helvetica-Bold", 10)
            .WithColor(PdfColor.White);

        p.AddText("Amount", tableLeft + 420, tableTop + 12)
            .Font("Helvetica-Bold", 10)
            .WithColor(PdfColor.White);

        // Table rows (add spacing after header)
        double rowY = tableTop + 25 + 5; // 5px gap after header
        bool alternateRow = false;

        foreach (var item in items)
        {
            // Alternate row background
            if (alternateRow)
            {
                p.AddRectangle(tableLeft, rowY, tableWidth, 20, PdfColor.FromHex("#F8F9FA"));
            }

            decimal amount = item.Quantity * item.Rate;

            p.AddText(item.Description, tableLeft + 10, rowY + 6)
                .Font("Helvetica", 9);

            p.AddText(item.Quantity.ToString(), tableLeft + 330, rowY + 6)
                .Font("Helvetica", 9);

            p.AddText($"${item.Rate:F2}", tableLeft + 370, rowY + 6)
                .Font("Helvetica", 9);

            p.AddText($"${amount:F2}", tableLeft + 420, rowY + 6)
                .Font("Helvetica", 9);

            rowY += 20;
            alternateRow = !alternateRow;
        }

        // Table bottom border
        p.AddRectangle(tableLeft, rowY, tableWidth, 1, PdfColor.LightGray);

        // ==================== TOTALS ====================
        double totalsY = rowY + 20;
        double totalsLabelX = tableLeft + 350;
        double totalsValueX = tableLeft + 420;

        p.AddText("Subtotal:", totalsLabelX, totalsY)
            .Font("Helvetica", 10);

        p.AddText($"${subtotal:F2}", totalsValueX, totalsY)
            .Font("Helvetica", 10);

        totalsY += 18;
        p.AddText($"Tax ({taxRate * 100:F0}%):", totalsLabelX, totalsY)
            .Font("Helvetica", 10);

        p.AddText($"${tax:F2}", totalsValueX, totalsY)
            .Font("Helvetica", 10);

        // Total separator line
        totalsY += 5;
        p.AddRectangle(totalsLabelX, totalsY, 115, 1, PdfColor.Black);

        totalsY += 15;  // Increased spacing before total
        p.AddText("TOTAL DUE:", totalsLabelX, totalsY)
            .Font("Helvetica-Bold", 12)
            .WithColor(PdfColor.FromHex("#E74C3C"));

        p.AddText($"${total:F2}", totalsValueX, totalsY)
            .Font("Helvetica-Bold", 12)
            .WithColor(PdfColor.FromHex("#E74C3C"));

        // ==================== PAYMENT TERMS ====================
        double footerY = 680;

        p.AddText("Payment Terms:", 72, footerY)
            .Font("Helvetica-Bold", 10);

        p.AddText("Payment is due within 30 days. Please make checks payable to PdfLibrary Services Inc.", 72, footerY + 15)
            .Font("Helvetica", 9)
            .WithColor(PdfColor.DarkGray);

        p.AddText("Wire transfer details available upon request.", 72, footerY + 28)
            .Font("Helvetica", 9)
            .WithColor(PdfColor.DarkGray);

        // ==================== FOOTER ====================
        p.AddRectangle(72, 740, 468, 1, PdfColor.LightGray);

        p.AddText("Thank you for your business!", 72, 750)
            .Font("Helvetica-Oblique", 9)
            .WithColor(PdfColor.DarkGray);
    })

    .Save(outputPath);

Console.WriteLine($"  ✓ Invoice created successfully!");
Console.WriteLine($"\nFile: {outputPath}");
Console.WriteLine($"\nInvoice Summary:");
Console.WriteLine($"  Invoice #: {invoiceNumber}");
Console.WriteLine($"  Date: {invoiceDate}");
Console.WriteLine($"  Subtotal: ${subtotal:F2}");
Console.WriteLine($"  Tax: ${tax:F2}");
Console.WriteLine($"  Total: ${total:F2}");
Console.WriteLine("\nDone!");
