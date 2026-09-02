using RagDocQa.Api.Search;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Allow the Vite dev server to call this API. Never AllowAnyOrigin() —
// that lets any site on the internet call your API from a visitor's browser.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Create the vector index on startup if it doesn't already exist.
// Safe to run every time — CreateOrUpdate is idempotent.
await SearchSetup.EnsureIndexAsync(
    builder.Configuration["Search:Endpoint"]!,
    builder.Configuration["Search:ApiKey"]!);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();