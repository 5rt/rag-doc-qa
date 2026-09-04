using Azure;
using Azure.Search.Documents;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RagDocQa.Api.Ai;
using RagDocQa.Api.Ingest;
using RagDocQa.Api.Search;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace RagDocQa.Api.Controllers;

public record DocumentSummary(Guid Id, string FileName, int ChunkCount, DateTime UploadedUtc);

[ApiController]
[Route("api/documents")]
public class DocumentsController(GeminiClient gemini, IConfiguration config) : ControllerBase
{
    /// <summary>
    /// Rebuilds readable text from a PDF page. Page.Text concatenates the raw
    /// glyph runs with no separators, which fuses words together and drops line
    /// breaks entirely ("ResultAssessment completed"). Grouping words by their
    /// vertical position recovers the original lines.
    /// </summary>
    private static string ExtractPageText(Page page)
    {
        var lines = page.GetWords()
            // PDF coordinates start bottom-left, so descending Y is top-to-bottom.
            // Rounding absorbs the sub-point baseline drift within a single line.
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 0))
            .OrderByDescending(g => g.Key)
            .Select(g => string.Join(" ", g
                .OrderBy(w => w.BoundingBox.Left)
                .Select(w => w.Text)));

        return string.Join("\n", lines);
    }

    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file was uploaded." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".txt"))
            return BadRequest(new { error = "Only PDF and .txt files are supported." });

        // 1. Pull the raw text out of the file
        string text;
        await using (var stream = file.OpenReadStream())
        {
            if (ext == ".pdf")
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                buffer.Position = 0;
                using var pdf = PdfDocument.Open(buffer);
                text = string.Join("\n\n", pdf.GetPages().Select(ExtractPageText));
            }
            else
            {
                text = await new StreamReader(stream).ReadToEndAsync();
            }
        }

        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(new { error = "No readable text found. Scanned PDFs need OCR." });

        // 2. Split into passages
        var chunks = Chunker.Split(text);
        var documentId = Guid.NewGuid();

        // 3. Embed each passage and push it to the vector index
        var searchClient = new SearchClient(
            new Uri(config["Search:Endpoint"]!),
            SearchSetup.IndexName,
            new AzureKeyCredential(config["Search:ApiKey"]!));

        var batch = new List<object>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var vector = await gemini.EmbedAsync(chunks[i], "RETRIEVAL_DOCUMENT");
            batch.Add(new
            {
                id = $"{documentId}-{i}",
                documentId = documentId.ToString(),
                fileName = file.FileName,
                chunkIndex = i,
                content = chunks[i],
                contentVector = vector
            });
        }
        await searchClient.UploadDocumentsAsync(batch);

        // 4. Record the file in SQL. Parameters, never string concatenation.
        await using var sql = new SqlConnection(config.GetConnectionString("Default"));
        await sql.ExecuteAsync(
            "INSERT INTO Documents (Id, FileName, ChunkCount) VALUES (@Id, @FileName, @ChunkCount)",
            new { Id = documentId, FileName = file.FileName, ChunkCount = chunks.Count });

        return Ok(new { documentId, fileName = file.FileName, chunkCount = chunks.Count });
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        await using var sql = new SqlConnection(config.GetConnectionString("Default"));
        var docs = await sql.QueryAsync<DocumentSummary>(
            "SELECT Id, FileName, ChunkCount, UploadedUtc FROM Documents ORDER BY UploadedUtc DESC");
        return Ok(docs);
    }
}