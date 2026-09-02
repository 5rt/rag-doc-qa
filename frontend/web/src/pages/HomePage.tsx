import { Link } from 'react-router-dom';
import Seo from '../seo/Seo';

export default function HomePage() {
  return (
    <>
      <Seo
        title="Ask questions about your own documents"
        description="Upload a document and ask questions about it in plain English. Every answer is grounded in the source text and cites the passage it came from."
        path="/"
      />
      <h1>Ask questions about your own documents</h1>
      <p>
        Upload a PDF or text file and ask about it in plain English. Answers are built
        only from passages retrieved out of your document, and each one shows the text
        it came from. When the answer is not in the document, it says so instead of
        guessing.
      </p>
      <p>
        <Link to="/upload">Upload a document</Link> to start, or read{' '}
        <Link to="/how-it-works">how the retrieval works</Link>.
      </p>
    </>
  );
}
