# rag-doc-qa

Ask questions about your own documents in plain English. Answers are built only
from passages retrieved out of the documents you upload, and every answer cites
the passage it came from. When the answer is not in your documents, it says so
instead of guessing.

**Live:** https://happy-mushroom-0162ee100.6.azurestaticapps.net (behind an
access key)

**Stack:** React 19 + TypeScript + Vite 8 · ASP.NET Core (net10.0) ·
Azure AI Search · Azure SQL · Gemini API

## How it works

Upload:

    PDF/txt -> PdfPig extracts text -> 350-word chunks (50-word overlap)
    -> Gemini embeds each chunk (768 dims, RETRIEVAL_DOCUMENT)
    -> vectors to Azure AI Search, metadata to Azure SQL

Ask:

    question -> Gemini embeds it (768 dims, RETRIEVAL_QUERY)
    -> vector search returns top 5 passages
    -> Gemini answers using only those passages, with [1][2] citations

## Why it is built this way

**350-word chunks with 50-word overlap.** A passage has to be small enough that
its embedding means one thing, and large enough to answer a question on its own.
The overlap stops a sentence that straddles a boundary from being cut in half,
leaving neither piece able to answer.

**768 dimensions, not the 3072 default.** The Azure AI Search free tier caps at
50 MB. Gemini only auto-normalises embeddings at full size, so `GeminiClient`
scales to unit length manually — without that, cosine similarity is wrong.
`SearchSetup.Dimensions` and the embed request must always match.

**Asymmetric task types.** `RETRIEVAL_DOCUMENT` when storing, `RETRIEVAL_QUERY`
when searching. A question and its answer are not semantically similar, so using
the matching type on each side measurably improves retrieval.

**PreFilter, not PostFilter.** Post-filtering takes the global top 5 and then
discards other documents hits, so a filtered question can come back with two
passages or none. Pre-filtering narrows to the chosen document first, then runs
KNN over what is left.

**Two stores, written in a fixed order.** Vectors live in Azure AI Search,
metadata in Azure SQL. Upload embeds first (the slow, quota-consuming step, so
failing there writes nothing), then inserts the SQL row, then indexes the
vectors — rolling the row back if indexing fails. Vectors without a SQL row are
invisible in the filter dropdown and quietly pollute retrieval, so no ordering
is allowed to produce them.

**PDF text needs rebuilding.** PdfPig `Page.Text` concatenates glyph runs with
no separators, fusing words and dropping line breaks entirely. Grouping words by
rounded Y position (PDF coordinates start bottom-left) and ordering each line by
X recovers the original lines. This affects chunk boundaries and every
embedding, not just readability.

## Running it locally

Needs .NET 10 SDK and Node 20+.

Five secrets:

    cd backend/RagDocQa.Api
    dotnet user-secrets set "Llm:ApiKey" "..."
    dotnet user-secrets set "Search:Endpoint" "https://<name>.search.windows.net"
    dotnet user-secrets set "Search:ApiKey" "..."
    dotnet user-secrets set "ConnectionStrings:Default" "..."
    dotnet user-secrets set "Auth:ApiKey" "<any long random string>"

The SQL table:

    CREATE TABLE Documents (
        Id UNIQUEIDENTIFIER PRIMARY KEY,
        FileName NVARCHAR(400) NOT NULL,
        ChunkCount INT NOT NULL,
        UploadedUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );

The search index is created on startup if it does not exist.

Terminal 1:

    cd backend/RagDocQa.Api
    dotnet run --launch-profile https

Terminal 2:

    cd frontend/web
    npm install
    npm run dev

Then http://localhost:5173

Tests (chunker and PDF line rebuilding):

    dotnet test RagDocQa.slnx

## Evaluation

`eval/run.mjs` measures retrieval and grounding against a running API. It
uploads `eval/handbook.txt` (a fictional 2,800-word staff handbook, 10
passages), asks 15 paraphrased questions scoped to it, checks 3 questions the
handbook cannot answer, then deletes the document:

    API_KEY=... node eval/run.mjs          # live API; API_URL=... for local

- **Retrieval:** where the passage holding the answer ranks in the top 5
  (hit@1, hit@5, MRR). Questions avoid the handbook's wording, so matches have
  to come from meaning.
- **Answers:** whether the answer contains the expected fact.
- **Refusals:** whether unanswerable questions get "That is not covered in the
  uploaded documents."

