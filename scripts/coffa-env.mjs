import { readFile } from 'node:fs/promises';
export async function localConfig() {
  const text = await readFile(new URL('../.env.coffa.local', import.meta.url), 'utf8');
  return Object.fromEntries(text.split(/\r?\n/).filter(line => line.trim() && !line.startsWith('#')).map(line => { const at=line.indexOf('='); return [line.slice(0,at),line.slice(at+1)]; }));
}
export function apiEnvironment(values) {
  return Object.fromEntries(Object.entries({
    ...process.env, ASPNETCORE_ENVIRONMENT:'Development', ASPNETCORE_URLS:'http://127.0.0.1:5100',
    ConnectionStrings__Default:`Host=127.0.0.1;Port=${values.COFFA_POSTGRES_PORT??55439};Database=${values.POSTGRES_DB};Username=${values.POSTGRES_USER};Password=${values.POSTGRES_PASSWORD}`,
    Public__PortalUrl:'http://localhost:3000/portaal', Contact__ApiKey:values.COFFA_CONTACT_KEY,
    Email__EnableSending:'false', Tickets__EmailNotificationsEnabled:'false',
    OPENAI_API_KEY:process.env.OPENAI_API_KEY??values.OPENAI_API_KEY,
    OpenAI__ApiKey:process.env.OpenAI__ApiKey??values.OpenAI__ApiKey,
    OpenAI__PlanningModel:process.env.OpenAI__PlanningModel??values.OpenAI__PlanningModel,
    OpenAI__TranscriptionModel:process.env.OpenAI__TranscriptionModel??values.OpenAI__TranscriptionModel,
  }).filter(([,value])=>value!==undefined));
}
