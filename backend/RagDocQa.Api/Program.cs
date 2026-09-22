using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using RagDocQa.Api.Auth;
using RagDocQa.Api.Search;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient<RagDocQa.Api.Ai.GeminiClient>();

// Allow the Vite dev server to call this API. Never AllowAnyOrigin() —
// that lets any site on the internet call your API from a visitor's browser.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

// App Service terminates TLS at the front end and forwards over http.
// Without this the app sees http and the wrong client IP: HTTPS redirection
// can loop, and every visitor collapses into a single rate-limit partition.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var apiKey = builder.Configuration["Auth:ApiKey"]
    ?? throw new InvalidOperationException("Auth:ApiKey is not configured.");

var rl = builder.Configuration.GetSection("RateLimits");
var perIpPerMinute = rl.GetValue("PerIpPerMinute", 30);
var askPerIpPerMinute = rl.GetValue("AskPerIpPerMinute", 5);
var askPerDay = rl.GetValue("AskPerDay", 150);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (ctx, token) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            ctx.HttpContext.Response.Headers.RetryAfter =
                ((int)retry.TotalSeconds).ToString();

        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            status = 429,
            title = "Too many requests",
            detail = "This is a public demo on a shared API quota. Wait a minute and try again."
        }, options: null, contentType: "application/problem+json", cancellationToken: token);
    };

    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(

        // a. per-IP burst control on everything
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
            RateLimitPartition.GetFixedWindowLimiter(ClientKey(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perIpPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                })),

        // b. per-IP on /api/ask — the endpoint that spends Gemini quota
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
            http.Request.Path.StartsWithSegments("/api/ask")
                ? RateLimitPartition.GetFixedWindowLimiter($"ask:{ClientKey(http)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = askPerIpPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    })
                : RateLimitPartition.GetNoLimiter<string>("none")),

        // c. app-wide daily cap on /api/ask. In-memory, so it resets on every
        // cold start — F1 sleeps after 20 min idle. Stops a sustained script,
        // is not a real daily budget.
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
            http.Request.Path.StartsWithSegments("/api/ask")
                ? RateLimitPartition.GetFixedWindowLimiter("ask:global",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = askPerDay,
                        Window = TimeSpan.FromHours(24),
                        QueueLimit = 0
                    })
                : RateLimitPartition.GetNoLimiter<string>("none")));

    static string ClientKey(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
});
var app = builder.Build();

app.Logger.LogInformation(
    "env={Env} perIp={PerIp} askPerIp={AskPerIp} askPerDay={AskPerDay}",
    app.Environment.EnvironmentName, perIpPerMinute, askPerIpPerMinute, askPerDay);

// Create the vector index on startup if it doesn't already exist.
// CreateOrUpdate is idempotent — which means it also silently no-ops if the
// index still exists. Do NOT rely on this to reset the index; use
// DELETE /api/documents/{id}. See HANDOVER corrections 1 and 2.
await SearchSetup.EnsureIndexAsync(
    builder.Configuration["Search:Endpoint"]!,
    builder.Configuration["Search:ApiKey"]!);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();   // first — everything below reads scheme and IP
app.UseHttpsRedirection();
app.UseCors();               // before the limiter, so 429s carry CORS headers
app.UseRateLimiter();        // before the key check, so guessing the key is also rate-limited
app.UseApiKeyAuth(apiKey);
app.UseAuthorization();
app.MapControllers();

app.Run();