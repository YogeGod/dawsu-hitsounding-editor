"use strict";
// Flatten only the production dependency closure: no pnpm store or build tools.
const fs=require('fs'),path=require('path'),{createRequire}=require('module');
const [source,stage,asarModule,destination]=process.argv.slice(2);
const seen=new Map();
function dependency(name,from){
 const file=createRequire(path.join(from,'package.json')).resolve(name+'/package.json');
 const folder=path.dirname(fs.realpathSync(file)),pkg=JSON.parse(fs.readFileSync(file,'utf8'));
 if(seen.has(name)){if(seen.get(name)!==pkg.version)throw Error('Conflicting production dependency: '+name);return;}
 seen.set(name,pkg.version);
 fs.cpSync(folder,path.join(stage,'node_modules',name),{recursive:true,dereference:true,filter:p=>!['node_modules','.git','.github','test','tests','examples','coverage'].includes(path.basename(p))&&!p.endsWith('.map')});
 for(const child of Object.keys(pkg.dependencies||{}))dependency(child,folder);
}
(async()=>{
 for(const name of Object.keys(JSON.parse(fs.readFileSync(path.join(source,'package.json'),'utf8')).dependencies||{}))dependency(name,source);
 await require(asarModule).createPackage(stage,destination);
 console.log(JSON.stringify({dependencies:Object.fromEntries(seen),asarBytes:fs.statSync(destination).size}));
})().catch(e=>{console.error(e);process.exitCode=1;});
