import { Link, Outlet } from 'react-router-dom';

export default function Layout() {
  return (
    <>
      <header className="site-header">
        <Link to="/" className="site-title">Document Q&amp;A</Link>
        <nav aria-label="Main">
          <Link to="/upload">Upload</Link>
          <Link to="/ask">Ask</Link>
          <Link to="/how-it-works">How it works</Link>
        </nav>
      </header>
      <main><Outlet /></main>
      <footer className="site-footer">
        <p>
          Built with React, ASP.NET Core, Azure AI Search and SQL Server.{' '}
          <a href="https://github.com/5rt/rag-doc-qa">Source on GitHub</a>
        </p>
      </footer>
    </>
  );
}
