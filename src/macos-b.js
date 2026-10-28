'use strict';
for(const [id,action] of [['windowClose','close'],['windowMinimize','minimize'],['windowMaximize','maximize']]){
 document.getElementById(id).onclick=async()=>{try{await desktop.windowAction(action);}catch(error){status(error.message);}};
}
const documentLabel=document.getElementById('macDocumentTitle'),projectLabel=document.getElementById('projectName');
const syncDocumentTitle=()=>{documentLabel.textContent=projectLabel.textContent||'Untitled';};
new MutationObserver(syncDocumentTitle).observe(projectLabel,{childList:true,characterData:true,subtree:true});syncDocumentTitle();
