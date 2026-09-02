import { useState } from "react";
import { Link } from "react-router-dom";
import { uploadDocument } from "../api";
import Seo from "../seo/Seo";

export default function UploadPage() {
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit() {
    if (!file) return;
    setBusy(true);
    setError(null);
    setResult(null);
    try {
      const data = await uploadDocument(file);
      setResult(`Indexed ${data.chunkCount} passages from ${data.fileName}.`);
      setFile(null);
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
          {busy ? "Indexing..." : "Upload and index"}
        </button>
      </div>

      {busy && (
        <p role="status" className="notice">
          Indexing {file?.name}. Each passage is embedded separately, so a long
          PDF can take a minute. Do not navigate away.
        </p>
      )}
      {result && (
        <div role="status" className="notice notice-success">
          <strong>Done.</strong> {result}{" "}
          <Link to="/ask">Ask a question about it</Link>
        </div>
      )}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
    </>
  );
}
