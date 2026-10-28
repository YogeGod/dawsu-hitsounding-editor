"use strict";
const fs=require('fs'),path=require('path'),{randomUUID}=require('crypto');
// Stage alongside the destination; an unsuccessful write must leave the old file intact.
function writeFile(file,bytes){const temporary=path.join(path.dirname(file),'.'+path.basename(file)+'.'+randomUUID()+'.tmp');try{fs.writeFileSync(temporary,bytes,{flag:'wx'});fs.renameSync(temporary,file);}finally{if(fs.existsSync(temporary))fs.unlinkSync(temporary);}}
module.exports={writeFile};
