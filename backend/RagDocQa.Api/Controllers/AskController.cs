using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.AspNetCore.Mvc;
using RagDocQa.Api.Ai;
using RagDocQa.Api.Search;

namespace RagDocQa.Api.Controllers;

public record AskRequest(string Question, string? DocumentId);

[ApiController]
[Route("api/ask")]
public class AskController(GeminiClient gemini, IConfiguration config) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] AskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Ask a question first." });

        Guid? documentId = null;
        if (!string.IsNullOrWhiteSpace(request.DocumentId))
        {
            if (!Guid.TryParse(request.DocumentId, out var parsed))
                return BadRequest(new { error = "documentId is not a valid id." });
            documentId = parsed;
        }

        // 1. Embed the question, using the QUERY task type this time
        var questionVector = await gemini.EmbedAsync(request.Question, "RETRIEVAL_QUERY");

        // 2. Find the 5 nearest passages
        var searchClient = new SearchClient(
            new Uri(config["Search:Endpoint"]!),
            SearchSetup.IndexName,
            new AzureKeyCredential(config["Search:ApiKey"]!));

        var options = new SearchOptions
        {
            Size = 5,
            VectorSearch = new()
            {
                Queries =
                {
                    new VectorizedQuery(questionVector)
                    {
                        KNearestNeighborsCount = 5,
                        Fields = { "contentVector" }
                    }
                },
                FilterMode = VectorFilterMode.PreFilter
            }
        };

        if (documentId is not null)
            options.Filter = $"documentId eq '{documentId}'";

        options.Select.Add("content");
        options.Select.Add("fileName");
        options.Select.Add("chunkIndex");

        var response = await searchClient.SearchAsync<SearchDocument>(null, options);

        var sources = new List<object>();
        var passages = new List<string>();
        await foreach (var hit in response.Value.GetResultsAsync())
        {
            var content = hit.Document["content"].ToString()!;
            passages.Add(content);
            sources.Add(new
            {
                fileName = hit.Document["fileName"].ToString(),
                chunkIndex = hit.Document["chunkIndex"],
                content,
                score = hit.Score
            });
        }

        if (passages.Count == 0)
            return Ok(new
            {
                answer = documentId is null
                    ? "No documents have been uploaded yet."
                    : "That document has no matching passages.",
                sources
            });

        // 3. Ask the model, giving it only those passages
        var answer = await gemini.AnswerAsync(request.Question, passages);

        return Ok(new { answer, sources });
    }
}