using System.Security.Cryptography;
using System.Text;

namespace RagDocQa.Api.Auth;

/// <summary>
/// Gates every request behind a single shared key. This is a public demo with
/// one owner, not a multi-user app - a shared secret is enough to stop
/// strangers from spending the Gemini quota, and it costs no signup flow or
/// accounts table.
/// </summary>
public static class ApiKeyAuth
{
    public const string HeaderName = "X-Api-Key";

    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder app, string apiKey)
    {
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

        return app.Use(async (context, next) =>
        {
            var provided = context.Request.Headers[HeaderName].ToString();

            // Hash both sides so FixedTimeEquals never sees mismatched lengths,
            // and the comparison itself does not leak the key length or a byte
            // at a time through timing.
            var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
            if (!CryptographicOperations.FixedTimeEquals(providedHash, expected))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = 401,
                    title = "Unauthorized",
                    detail = "Missing or invalid API key."
                }, options: null, contentType: "application/problem+json");
                return;
            }

            await next(context);
        });
    }
}
