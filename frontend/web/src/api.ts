import { clearApiKey, getApiKey } from './apiKey';
import type { AskResponse, DocumentSummary, UploadResponse } from './types';

// Empty base means "same origin", so the Vite dev proxy handles /api in dev.
const BASE = import.meta.env.VITE_API_URL ?? '';

function authHeaders(extra?: Record<string, string>): HeadersInit {
  const key = getApiKey();
  return { ...extra, ...(key ? { 'X-Api-Key': key } : {}) };
}

async function handle<T>(response: Response): Promise<T> {
  if (response.status === 401) {
    // The key was never set, is wrong, or was rotated server-side. Clear it
    // and reload so AuthGate asks again instead of every page showing the
    // same opaque "Unauthorized" error.
    clearApiKey();
    window.location.reload();
    throw new Error('Access key is invalid. Reloading...');
  }
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
    headers: authHeaders(),
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
    headers: authHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify({ question, documentId }),
    signal,
  });
  return handle<AskResponse>(response);
}

export async function listDocuments(signal?: AbortSignal) {
  const response = await fetch(`${BASE}/api/documents`, { headers: authHeaders(), signal });
  return handle<DocumentSummary[]>(response);
}
export async function deleteDocument(id: string) {
  const response = await fetch(`${BASE}/api/documents/${id}`, {
    method: 'DELETE',
    headers: authHeaders(),
  });
  return handle<{ chunksDeleted: number }>(response);
}
