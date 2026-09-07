using System.Net;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
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
    // "Database is not currently available" - serverless auto-pause, not a
    // fault. Self-healing: the next call succeeds once the database wakes.
    private const int SqlDatabasePaused = 40613;

    private SearchClient CreateSearchClient() => new(
        new Uri(config["Search:Endpoint"]!),
        SearchSetup.IndexName,
        new AzureKeyCredential(config["Search:ApiKey"]!));

    private SqlConnection CreateSqlConnection() =>
        new(config.GetConnectionString("Default"));

    /// <summary>
    /// Rebuilds readable text from a PDF page. Page.Text concatenates the raw
    /// glyph runs with no separators, which fuses words together and drops line
    /// breaks entirely. Grouping words by vertical position recovers the lines.
    /// </summary>
    private static string ExtractPageText(Page page)
    {
        var lines = page.GetWords()
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

        var chunks = Chunker.Split(text);
        var documentId = Guid.NewGuid();

        // Embed everything BEFORE writing to either store. This is the slow,
        // quota-consuming step and the most likely to fail, so failing here
        // leaves nothing behind to clean up.
        var vectors = new List<float[]>(chunks.Count);
        try
        {
            foreach (var chunk in chunks)
                vectors.Add(await gemini.EmbedAsync(chunk, "RETRIEVAL_DOCUMENT"));
        }
        catch (GeminiException ex) when (ex.Status == HttpStatusCode.TooManyRequests)
        {
            return StatusCode(429, new
            {
                error = "The daily embedding quota is used up. Try again tomorrow, "
                      + "or upload a shorter document."
            });
        }
        catch (GeminiException)
        {
            return StatusCode(502, new { error = "The embedding service failed. Try again." });
        }

        var searchClient = CreateSearchClient();
        await using var sql = CreateSqlConnection();

        // SQL row first. If this fails, nothing is in the index yet.
        try
        {
            await sql.ExecuteAsync(
                "INSERT INTO Documents (Id, FileName, ChunkCount) VALUES (@Id, @FileName, @ChunkCount)",
                new { Id = documentId, FileName = file.FileName, ChunkCount = chunks.Count });
        }
        catch (SqlException ex) when (ex.Number == SqlDatabasePaused)
        {
            return StatusCode(503, new
            {
                error = "The database is waking up. Try again in a few seconds."
            });
        }

        // Then the vectors. If this fails, remove the row we just wrote - never
        // leave vectors with no metadata, since they are invisible in the filter
        // dropdown and very hard to notice.
        try
        {
            var batch = new List<object>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                batch.Add(new
                {
                    id = $"{documentId}-{i}",
                    documentId = documentId.ToString(),
                    fileName = file.FileName,
                    chunkIndex = i,
                    content = chunks[i],
                    contentVector = vectors[i]
                });
            }

            // Without ThrowOnAnyError a partial failure comes back as a 200 with
            // per-document statuses, and we would report success.
            await searchClient.UploadDocumentsAsync(
                batch, new IndexDocumentsOptions { ThrowOnAnyError = true });
        }
        catch
        {
            await RollBackAsync(sql, searchClient, documentId);
            return StatusCode(502, new { error = "Indexing failed. Nothing was saved - try again." });
        }

        return Ok(new { documentId, fileName = file.FileName, chunkCount = chunks.Count });
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        await using var sql = CreateSqlConnection();
        try
        {
            var docs = await sql.QueryAsync<DocumentSummary>(
                "SELECT Id, FileName, ChunkCount, UploadedUtc FROM Documents ORDER BY UploadedUtc DESC");
            return Ok(docs);
        }
        catch (SqlException ex) when (ex.Number == SqlDatabasePaused)
        {
            return StatusCode(503, new
            {
                error = "The database is waking up. Try again in a few seconds."
            });
        }
    }

    /// <summary>
    /// Removes a document from both stores. Without this the only remedy for a
    /// bad upload was deleting the whole index in the portal, which silently
    /// no-ops if the API recreates it before the delete propagates.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var searchClient = CreateSearchClient();

        // Query for the chunk keys rather than rebuilding "{id}-{i}" from the
        // SQL ChunkCount, so this still works when the SQL row is already gone.
        var keys = await FindChunkKeysAsync(searchClient, id);

        // Vectors first, deliberately. If the SQL delete then fails you are left
        // with a row that appears in the dropdown and returns nothing: visible
        // and obvious. The other order leaves invisible orphans.
        if (keys.Count > 0)
        {
            await searchClient.DeleteDocumentsAsync(
                "id", keys, new IndexDocumentsOptions { ThrowOnAnyError = true });
        }

        int rows;
        await using var sql = CreateSqlConnection();
        try
        {
            rows = await sql.ExecuteAsync("DELETE FROM Documents WHERE Id = @Id", new { Id = id });
        }
        catch (SqlException ex) when (ex.Number == SqlDatabasePaused)
        {
            return StatusCode(503, new
            {
                error = $"Removed {keys.Count} passages, but the database is asleep. "
                      + "Call this again in a few seconds to finish."
            });
        }

        if (rows == 0 && keys.Count == 0)
            return NotFound(new { error = "No document with that id." });

        return Ok(new { documentId = id, chunksDeleted = keys.Count, rowsDeleted = rows });
    }

    /// <summary>
    /// Every chunk key carrying this documentId. Size 1000 is the Azure AI
    /// Search page maximum - far past what the 20 MB upload cap can produce.
    /// </summary>
    private static async Task<List<string>> FindChunkKeysAsync(SearchClient client, Guid documentId)
    {
        var options = new SearchOptions
        {
            Filter = $"documentId eq '{documentId}'",
            Size = 1000
        };
        options.Select.Add("id");

        var keys = new List<string>();
        var response = await client.SearchAsync<SearchDocument>("*", options);
        await foreach (var hit in response.Value.GetResultsAsync())
            keys.Add(hit.Document["id"].ToString()!);

        return keys;
    }

    /// <summary>
    /// Best effort cleanup after a failed upload. Deliberately swallows its own
    /// errors: the caller is already returning a failure, and throwing here
    /// would replace a clear message with a bare 500.
    /// </summary>
    private static async Task RollBackAsync(SqlConnection sql, SearchClient client, Guid documentId)
    {
        try
        {
            var keys = await FindChunkKeysAsync(client, documentId);
            if (keys.Count > 0)
                await client.DeleteDocumentsAsync("id", keys);
        }
        catch { /* ignored */ }

        try
        {
            await sql.ExecuteAsync("DELETE FROM Documents WHERE Id = @Id", new { Id = documentId });
        }
        catch { /* ignored */ }
    }
}
