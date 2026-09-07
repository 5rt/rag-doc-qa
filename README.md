# rag-doc-qa

Ask questions about your own documents in plain English. Answers are built only
from passages retrieved out of the documents you upload, and every answer cites
the passage it came from. When the answer is not in your documents, it says so
instead of guessing.

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

Four secrets, via user secrets — never in a file:

    cd backend/RagDocQa.Api
    dotnet user-secrets set "Llm:ApiKey" "..."
    dotnet user-secrets set "Search:Endpoint" "https://<name>.search.windows.net"
    dotnet user-secrets set "Search:ApiKey" "..."
    dotnet user-secrets set "ConnectionStrings:Default" "..."

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

## API

| Method | Route | Notes |
|---|---|---|
| POST | `/api/documents` | multipart upload, PDF or txt, 20 MB cap |
| GET | `/api/documents` | list of uploaded documents |
| DELETE | `/api/documents/{id}` | removes vectors and the SQL row |
| POST | `/api/ask` | `{ question, documentId? }` |

## Known limits

- **No authentication.** Every endpoint is open. Fine on localhost; not fine
  deployed, where anyone with the URL can upload, list every filename, and spend
  the Gemini quota.
- **No deletion of uploaded text beyond the delete endpoint.** Document text is
  stored recoverably in the search index and sent to Google's API for embedding
  and answering.
- **Two-column PDFs interleave.** Words sharing a Y position merge into one
  line. Fine for prose and most CVs, wrong for academic papers.
  `DocstrumBoundingBoxes` ships with PdfPig if this becomes a real problem.
- **Scanned PDFs need OCR** and are rejected as having no readable text.
- **Free-tier quota** is 1,500 embedding calls per day, one per chunk.
- **`SearchSetup` uses `CreateOrUpdateIndexAsync`.** Deleting the index in the
  Azure portal and restarting does *not* reliably reset it — if the delete has
  not propagated, the call finds the index still present and silently no-ops,
  leaving every old vector in place. Use `DELETE /api/documents/{id}`.
- **Chunking is O(n^2)** in word count. Irrelevant at this scale.
