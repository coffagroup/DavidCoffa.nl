import { spawn } from 'node:child_process';
import { localConfig, apiEnvironment } from './coffa-env.mjs';
const values=await localConfig();
const args=['run','--project','apps/api/Coffa.Api'];
if(process.argv.includes('--migrate'))args.push('--','--migrate');
const child=spawn('dotnet',args,{stdio:'inherit',env:apiEnvironment(values)});
child.on('error',()=>{console.error('De .NET 10 SDK kon niet worden gestart.');process.exitCode=1;});
child.on('exit',code=>{process.exitCode=code??1;});
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>child.kill(signal));
