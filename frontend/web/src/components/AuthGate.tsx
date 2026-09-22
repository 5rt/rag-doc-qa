import { useState, type ReactNode, type SubmitEvent } from 'react';
import { getApiKey, setApiKey } from '../apiKey';

// Empty base means "same origin", matching api.ts.
const BASE = import.meta.env.VITE_API_URL ?? '';

/**
 * Blocks the whole app behind the shared access key. Verifies the key with a
 * direct fetch rather than api.ts's listDocuments() - that helper reloads the
 * page on a 401, which would wipe this form's error message before the user
 * could read it.
 */
export default function AuthGate({ children }: { children: ReactNode }) {
  const [authed, setAuthed] = useState(() => !!getApiKey());
  const [input, setInput] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);

  async function handleSubmit(e: SubmitEvent) {
    e.preventDefault();
    const key = input.trim();
    if (!key) return;

    setChecking(true);
    setError(null);
    try {
      const response = await fetch(`${BASE}/api/documents`, {
        headers: { 'X-Api-Key': key },
      });
      if (response.status === 401) {
        setError('That key was not accepted.');
        return;
      }
      // 503 means the database is waking up. The key check runs before the
      // controller, so reaching it at all means the key was accepted.
      if (!response.ok && response.status !== 503) {
        setError(`Could not verify the key (${response.status}). Try again.`);
        return;
      }
      setApiKey(key);
      setAuthed(true);
    } catch {
      setError('Could not reach the server. Try again.');
    } finally {
      setChecking(false);
    }
  }

  if (authed) return <>{children}</>;

  return (
    <div className="auth-gate">
      <h1>Document Q&amp;A</h1>
      <p>This is a shared demo behind an access key. Enter it to continue.</p>
      <form className="upload-form" onSubmit={handleSubmit}>
        <label htmlFor="access-key">Access key</label>
        <input
          id="access-key"
          type="password"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          disabled={checking}
          autoFocus
        />
        <button type="submit" disabled={!input.trim() || checking}>
          {checking ? 'Checking…' : 'Continue'}
        </button>
      </form>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
    </div>
  );
}
