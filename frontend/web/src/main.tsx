import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import './index.css';
import Layout from './components/Layout';
import AskPage from './pages/AskPage';
import HomePage from './pages/HomePage';
import HowItWorksPage from './pages/HowItWorksPage';
import NotFound from './pages/NotFound';
import UploadPage from './pages/UploadPage';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<HomePage />} />
          <Route path="/upload" element={<UploadPage />} />
          <Route path="/ask" element={<AskPage />} />
          <Route path="/how-it-works" element={<HowItWorksPage />} />
        </Route>
        <Route path="*" element={<NotFound />} />
      </Routes>
    </BrowserRouter>
  </StrictMode>,
);