Run it before and after changing chunk size, overlap, top-k or the prompt. It
spends about 40 Gemini calls, 18 of them answers, and takes around three
minutes, paced to stay under the per-IP ask limit. On the free tier that is
almost the whole day's answer quota (see Known limits), so run it on a paid
key or on a day the site is not in use.

## Deployment

Every push to `main` runs CI, and if lint, build, format and tests pass, deploys
both halves:

| Part | Azure resource | How |
|---|---|---|
| API | App Service `ragdocqa-api` (Linux, F1) | `dotnet publish` + publish profile |
| Frontend | Static Web App `ragdocqa-web` (Free) | `npm run build` with `VITE_API_URL` baked in, then upload `dist/` |

Both live in resource group `rag-doc-qa`. CI needs two repository secrets:

    AZURE_WEBAPP_PUBLISH_PROFILE     az webapp deployment list-publishing-profiles -g rag-doc-qa -n ragdocqa-api --xml
    AZURE_STATIC_WEB_APPS_API_TOKEN  az staticwebapp secrets list -g rag-doc-qa -n ragdocqa-web --query properties.apiKey -o tsv

The API's secrets are App Service application settings, with `__` for nesting:
`Llm__ApiKey`, `Search__Endpoint`, `Search__ApiKey`, `ConnectionStrings__Default`,
`Auth__ApiKey`, plus `Cors__AllowedOrigin` set to the Static Web App URL.

Gotchas hit on the first deploy:

- **Basic publishing auth must be on** for the publish profile to work
  (`basicPublishingCredentialsPolicies/scm`, `allow=true`). A profile fetched
  while it was off has a blank password and fails with "Publish profile is
  invalid" — re-fetch it after turning it on.
- **The API will not start without `Auth__ApiKey`** and returns 503 until it is
  set. To rotate the key, set a new value; the frontend clears the old key on
  its first 401 and asks again.
- **`staticwebapp.config.json`** rewrites unknown paths to `index.html`, so
  refreshing `/ask` does not 404.
- **The host appears in** `index.html` (og tags), `public/sitemap.xml`,
  `public/robots.txt` and `src/seo/siteUrl.ts`. Change all four if the domain
  changes.

## API

Every route requires an `X-Api-Key` header matching the configured
`Auth:ApiKey` secret. A wrong or missing key gets a 401 before anything else
runs — before CORS-exempt preflight, but after rate limiting, so guessing the
key is also throttled. The frontend asks for the key once and keeps it in
`localStorage`.

| Method | Route | Notes |
|---|---|---|
| POST | `/api/documents` | multipart upload, PDF or txt, 20 MB cap |
| GET | `/api/documents` | list of uploaded documents |
| DELETE | `/api/documents/{id}` | removes vectors and the SQL row |
| POST | `/api/ask` | `{ question, documentId? }` |

## Known limits

- **One shared key, not per-user accounts.** Anyone with the key can upload,
  list every filename, and spend the Gemini quota — it stops strangers, not a
  trusted-but-nosy holder of the link. Fine for a single-owner demo; would need
  real accounts to support distinct users.
- **No deletion of uploaded text beyond the delete endpoint.** Document text is
  stored recoverably in the search index and sent to Google's API for embedding
  and answering.
- **Two-column PDFs interleave.** Words sharing a Y position merge into one
  line. Fine for prose and most CVs, wrong for academic papers.
  `DocstrumBoundingBoxes` ships with PdfPig if this becomes a real problem.
- **Scanned PDFs need OCR** and are rejected as having no readable text.
- **Free-tier Gemini quota is tiny for answers.** The chat model allows 20
  `generateContent` calls per day per project, so the whole site can answer
  about 20 questions a day. Embeddings have a separate, larger quota (one call
  per chunk on upload, one per question). Quotas reset at midnight Pacific
  time. A 429 from Gemini is reported as "daily quota used up", though Gemini
  also returns 429 for per-minute limits. The API's own `AskPerDay` cap (300)
  never comes into play on the free tier.
- **`SearchSetup` uses `CreateOrUpdateIndexAsync`.** Deleting the index in the
  Azure portal and restarting does *not* reliably reset it — if the delete has
  not propagated, the call finds the index still present and silently no-ops,
  leaving every old vector in place. Delete documents from the Upload page
  (or `DELETE /api/documents/{id}`) instead.
- **Chunking is O(n^2)** in word count. Irrelevant at this scale.
