import { Link, NavLink, Outlet } from 'react-router-dom';

export default function Layout() {
  return (
    <>
      <a href="#main" className="skip-link">Skip to content</a>
      <header className="site-header">
        <Link to="/" className="site-title">Document Q&amp;A</Link>
        <nav aria-label="Main">
          <NavLink to="/upload">Upload</NavLink>
          <NavLink to="/ask">Ask</NavLink>
          <NavLink to="/how-it-works">How it works</NavLink>
        </nav>
      </header>
      <main id="main"><Outlet /></main>
      <footer className="site-footer">
        <p>
          Built with React, ASP.NET Core, Azure AI Search and SQL Server.{' '}
          <a href="https://github.com/5rt/rag-doc-qa">Source on GitHub</a>
        </p>
      </footer>
    </>
  );
}
