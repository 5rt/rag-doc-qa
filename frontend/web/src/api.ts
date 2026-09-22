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

/**
 * The first call after an idle hour wakes both the free-tier App Service and
 * the serverless database. That call can hang for a minute and then come back
 * 503 once the database connection times out; by then the database is usually
 * awake. So: tell the caller as soon as the call is slow, and retry 503s.
 * Only this read retries - an upload retry would spend embedding quota again.
 */
export async function listDocuments(onSlow?: () => void) {
  const timer = setTimeout(() => onSlow?.(), 3000);
  try {
    for (let attempt = 1; ; attempt++) {
      const response = await fetch(`${BASE}/api/documents`, { headers: authHeaders() });
      if (response.status !== 503 || attempt === 3) {
        return await handle<DocumentSummary[]>(response);
      }
      onSlow?.();
    }
  } finally {
    clearTimeout(timer);
  }
}
export async function deleteDocument(id: string) {
  const response = await fetch(`${BASE}/api/documents/${id}`, {
    method: 'DELETE',
    headers: authHeaders(),
  });
  return handle<{ chunksDeleted: number }>(response);
}
