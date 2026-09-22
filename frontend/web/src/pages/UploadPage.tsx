import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { deleteDocument, listDocuments, uploadDocument } from "../api";
import type { DocumentSummary } from "../types";
import Seo from "../seo/Seo";

export default function UploadPage() {
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [docs, setDocs] = useState<DocumentSummary[] | null>(null);
  const [deleting, setDeleting] = useState<string | null>(null);
  const [waking, setWaking] = useState(false);

  function refresh() {
    listDocuments(() => setWaking(true))
      .then(setDocs)
      .catch(() => setDocs(null))
      .finally(() => setWaking(false));
  }

  useEffect(refresh, []);

  async function handleDelete(doc: DocumentSummary) {
    if (!window.confirm(`Delete ${doc.fileName}? Its passages will no longer be searched.`)) return;
    setDeleting(doc.id);
    setError(null);
    setResult(null);
    try {
      await deleteDocument(doc.id);
      setDocs((d) => d?.filter((x) => x.id !== doc.id) ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed.");
    } finally {
      setDeleting(null);
    }
  }

  async function handleSubmit() {
    if (!file) return;
    setBusy(true);
    setError(null);
    setResult(null);
    try {
      const data = await uploadDocument(file);
      setResult(`Indexed ${data.chunkCount} passages from ${data.fileName}.`);
      setFile(null);
      refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Seo
        title="Upload a document"
        description="Add a PDF or text file. It is split into passages and indexed so you can ask questions about it straight away."
        path="/upload"
      />
      <h1>Upload a document</h1>
      <p>
        PDF or plain text, up to 20 MB. Scanned PDFs will not work without OCR.
      </p>

      {waking && (
        <p role="status" className="notice">
          Waking up the server. The first visit after a quiet spell can take up
          to a minute.
        </p>
      )}

      <div className="upload-form">
        <label htmlFor="file-input">Choose a file</label>
        <input
          id="file-input"
          type="file"
          accept=".pdf,.txt"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          disabled={busy}
        />
        <button type="button" onClick={handleSubmit} disabled={!file || busy}>
          {busy ? "Indexing…" : "Upload and index"}
        </button>
      </div>

      {busy && (
        <p role="status" className="notice">
          Indexing {file?.name}. Each passage is embedded separately, so a long
          PDF can take a minute. Do not navigate away.
        </p>
      )}
      {result && (
        <div role="status" className="notice">
          <strong>Done.</strong> {result}{" "}
          <Link to="/ask">Ask a question about it</Link>
        </div>
      )}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {docs && docs.length > 0 && (
        <section aria-labelledby="docs-heading">
          <h2 id="docs-heading">Your documents</h2>
          <ul className="doc-list">
            {docs.map((d) => (
              <li key={d.id}>
                <div>
                  <p className="doc-name">{d.fileName}</p>
                  <p className="source-meta">
                    {d.chunkCount} passages, uploaded{" "}
                    {/* SQL returns UTC without a zone marker. */}
                    {new Date(d.uploadedUtc.endsWith("Z") ? d.uploadedUtc : d.uploadedUtc + "Z").toLocaleDateString()}
                  </p>
                </div>
                <button
                  type="button"
                  className="button-quiet"
                  onClick={() => handleDelete(d)}
                  disabled={deleting !== null || busy}
                  aria-label={`Delete ${d.fileName}`}
                >
                  {deleting === d.id ? "Deleting…" : "Delete"}
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  );
}
