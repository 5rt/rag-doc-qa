const STORAGE_KEY = 'ragDocQa.apiKey';

// Wrapped in try/catch: localStorage throws in private browsing on some
// browsers, and a missing key should just mean "show the gate again", not
// crash the app.

export function getApiKey(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setApiKey(key: string) {
  try {
    localStorage.setItem(STORAGE_KEY, key);
  } catch {
    /* ignored */
  }
}

export function clearApiKey() {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    /* ignored */
  }
}
