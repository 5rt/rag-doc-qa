using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using RagDocQa.Api.Ai;

namespace RagDocQa.Tests;

public class GeminiRetryTests
{
    private const string Answer = """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""";

    /// <summary>Replies with the given statuses in order and counts the calls.</summary>
    private sealed class ScriptedHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var status = statuses[Calls++];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? Answer : "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private static GeminiClient Client(ScriptedHandler handler) => new(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:BaseUrl"] = "https://gemini.test",
            ["Llm:ChatModel"] = "chat",
            ["Llm:ApiKey"] = "key",
        }).Build());

    [Fact]
    public async Task Overloaded_twice_then_answers()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        Assert.Equal("ok", await Client(handler).AnswerAsync("q", ["p"]));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Gives_up_after_three_overloaded_replies()
    {
        var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<GeminiException>(() => Client(handler).AnswerAsync("q", ["p"]));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.Status);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Quota_exhaustion_is_not_retried()
    {
        var handler = new ScriptedHandler(HttpStatusCode.TooManyRequests);

        var ex = await Assert.ThrowsAsync<GeminiException>(() => Client(handler).AnswerAsync("q", ["p"]));
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.Status);
        Assert.Equal(1, handler.Calls);
    }
}
