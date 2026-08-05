#!/usr/bin/env node
// Secret scan over git-TRACKED files only (whatever is or would be committed).
// Zero dependencies; run locally (`node scripts/secret-scan.mjs`) and in CI.
// Exit 0 = clean, exit 1 = findings (values are never printed, only file:line + category).
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const repoRoot = execFileSync('git', ['rev-parse', '--show-toplevel']).toString().trim();
const files = execFileSync('git', ['ls-files'], { cwd: repoRoot }).toString().split('\n').filter(Boolean);

// Binary-ish or hash-carrying files that legitimately contain long random strings.
const SKIP = [
  /package-lock\.json$/,
  /\.(png|jpg|jpeg|gif|ico|webp|woff2?|ttf|eot|pdf|zip)$/i,
  /^scripts\/secret-scan\.mjs$/, // this file names the patterns it hunts
];

// Known deliberate dummy values (test fixtures) — never real credentials.
const ALLOW = [
  'test-signing-key-that-is-at-least-32-bytes!!',
  '9f3c1a7e-b28d-4c60-a1f2-7e5d9b04c8e1-Qk7pZ',
  'Admin123!', // documented development-only seed login
  'Test123!x', // documented development-only seed login
  '<SET_VIA_USER_SECRETS>',
  '<SET_VIA_ENVIRONMENT_VARIABLE>',
  '${POSTGRES_PASSWORD:-postgres}',
];

// A captured "value" that is a plain lowercase identifier (permission codes such as
// users.reset_password, claim names, css classes) is not a credential. Real keys are
// high-entropy/mixed case. Values containing spaces are UI copy, never secrets.
const looksLikeIdentifier = v => /^[a-z0-9_.:\/-]+$/.test(v);

const RULES = [
  { name: 'private-key-block', re: /-----BEGIN (RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----/ },
  { name: 'aws-access-key', re: /\bAKIA[0-9A-Z]{16}\b/ },
  { name: 'github-token', re: /\bgh[pousr]_[A-Za-z0-9]{30,}\b/ },
  {
    // Config/doc files only: source code hits are ordinary identifiers (bool flags, params).
    name: 'connection-string-password',
    re: /(Password|Pwd)\s*=\s*([^;"'\s<$:][^;"'\s]{3,})/i,
    onlyExt: /\.(json|ya?ml|env|txt|config|xml|sql|md)$/i,
    valueGroup: 2,
  },
  {
    name: 'assigned-secret-literal',
    // password/secret/key/token assigned a >=12-char quoted literal.
    re: /(?:password|passwd|secret|apikey|api_key|signingkey|accesstoken|auth_token|encryption[_:]?key)["']?\s*[:=]\s*["']([^"']{12,})["']/i,
    valueGroup: 1,
    // Test fixtures use obvious dummy passwords; the test project is excluded for this
    // heuristic rule only (the exact-match rules above still cover it).
    skipPath: /^TransportationService\.Api\.Tests\//,
  },
  {
    name: 'long-base64-in-config',
    re: /["':= ]([A-Za-z0-9+/]{40,}={0,2})["' ]?/,
    onlyExt: /\.(json|ya?ml|env|txt|config|xml)$/i,
    valueGroup: 1,
  },
];

let findings = 0;
for (const file of files) {
  if (SKIP.some(re => re.test(file))) continue;
  let text;
  try { text = readFileSync(`${repoRoot}/${file}`, 'utf8'); } catch { continue; }
  const lines = text.split('\n');
  lines.forEach((line, i) => {
    if (ALLOW.some(v => line.includes(v))) return;
    for (const rule of RULES) {
      if (rule.onlyExt && !rule.onlyExt.test(file)) continue;
      if (rule.skipPath && rule.skipPath.test(file)) continue;
      const match = rule.re.exec(line);
      if (!match) continue;
      const value = rule.valueGroup ? match[rule.valueGroup] : null;
      if (value && (value.includes(' ') || looksLikeIdentifier(value))) continue;
      findings++;
      console.error(`SECRET-SCAN [${rule.name}] ${file}:${i + 1} (value not shown)`);
    }
  });
}

if (findings > 0) {
  console.error(`\nsecret-scan: ${findings} potential secret(s) in tracked files — commit blocked. `
    + 'Move real values to user-secrets/environment variables, or extend the ALLOW list for provably fake fixtures.');
  process.exit(1);
}
console.log(`secret-scan: clean (${files.length} tracked files).`);
