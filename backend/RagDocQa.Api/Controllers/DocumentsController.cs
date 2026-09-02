using Azure;
using Azure.Search.Documents;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RagDocQa.Api.Ai;
using RagDocQa.Api.Ingest;
using RagDocQa.Api.Search;
using UglyToad.PdfPig;

namespace RagDocQa.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController(GeminiClient gemini, IConfiguration config) : ControllerBase
{
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
                text = string.Join("\n", pdf.GetPages().Select(p => p.Text));
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
        var docs = await sql.QueryAsync(
            "SELECT Id, FileName, ChunkCount, UploadedUtc FROM Documents ORDER BY UploadedUtc DESC");
        return Ok(docs);
    }
}
