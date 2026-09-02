import { Link } from 'react-router-dom';
import Seo from '../seo/Seo';

export default function HowItWorksPage() {
  return (
    <>
      <Seo
        title="How the retrieval works"
        description="Chunking, embeddings, vector search and grounding — how this app finds the right passage and keeps answers tied to the source."
        path="/how-it-works"
      />
      <h1>How the retrieval works</h1>

      <h2>Indexing</h2>
      <p>
        An uploaded file is stripped to plain text and split into overlapping windows
        of about 350 words, with 50 words of overlap. The overlap matters: without it,
        a sentence that straddles a boundary is cut in half and neither piece answers
        the question well.
      </p>
      <p>
        Each passage is turned into a 768-number vector by Gemini and stored in Azure
        AI Search. The filename, upload date and passage count go to SQL Server
        instead. Relational data lives in a relational store; vectors live in a vector
        store.
      </p>

      <h2>Answering</h2>
      <p>
        A question is embedded the same way, and the five closest passages are found by
        vector similarity. Those passages and the question go to the model with an
        instruction to answer only from what it was given.
      </p>
      <p>
        Documents are embedded with the RETRIEVAL_DOCUMENT task type and questions with
        RETRIEVAL_QUERY. Questions and their answers are not semantically similar, so
        using the matching task type on each side measurably improves which passages
        come back.
      </p>

      <h2>Why it does not make things up</h2>
      <p>
        The model is told to reply that something is not covered rather than fall back
        on outside knowledge. That refusal is the point of retrieval-augmented
        generation: answers stay tied to a source you can check.
      </p>
      <p>
        <Link to="/ask">Try a question</Link> or read the{' '}
        <a href="https://github.com/5rt/rag-doc-qa">source on GitHub</a>.
      </p>
    </>
  );
}
