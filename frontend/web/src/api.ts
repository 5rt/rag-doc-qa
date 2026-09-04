import type { AskResponse, DocumentSummary, UploadResponse } from './types';

// Empty base means "same origin", so the Vite dev proxy handles /api in dev.
const BASE = import.meta.env.VITE_API_URL ?? '';

async function handle<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.error ?? `Request failed (${response.status})`);
  }
  return response.json() as Promise<T>;
}

export async function uploadDocument(file: File, signal?: AbortSignal) {
  const form = new FormData();
  form.append('file', file);
  const response = await fetch(`${BASE}/api/documents`, {
    method: 'POST',
    body: form,
    signal,
  });
  return handle<UploadResponse>(response);
}

export async function askQuestion(
  question: string,
  documentId?: string,
  signal?: AbortSignal,
) {
  const response = await fetch(`${BASE}/api/ask`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ question, documentId }),
    signal,
  });
  return handle<AskResponse>(response);
}

export async function listDocuments(signal?: AbortSignal) {
  const response = await fetch(`${BASE}/api/documents`, { signal });
  return handle<DocumentSummary[]>(response);
}