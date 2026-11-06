import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import ts from 'typescript';
const source = readFileSync(new URL('../src/lib/portal/financial-period.ts', import.meta.url), 'utf8');
const { financialPeriod } = await import(`data:text/javascript;base64,${Buffer.from(ts.transpile(source, { module: ts.ModuleKind.ESNext })).toString('base64')}`);
for (const [instant, from, to] of [
  ['2026-11-06T12:00:00Z', '2026-10-20', '2026-11-19'],
  ['2026-11-19T22:59:59Z', '2026-10-20', '2026-11-19'],
  ['2026-11-19T23:00:00Z', '2026-11-20', '2026-12-19'],
  ['2027-01-01T00:00:00Z', '2026-12-20', '2027-01-19'],
  ...['2026-03-29T00:59:59Z', '2026-03-29T01:00:00Z'].map(d => [d, '2026-03-20', '2026-04-19']),
  ...['2026-10-25T00:59:59Z', '2026-10-25T01:00:00Z'].map(d => [d, '2026-10-20', '2026-11-19']),
]) test(instant, () => assert.deepEqual(financialPeriod(new Date(instant)), { from, to, financialMonth: true }));
