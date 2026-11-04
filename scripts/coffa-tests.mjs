import { spawn } from 'node:child_process';
const child=spawn('dotnet',['test','Coffa.sln','--no-restore','--logger','console;verbosity=minimal'],{stdio:'inherit',env:process.env});
child.on('exit',code=>{process.exitCode=code??1;});
child.on('error',()=>{console.error('De .NET 10 SDK is vereist.');process.exitCode=1;});
