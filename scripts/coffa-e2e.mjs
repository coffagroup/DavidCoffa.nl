// Real HTTP smoke test: Next BFF + .NET Identity + an isolated PostgreSQL schema.
// All accounts, documents and records are test data; the schema is removed afterwards.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes,randomUUID,createHmac } from 'node:crypto';
import { createServer } from 'node:net';
import { mkdir,writeFile,readFile,readdir,rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import pg from 'pg';
import { localConfig } from './coffa-env.mjs';
const root=fileURLToPath(new URL('../',import.meta.url));
const values=await localConfig();
const schema=`e2e_${randomUUID().replaceAll('-','')}`;
const temporary=path.join(root,'.data',schema);
await mkdir(temporary,{recursive:true,mode:0o700});
const db=new pg.Client({host:'127.0.0.1',port:Number(values.COFFA_POSTGRES_PORT??55439),database:values.POSTGRES_DB,user:values.POSTGRES_USER,password:values.POSTGRES_PASSWORD});
await db.connect();
await db.query(`CREATE SCHEMA "${schema}"`);
const processes=[];
let apiLog='',webLog='';
async function freePort(){return await new Promise((resolve,reject)=>{const server=createServer();server.on('error',reject);server.listen(0,'127.0.0.1',()=>{const port=server.address().port;server.close(()=>resolve(port));});});}
async function ready(url,child){for(let i=0;i<150;i++){if(child.exitCode!==null)throw new Error('Testserver stopte onverwacht; bekijk de private testlogs.');try{const response=await fetch(url);if(response.status<500)return;}catch{}await new Promise(resolve=>setTimeout(resolve,300));}throw new Error('Testserver startte niet op tijd.');}
function launch(executable,args,options,collect){const child=spawn(executable,args,{stdio:['ignore','pipe','pipe'],...options});processes.push(child);child.stdout.on('data',collect);child.stderr.on('data',collect);return child;}
function totp(secret){const alphabet='ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';let bits=0,value=0;const bytes=[];for(const character of secret.toUpperCase().replaceAll('=','')){value=(value<<5)|alphabet.indexOf(character);bits+=5;if(bits>=8){bytes.push((value>>(bits-8))&255);bits-=8;}}const counter=Buffer.alloc(8);counter.writeBigUInt64BE(BigInt(Math.floor(Date.now()/30000)));const hash=createHmac('sha1',Buffer.from(bytes)).update(counter).digest();const at=hash[hash.length-1]&15;return String((hash.readUInt32BE(at)&0x7fffffff)%1000000).padStart(6,'0');}
let webOrigin;
class Session{
 cookies=new Map();
 async raw(route,{method='GET',body,csrf}={}){const headers=new Headers({Origin:webOrigin});if(this.cookies.size)headers.set('Cookie',[...this.cookies].map(([key,value])=>`${key}=${value}`).join('; '));if(csrf)headers.set('X-CSRF-TOKEN',csrf);const form=body instanceof FormData;if(body!==undefined&&!form)headers.set('Content-Type','application/json');const response=await fetch(webOrigin+route,{method,headers,body:body===undefined?undefined:form?body:JSON.stringify(body),redirect:'manual'});for(const cookie of response.headers.getSetCookie()){const first=cookie.split(';')[0],at=first.indexOf('='),key=first.slice(0,at),value=first.slice(at+1);if(value)this.cookies.set(key,value);else this.cookies.delete(key);}return response;}
 async call(route,method='GET',body,expected=200){let csrf;if(method!=='GET'){const response=await this.raw('/api/os/auth/csrf');assert.equal(response.status,200,'CSRF endpoint');csrf=(await response.json()).token;}const response=await this.raw('/api/os/'+route,{method,body,csrf});assert.equal(response.status,expected,`HTTP-status voor ${route.startsWith('auth/invitations/')?'auth/invitations/[token]':route}`);if(response.status===204)return null;const type=response.headers.get('content-type')??'';return type.includes('json')?await response.json():response;}
}
try{
 const apiPort=await freePort(),webPort=await freePort();webOrigin=`http://127.0.0.1:${webPort}`;
 const email=`admin-${randomUUID()}@example.com`,password=process.env.COFFA_E2E_INSPECT_PASSWORD??(randomBytes(28).toString('base64url')+'Aa1!'),key=randomBytes(32).toString('hex');
 const connection=`Host=127.0.0.1;Port=${values.COFFA_POSTGRES_PORT??55439};Database=${values.POSTGRES_DB};Username=${values.POSTGRES_USER};Password=${values.POSTGRES_PASSWORD};Search Path=${schema}`;
 const backend=launch('dotnet',[path.join(root,'apps/api/Coffa.Api/bin/Debug/net10.0/Coffa.Api.dll')],{cwd:path.join(root,'apps/api/Coffa.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Testing',ASPNETCORE_URLS:`http://127.0.0.1:${apiPort}`,ConnectionStrings__Default:connection,Database__AutoMigrate:'true',Bootstrap__Email:email,Bootstrap__Password:password,Bootstrap__Name:'E2E beheerder',Public__PortalUrl:webOrigin+'/portaal',Contact__ApiKey:key,OPENAI_API_KEY:'',OpenAI__ApiKey:'',Email__EnableSending:'false',Email__OutboxPath:path.join(temporary,'outbox'),Storage__DevelopmentDirectory:path.join(temporary,'objects'),Tickets__EmailNotificationsEnabled:'false',Logging__LogLevel__Default:'Warning'}},chunk=>{apiLog+=chunk;});
 await ready(`http://127.0.0.1:${apiPort}/health`,backend);
 const web=launch(process.execPath,[path.join(root,'node_modules/next/dist/bin/next'),'start','--hostname','127.0.0.1','--port',String(webPort)],{cwd:root,env:{...process.env,NODE_ENV:'production',COFFA_API_URL:`http://127.0.0.1:${apiPort}`,COFFA_CONTACT_KEY:key}},chunk=>{webLog+=chunk;});
 await ready(webOrigin+'/portaal',web);
 const admin=new Session();
 let me=await admin.call('auth/login','POST',{email,password});assert.equal(me.requiresTwoFactorSetup,true);
 await admin.call('organizations','GET',undefined,403);
 const setup=await admin.call('auth/2fa/setup');
 const secured=await admin.call('auth/2fa/enable','POST',{code:totp(setup.sharedKey)});assert.equal(secured.user.requiresTwoFactorSetup,false);assert.equal(secured.recoveryCodes.length,10);
 const assistant=await admin.call("assistant/status");assert.equal(assistant.configured,false);await admin.call("assistant/plan","POST",{text:"Maak een factuur"},503);
 await admin.call('settings','PUT',{companyName:'E2E testbedrijf',email:'test@example.com',address:'Teststraat 1, 1234 AB Voorburg',chamberOfCommerce:'12345678',vatNumber:'NL000000000B01',iban:'NL91ABNA0417164300',paymentInstructions:'Betaling volgens afspraak',invoicePrefix:'E2E',quotePrefix:'OFFE2E',paymentTermDays:14,automaticEmailEnabled:false});
 const organization=await admin.call('organizations','POST',{name:'E2E klant A',email:'customer@example.com',status:'active',type:'customer',tags:[],internalNotes:'Alleen intern'} ,201);
 const other=await admin.call('organizations','POST',{name:'E2E klant B',status:'active',type:'customer',tags:[]},201);
 const project=await admin.call('projects','POST',{organizationId:organization.id,name:'E2E klantportaal',description:'Project via de echte BFF',status:'development',priority:'normal',sharedWithCustomer:true,budget:1000,estimatedHours:8},201);
 const customerEmail=`customer-${randomUUID()}@example.com`;
 const invite=await admin.call('invitations','POST',{email:customerEmail,organizationId:organization.id,role:'Customer',permissions:[]});assert.equal(invite.delivery.sent,false);
 const outbox=JSON.parse(await readFile(path.join(temporary,'outbox',invite.delivery.outboxId+'.json'),'utf8'));
 const token=decodeURIComponent(outbox.Message.HtmlBody.match(/token=([^"&<>]+)/)[1]);
 const customer=new Session();await customer.call(`auth/invitations/${encodeURIComponent(token)}`);
 await customer.call('auth/accept-invitation','POST',{token,name:'E2E klantgebruiker',password},200);
 me=await customer.call('auth/login','POST',{email:customerEmail,password});assert.equal(me.organizationId,organization.id);
 await customer.call(`organizations/${other.id}`,'GET',undefined,404);await customer.call('finance','GET',undefined,403);await customer.call('assistant/status','GET',undefined,403);
 const visibleProject=await customer.call(`projects/${project.id}`);assert.equal(visibleProject.project.name,'E2E klantportaal');assert.equal('budget' in visibleProject.project,false);
 const ticket=await customer.call('tickets','POST',{organizationId:organization.id,projectId:project.id,title:'E2E supportvraag',description:'De knop kan duidelijker worden.',category:'general',priority:'normal'},201);
 await admin.call(`tickets/${ticket.id}/comments`,'POST',{body:'Interne prijsafspraak',isInternal:true},201);
 await admin.call(`tickets/${ticket.id}/comments`,'POST',{body:'Ik heb je vraag ontvangen.',isInternal:false},201);
 const conversation=await customer.call(`tickets/${ticket.id}`);assert.ok(conversation.comments.some(item=>item.body==='Ik heb je vraag ontvangen.'));assert.ok(!JSON.stringify(conversation).includes('Interne prijsafspraak'));
 const attachment=new FormData();attachment.set('organizationId',organization.id);attachment.set('ticketId',ticket.id);attachment.set('category','Support');attachment.set('file',new Blob(['E2E privébijlage'],{type:'text/plain'}),'bijlage.txt');
 const document=await customer.call('documents','POST',attachment,201);const downloaded=await customer.call(`documents/${document.id}/download`);assert.equal(await downloaded.text(),'E2E privébijlage');
 const invoice=await admin.call('invoices','POST',{organizationId:organization.id,projectId:project.id,title:'E2E factuur',invoiceDate:'2026-11-04',dueDate:'2026-11-18',sharedWithCustomer:true,lines:[{description:'Werkzaamheden',quantity:2,unitPrice:100,taxRate:21,discountPercent:0}]});assert.equal(invoice.totalAmount,242);
 const issued=await admin.call(`invoices/${invoice.id}/issue`,'POST',{});assert.ok(issued.number);
 await admin.call(`invoices/${invoice.id}/payments`,'POST',{amount:100,date:'2026-11-04',reference:'E2E deelbetaling',idempotencyKey:randomUUID()});
 const paid=await admin.call(`invoices/${invoice.id}`);assert.equal(paid.outstandingAmount,142);
 await admin.call(`invoices/${invoice.id}`,'PUT',{organizationId:organization.id,title:'Ongeoorloofde wijziging',invoiceDate:'2026-11-04',dueDate:'2026-11-18',lines:[{description:'Andere regel',quantity:1,unitPrice:10,taxRate:21}]},409);
 const pdf=await customer.call(`invoices/${invoice.id}/pdf`);const bytes=Buffer.from(await pdf.arrayBuffer());assert.equal(bytes.subarray(0,5).toString(),'%PDF-');await writeFile(path.join(root,'.data','coffa-last-test-invoice.pdf'),bytes,{mode:0o600});
 const contactId=randomUUID();const contact={submissionId:contactId,name:'E2E websiteaanvraag',email:'lead@example.com',subject:'Contactbridge',message:'Deze aanvraag moet in de CRM-pipeline verschijnen.',website:''};
 for(let i=0;i<2;i++){const response=await admin.raw('/api/contact',{method:'POST',body:contact});assert.equal(response.status,200,'Publiek contactformulier -> CRM');assert.equal((await response.json()).ok,true);}
 const leads=await admin.call('leads?q=E2E%20websiteaanvraag');assert.equal(leads.items.filter(item=>item.id===contactId).length,1);
 const dashboard=await admin.call('dashboard?from=2026-11-01&to=2026-11-30');assert.ok(dashboard.kpis.activeCustomers>=2);assert.ok(dashboard.kpis.outstandingInvoices>=142);
 for(const route of ['/','/diensten','/contact','/portaal','/portaal/projecten','/robots.txt'])assert.equal((await fetch(webOrigin+route)).status,200,route);
 await admin.call('auth/logout','POST',{},204);await admin.call('auth/me','GET',undefined,401);
 if(process.argv.includes('--inspect')){console.log(JSON.stringify({inspectUrl:webOrigin+'/portaal',customerEmail}));await new Promise(resolve=>{process.once('SIGINT',resolve);process.once('SIGTERM',resolve);});}
 console.log('Coffa OS HTTP-e2e geslaagd: BFF-cookies/CSRF, verplichte 2FA, klantuitnodiging, tenantafscherming, tickets/interne reacties, privébijlage, factuur/PDF/deelbetaling, contact->CRM en uitloggen.');
}finally{
 for(const child of processes)if(child.exitCode===null)child.kill('SIGTERM');
 await Promise.all(processes.map(child=>child.exitCode!==null?Promise.resolve():Promise.race([new Promise(resolve=>child.once('exit',resolve)),new Promise(resolve=>setTimeout(resolve,3000))])));
 for(const child of processes)if(child.exitCode===null)child.kill('SIGKILL');
 await writeFile(path.join(root,'.data','coffa-e2e-api.log'),apiLog,{mode:0o600});await writeFile(path.join(root,'.data','coffa-e2e-web.log'),webLog,{mode:0o600});
 await db.query(`DROP SCHEMA "${schema}" CASCADE`);await db.end();await rm(temporary,{recursive:true,force:true});
}
