import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { askQuestion, listDocuments } from "../api";
import type { AskResponse, DocumentSummary } from "../types";
import Seo from "../seo/Seo";

export default function AskPage() {
  const [question, setQuestion] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<AskResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);
  const [docs, setDocs] = useState<DocumentSummary[]>([]);
  const [documentId, setDocumentId] = useState("");

  useEffect(() => {
    listDocuments()
      .then(setDocs)
      .catch(() => setDocs([]));
  }, []);

  // Cancel any in-flight request if the user navigates away, so we never
  // call setState on an unmounted component.
  useEffect(() => () => controller.current?.abort(), []);

  async function handleAsk() {
    if (!question.trim()) return;
    controller.current?.abort();
    controller.current = new AbortController();

    setBusy(true);
    setError(null);
    try {
      const data = await askQuestion(
        question,
        documentId || undefined,
        controller.current.signal,
      );
      setResult(data);
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") return;
      setError(err instanceof Error ? err.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  const scopeName = docs.find((d) => d.id === documentId)?.fileName;

  return (
    <>
      <Seo
        title="Ask a question"
        description="Ask a question in plain English. The answer is drawn only from your uploaded documents, and every claim cites the passage it came from."
        path="/ask"
      />
      <h1>Ask a question</h1>
      <p>
        Answers come only from your uploaded documents. Sources are shown below
        each answer.
      </p>

      <div className="ask-form">
        <label htmlFor="doc-filter">Search in</label>
        <select
          id="doc-filter"
          value={documentId}
          onChange={(e) => setDocumentId(e.target.value)}
          disabled={busy || docs.length === 0}
        >
          <option value="">All documents</option>
          {docs.map((d) => (
            <option key={d.id} value={d.id}>
              {d.fileName}
            </option>
          ))}
        </select>

        <label htmlFor="question">Your question</label>
        <textarea
          id="question"
          rows={3}
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="How many staff are in the Wellington office?"
          disabled={busy}
        />
        <button
          type="button"
          onClick={handleAsk}
          disabled={busy || !question.trim()}
        >
          {busy ? "Searching..." : "Ask"}
        </button>
      </div>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      {result && (
        <section aria-labelledby="answer-heading">
          <h2 id="answer-heading">Answer</h2>
          <p className="answer">{result.answer}</p>

          <h2>Sources</h2>
          {result.sources.length === 0 ? (
            <p>
              {scopeName ? (
                <>No passages matched in {scopeName}. Try All documents.</>
              ) : (
                <>
                  Nothing indexed yet.{" "}
                  <Link to="/upload">Upload a document</Link> first.
                </>
              )}
            </p>
          ) : (
            <ol className="sources">
              {result.sources.map((source, i) => (
                <li key={`${source.fileName}-${source.chunkIndex}-${i}`}>
                  <p className="source-meta">
                    {source.fileName}, passage {source.chunkIndex + 1}
                  </p>
                  <span
                    className="source-score"
                    aria-label={`Similarity ${source.score.toFixed(3)}`}
                  >
                    <span
                      style={{ width: `${Math.round(source.score * 100)}%` }}
                    />
                  </span>
                  <details>
                    <summary>{source.content.slice(0, 160).trim()}…</summary>
                    <blockquote>{source.content}</blockquote>
                  </details>
                </li>
              ))}
            </ol>
          )}
        </section>
      )}
    </>
  );
}