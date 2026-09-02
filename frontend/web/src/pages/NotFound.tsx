import { Link, useLocation } from 'react-router-dom';
import Seo from '../seo/Seo';

export default function NotFound() {
  const { pathname } = useLocation();

  return (
    <>
      <Seo
        title="Page not found"
        description="That page does not exist. Head back to the home page or start a new question."
        path={pathname}
        noIndex
      />
      <main className="notfound">
        <p className="notfound-code">404</p>
        <h1>There is nothing at this address</h1>
        <p>
          <code>{pathname}</code> does not match any page. It may have been renamed, or
          the link that brought you here may be out of date.
        </p>
        <nav aria-label="Suggested pages">
          <ul>
            <li><Link to="/">Start from the home page</Link></li>
            <li><Link to="/upload">Upload a document</Link></li>
            <li><Link to="/ask">Ask a question</Link></li>
            <li><Link to="/how-it-works">See how the retrieval works</Link></li>
          </ul>
        </nav>
      </main>
    </>
  );
}
