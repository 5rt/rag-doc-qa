// Retrieval and grounding eval against a running API.
//
//   API_KEY=... node eval/run.mjs                   # live API
//   API_URL=https://localhost:7212 API_KEY=... node eval/run.mjs
//
// Uploads handbook.txt, asks every question scoped to it, then deletes it.
// Costs ~8 embedding calls for the upload plus 2 Gemini calls per question.

import { readFile } from 'node:fs/promises';

const API = process.env.API_URL ?? 'https://ragdocqa-api.azurewebsites.net';
const KEY = process.env.API_KEY;
if (!KEY) throw new Error('Set API_KEY.');

// `needle` is text that only the passage holding the answer contains.
// `expect` must appear in the answer. Questions are paraphrased on purpose,
// so retrieval has to match meaning rather than copied words.
const QUESTIONS = [
  { q: 'How many days off a year do staff get?', needle: '25 days of annual leave', expect: /25/ },
  { q: 'How much unused holiday can roll into next year?', needle: 'carry over up to five days', expect: /five|5/i },
  { q: 'How long do I have to claim back money I spent?', needle: 'within 30 days', expect: /30/ },
  { q: 'Who signs off on a purchase that costs more than $500?', needle: 'above $500 needs approval', expect: /team lead/i },
  { q: 'What am I paid for driving my own car to a client?', needle: '99 cents per kilometre', expect: /99/ },
  { q: 'How often do I get a new laptop?', needle: 'replaced every three years', expect: /three|3/i },
  { q: 'How much can I spend setting up my desk at home?', needle: '$750', expect: /750/ },
  { q: 'Where do we gather after evacuating the Nelson building?', needle: 'Anzac Park', expect: /Anzac/i },
  { q: 'How long is the trial period for new hires?', needle: 'probation period of 90 days', expect: /90/ },
  { q: 'What is the yearly budget for courses and conferences?', needle: '$1,800', expect: /1,?800/ },
  { q: 'Which hours do I have to be online?', needle: '10:00 and 15:00', expect: /10/ },
  { q: 'How many days a week can I work from home?', needle: 'up to three days a week', expect: /three|3/i },
  { q: 'How many people work in the Wellington office?', needle: 'has 11 people', expect: /11/ },
  { q: 'What should I do if my work phone goes missing?', needle: 'within one hour', expect: /hour/i },
  { q: 'Who do I book flights through?', needle: 'Ember Travel', expect: /Ember/i },
];

// Not in the handbook: the right answer is the refusal.
const UNANSWERABLE = [
  'What is the dress code for client meetings?',
  'Does the studio match KiwiSaver contributions above the minimum?',
  'What is the office Wi-Fi password?',
];
const REFUSAL = /not covered in the uploaded documents/i;

// The API allows 8 asks per minute per IP.
const ASK_GAP_MS = 8000;

const headers = { 'X-Api-Key': KEY };
const norm = (s) => s.replace(/\s+/g, ' ');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(path, init) {
  const res = await fetch(`${API}${path}`, { ...init, headers: { ...headers, ...init?.headers } });
  const body = await res.json().catch(() => null);
  if (!res.ok) throw new Error(`${init?.method ?? 'GET'} ${path}: ${res.status} ${body?.error ?? ''}`);
  return body;
}

async function ask(question, documentId, retried = false) {
  await sleep(ASK_GAP_MS);
  try {
    return await call('/api/ask', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question, documentId }),
    });
  } catch (err) {
    // 502 is Gemini being briefly overloaded (it returns 503 upstream).
    if (retried || !/: 502 /.test(err.message)) throw err;
    console.log('\t(Gemini busy, retrying in 15 s)');
    await sleep(15000);
    return ask(question, documentId, true);
  }
}

const text = await readFile(new URL('./handbook.txt', import.meta.url));
const form = new FormData();
form.append('file', new Blob([text], { type: 'text/plain' }), 'eval-handbook.txt');
const { documentId, chunkCount } = await call('/api/documents', { method: 'POST', body: form });
console.log(`Uploaded handbook: ${chunkCount} passages\n`);

try {
  let hit1 = 0, hit5 = 0, rr = 0, answered = 0, refused = 0;

  for (const { q, needle, expect } of QUESTIONS) {
    const { answer, sources } = await ask(q, documentId);
    const rank = sources.findIndex((s) => norm(s.content).includes(needle)) + 1;
    const ok = expect.test(answer);
    if (rank === 1) hit1++;
    if (rank > 0) { hit5++; rr += 1 / rank; }
    if (ok) answered++;
    console.log(`${rank ? `#${rank}` : 'miss'}\t${ok ? 'ok ' : 'BAD'}\t${q}${ok ? '' : `\n\t\t-> ${answer}`}`);
  }

  for (const q of UNANSWERABLE) {
    const { answer } = await ask(q, documentId);
    const ok = REFUSAL.test(answer);
    if (ok) refused++;
    console.log(`-\t${ok ? 'ok ' : 'BAD'}\t${q}${ok ? '' : `\n\t\t-> ${answer}`}`);
  }

  const n = QUESTIONS.length;
  const pct = (x, d) => `${((100 * x) / d).toFixed(0)}%`;
  console.log(`
Retrieval   hit@1 ${pct(hit1, n)}   hit@5 ${pct(hit5, n)}   MRR ${(rr / n).toFixed(2)}
Answers     ${answered}/${n} contain the expected fact
Refusals    ${refused}/${UNANSWERABLE.length} unanswerable questions refused`);
} finally {
  await call(`/api/documents/${documentId}`, { method: 'DELETE' });
  console.log('\nDeleted the eval document.');
}
