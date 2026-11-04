import { randomBytes } from 'node:crypto';
import { writeFile,readFile,appendFile,chmod } from 'node:fs/promises';
import { localConfig } from './coffa-env.mjs';
const file=new URL('../.env.coffa.local',import.meta.url);
try{await readFile(file);}catch(error){if(error.code!=='ENOENT')throw error;await writeFile(file,`POSTGRES_DB=coffa_os_dev\nPOSTGRES_USER=coffa_os\nPOSTGRES_PASSWORD=${randomBytes(32).toString('hex')}\nCOFFA_POSTGRES_PORT=55439\nCOFFA_API_URL=http://127.0.0.1:5100\n`,{mode:0o600,flag:'wx'});}
let values=await localConfig();
if(!values.COFFA_CONTACT_KEY){await appendFile(file,`COFFA_CONTACT_KEY=${randomBytes(32).toString('hex')}\n`);values=await localConfig();}
const webFile=new URL('../.env.local',import.meta.url);
let web='';try{web=await readFile(webFile,'utf8');}catch(error){if(error.code!=='ENOENT')throw error;}
const additions=Object.entries({COFFA_API_URL:values.COFFA_API_URL,COFFA_CONTACT_KEY:values.COFFA_CONTACT_KEY}).filter(([key])=>!new RegExp(`^${key}=`, 'm').test(web)).map(([key,value])=>`${key}=${value}`).join('\n');
if(additions)await appendFile(webFile,`${web&&!web.endsWith('\n')?'\n':''}${additions}\n`,{mode:0o600});
await chmod(file,0o600);await chmod(webFile,0o600);
console.log('Lokale Coffa-configuratie staat klaar. Bestaande waarden zijn behouden; secrets zijn niet weergegeven.');
