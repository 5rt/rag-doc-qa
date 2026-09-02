using System.Net.Http.Json;
using System.Text.Json;

namespace RagDocQa.Api.Ai;

public class GeminiClient(HttpClient http, IConfiguration config)
{
    private string Base  => config["Llm:BaseUrl"]!;
    private string Embed => config["Llm:EmbeddingModel"]!;
    private string Chat  => config["Llm:ChatModel"]!;
    private string Key   => config["Llm:ApiKey"]
        ?? throw new InvalidOperationException("Llm:ApiKey is not configured.");

    /// <summary>Turns one piece of text into a 768-number vector.</summary>
    /// <param name="taskType">
    /// RETRIEVAL_DOCUMENT when storing a passage, RETRIEVAL_QUERY when searching.
    /// Questions and answers are not semantically similar, so using the matching
    /// task type on each side measurably improves retrieval.
    /// </param>
    public async Task<float[]> EmbedAsync(string text, string taskType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/models/{Embed}:embedContent")
        {
            Content = JsonContent.Create(new
            {
                model = $"models/{Embed}",
                content = new { parts = new[] { new { text } } },
                taskType,
                outputDimensionality = Search.SearchSetup.Dimensions
            })
        };
        request.Headers.Add("x-goog-api-key", Key);

        var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var values = json.GetProperty("embedding").GetProperty("values")
            .EnumerateArray().Select(v => v.GetSingle()).ToArray();

        // Gemini only auto-normalizes at the full 3072 dimensions. We asked for
        // 768, so scale to unit length ourselves for correct cosine similarity.
        return Normalize(values);
    }

    /// <summary>Writes the answer using only the passages we supply.</summary>
    public async Task<string> AnswerAsync(string question, IEnumerable<string> passages)
    {
        var context = string.Join("\n\n", passages.Select((p, i) => $"[{i + 1}] {p}"));

        var prompt = $"""
            Answer the question using only the numbered context below.
            Cite the passages you used like [1] or [2].
            If the context does not contain the answer, reply exactly:
            "That is not covered in the uploaded documents."
            Do not use outside knowledge.

            Context:
            {context}

            Question: {question}
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/models/{Chat}:generateContent")
        {
            Content = JsonContent.Create(new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } }
            })
        };
        request.Headers.Add("x-goog-api-key", Key);

        var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("candidates")[0]
                   .GetProperty("content").GetProperty("parts")[0]
                   .GetProperty("text").GetString() ?? "";
    }

    private static float[] Normalize(float[] v)
    {
        var length = MathF.Sqrt(v.Sum(x => x * x));
        return length == 0 ? v : v.Select(x => x / length).ToArray();
    }
}
