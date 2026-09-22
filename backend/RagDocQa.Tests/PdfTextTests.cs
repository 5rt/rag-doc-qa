using RagDocQa.Api.Controllers;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace RagDocQa.Tests;

public class PdfTextTests
{
    [Fact]
    public void Lines_come_out_top_to_bottom_and_words_left_to_right()
    {
        // Written deliberately out of reading order: PDF content order is not
        // reading order, and y grows upwards from the bottom of the page.
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("line", 12, new PdfPoint(150, 600), font);
        page.AddText("second", 12, new PdfPoint(50, 600), font);
        page.AddText("world", 12, new PdfPoint(150, 700), font);
        page.AddText("hello", 12, new PdfPoint(50, 700), font);

        using var pdf = PdfDocument.Open(builder.Build());

        Assert.Equal("hello world\nsecond line",
            DocumentsController.ExtractPageText(pdf.GetPage(1)));
    }
}
