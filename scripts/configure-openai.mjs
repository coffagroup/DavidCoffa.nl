import { readFile, writeFile, chmod } from 'node:fs/promises';

if (!process.stdin.isTTY) throw new Error('Start npm run coffa:ai interactief in je eigen terminal.');
const target = new URL('../.env.coffa.local', import.meta.url);
let existing;
try { existing = await readFile(target, 'utf8'); } catch { throw new Error('Voer eerst npm run coffa:setup uit.'); }
process.stdout.write('OpenAI API-sleutel (invoer is verborgen; Ctrl+C annuleert): ');
const key = await new Promise(resolve => {
  let value = '';
  const cleanup = () => { process.stdin.off('data', receive); process.stdin.setRawMode(false); process.stdin.pause(); };
  function receive(chunk) {
    for (const character of chunk.toString()) {
      if (character === '\u0003') { cleanup(); process.stdout.write('\nGeannuleerd.\n'); process.exit(130); }
      if (character === '\r' || character === '\n') { cleanup(); process.stdout.write('\n'); resolve(value.trim()); return; }
      if (character === '\u007f' || character === '\b') value = value.slice(0, -1);
      else if (character >= ' ') value += character;
    }
  }
  process.stdin.setRawMode(true); process.stdin.resume(); process.stdin.on('data', receive);
});
if (!/^sk-[A-Za-z0-9_-]{16,}$/.test(key)) throw new Error('Dit lijkt geen geldige OpenAI API-sleutel. Er is niets gewijzigd.');
const lines = existing.split(/\r?\n/).filter(line => !/^(OPENAI_API_KEY|OpenAI__ApiKey)=/.test(line));
while (lines.at(-1) === '') lines.pop();
lines.push(`OPENAI_API_KEY=${key}`, '');
await writeFile(target, lines.join('\n'), { mode: 0o600 });
await chmod(target, 0o600);
console.log('De sleutel is lokaal opgeslagen in het genegeerde bestand .env.coffa.local.');
console.log('Start de API opnieuw met npm run api:dev. De sleutel is niet in de browser beschikbaar.');
