"use strict";
// Inspector arrangement is a local UI preference; sample data and history stay untouched.
(function(){
 const sidebar=document.getElementById('inspector'),cards=[...sidebar.querySelectorAll('.inspectorCard')],byId=new Map(cards.map(card=>[card.dataset.panelId,card])),defaults=[...byId.keys()],storageKey='hse-inspector-panels';
 let drag=null;
 function allowed(){return !(gesture||rowDrag||locatorDrag||bLaneDrag||document.getElementById('dialog').open);}
 function state(){return {order:[...sidebar.querySelectorAll('.inspectorCard')].map(card=>card.dataset.panelId),collapsed:cards.filter(card=>card.querySelector('.inspectorCardBody').hidden).map(card=>card.dataset.panelId)};}
 function save(){try{localStorage.setItem(storageKey,JSON.stringify(state()));}catch{}}
 function collapsed(id,value,persist=true){const card=byId.get(id);if(!card)return;const body=card.querySelector('.inspectorCardBody'),toggle=card.querySelector('.inspectorPanelToggle'),title=card.getAttribute('aria-label');if(value&&body.contains(document.activeElement))toggle.focus({preventScroll:true});body.hidden=!!value;card.classList.toggle('panelCollapsed',!!value);toggle.setAttribute('aria-expanded',String(!value));toggle.title=(value?'Expand ':'Collapse ')+title;card.querySelector('.panelChevron').textContent=value?'▸':'▾';if(persist)save();}
 function apply(saved){const order=[...new Set([...(Array.isArray(saved?.order)?saved.order:[]),...defaults])].filter(id=>byId.has(id)),closed=new Set(Array.isArray(saved?.collapsed)?saved.collapsed:[]),footnote=sidebar.querySelector('.inspectorFootnote');for(const id of order){sidebar.insertBefore(byId.get(id),footnote);collapsed(id,closed.has(id),false);}}
 function restore(){let saved={};try{saved=JSON.parse(localStorage.getItem(storageKey)||'{}');}catch{}apply(saved);}
 function clearDrop(){for(const card of cards)card.classList.remove('panelDropBefore','panelDropAfter');}
 function cleanup(){clearDrop();if(drag)byId.get(drag.id)?.classList.remove('panelDragging');drag=null;}
 function move(id,targetId,after=false){const card=byId.get(id),target=byId.get(targetId);if(!card||!target||card===target)return false;sidebar.insertBefore(card,after?target.nextSibling:target);save();return true;}
 for(const card of cards){const id=card.dataset.panelId,toggle=card.querySelector('.inspectorPanelToggle'),grip=card.querySelector('.inspectorPanelGrip');
  toggle.onclick=()=>{if(allowed()&&!drag)collapsed(id,!card.querySelector('.inspectorCardBody').hidden);};
  grip.onkeydown=ev=>{if(!ev.altKey||!['ArrowUp','ArrowDown'].includes(ev.key))return;ev.preventDefault();ev.stopPropagation();if(!allowed()||drag)return;const order=state().order,index=order.indexOf(id),target=order[index+(ev.key==='ArrowUp'?-1:1)];if(target&&move(id,target,ev.key==='ArrowDown')){grip.focus({preventScroll:true});card.scrollIntoView({block:'nearest'});}};
  grip.ondragstart=ev=>{if(!allowed()){ev.preventDefault();return;}drag={id,target:null,after:false};ev.dataTransfer.effectAllowed='move';ev.dataTransfer.setData('text/plain',id);card.classList.add('panelDragging');};
  grip.ondragend=cleanup;
 }
 sidebar.addEventListener('dragover',ev=>{if(!drag||!allowed())return;ev.preventDefault();ev.dataTransfer.dropEffect='move';const card=ev.target.closest('.inspectorCard');clearDrop();if(card&&byId.has(card.dataset.panelId)&&card.dataset.panelId!==drag.id){const rect=card.getBoundingClientRect();drag.target=card.dataset.panelId;drag.after=ev.clientY>rect.top+rect.height/2;card.classList.add(drag.after?'panelDropAfter':'panelDropBefore');}else drag.target=null;const bounds=sidebar.getBoundingClientRect(),top=bounds.top+sidebar.querySelector('.paneTitle').offsetHeight;if(ev.clientY<top+24)sidebar.scrollTop-=18;else if(ev.clientY>bounds.bottom-24)sidebar.scrollTop+=18;});
 sidebar.addEventListener('drop',ev=>{if(!drag)return;ev.preventDefault();ev.stopPropagation();if(allowed()&&drag.target){const id=drag.id;move(id,drag.target,drag.after);byId.get(id).querySelector('.inspectorPanelGrip').focus({preventScroll:true});}cleanup();});
 sidebar.addEventListener('dragleave',ev=>{if(drag&&!sidebar.contains(ev.relatedTarget)){clearDrop();drag.target=null;}});
 document.addEventListener('keydown',ev=>{if(ev.key==='Escape'&&drag){ev.preventDefault();ev.stopPropagation();cleanup();}});
 window.addEventListener('blur',cleanup);
 restore();window.HSEInspectorPanels={state,restore};
})();
