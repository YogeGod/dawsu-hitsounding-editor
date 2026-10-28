(function(root){
"use strict";
const H=typeof module!=="undefined"?require('./model'):root.HSE;
const sets=['normal','soft','drum'],kinds=['normal','whistle','finish','clap'],notes={normal:35,finish:49,whistle:42,clap:38};
function identity(row){const m=/^(normal|soft|drum)-hit(normal|finish|whistle|clap)([1-9]\d*)?\.(wav|ogg|mp3)$/i.exec(row?.name||'');return m?{set:m[1].toLowerCase(),kind:m[2].toLowerCase(),index:m[3]?Number(m[3]):1,extension:m[4].toLowerCase()}:null;}
function filename(set,kind,index=1){return set+'-hit'+kind+(index>1?index:'')+'.wav';}
function row(set,kind,index=1){return {id:H.id('sample'),name:filename(set,kind,index),note:notes[kind],channel:10,assetKey:"osu-default:"+set+"-hit"+kind+".wav",muted:false,solo:false,sampleMapping:{set,kind,index}};}
function prepare(project){const p=H.clone(project);for(const r of p.rows){const info=identity(r);if(info)r.sampleMapping={set:info.set,kind:info.kind,index:info.index};}for(const set of sets)for(const kind of kinds)if(!p.rows.some(r=>{const i=identity(r);return i&&i.set===set&&i.kind===kind&&i.index===1;}))p.rows.push(row(set,kind));return p;}
function defaultProject(){return prepare(H.project());}
function familyKey(i){return i.set+':'+i.kind;}
function addLayer(project,source){const info=identity(source);if(!info)throw Error('Choose a standard sample family first.');const p=H.clone(project),indices=p.rows.map(identity).filter(i=>i&&i.set===info.set&&i.kind===info.kind).map(i=>i.index),index=Math.max(1,...indices)+1;if(index>999999)throw Error('Sample layer index is too large.');const added=row(info.set,info.kind,index);p.rows.push(added);return {project:p,row:added};}
function entries(project,state={},filter=''){
 const out=[],query=filter.trim().toLowerCase(),closed=state.closed||new Set(),expanded=state.expanded||new Set(),groups=new Map(),custom=[];
 for(const r of project.rows){const i=identity(r);if(i){const key=familyKey(i);if(!groups.has(key))groups.set(key,[]);groups.get(key).push(r);}else custom.push(r);}
 const match=r=>!query||(H.laneLabel(r)+' '+r.name).toLowerCase().includes(query);
 function folder(id,name,depth,count){out.push({id:'group:'+id,name,_group:true,_key:id,_depth:depth,_count:count,_closed:!query&&closed.has(id)});return !!query||!closed.has(id);}
 for(const set of sets){const members=project.rows.filter(r=>identity(r)?.set===set);if(!members.some(match))continue;if(!folder(set,set[0].toUpperCase()+set.slice(1)+' sampleset',0,members.length))continue;
  const setStart=out.length;const saved=project.laneGroups?.familyOrder?.[set]||[],configured=saved.join(',')==='finish,whistle,clap'?[]:saved,ordered=['normal',...new Set([...configured.filter(k=>kinds.includes(k)&&k!=='normal'),...kinds.filter(k=>k!=='normal')])];for(const kind of ordered){
   const key=set+':'+kind,items=(groups.get(key)||[]).slice().sort((a,b)=>identity(a).index-identity(b).index);if(!items.some(match))continue;const base=items.find(r=>identity(r).index===1)||items[0],layers=items.filter(r=>r!==base),open=!!query||expanded.has(key),depth=kind==='normal'?1:2;
   const manual=project.laneGroups?.layerOrder?.[key];if(manual){const ranks=new Map(manual.map((id,i)=>[id,i]));layers.sort((a,b)=>(ranks.get(a.id)??1000000+identity(a).index)-(ranks.get(b.id)??1000000+identity(b).index));}out.push({...base,_depth:depth,_family:key,_addition:kind!=='normal',_setChild:kind!=='normal',_base:true,_layerCount:layers.length,_expanded:open});if(open)for(const r of layers)if(match(r))out.push({...r,_depth:depth+1,_family:key,_addition:kind!=='normal',_setChild:true,_layer:true});
  }
  if(out.length>setStart)out[out.length-1]._lastInSet=true;
 }
 if(custom.some(match)&&folder('custom','Other samples',0,custom.length))for(const r of custom.filter(match))out.push({...r,_depth:1});return out;
}
function reorder(project,rowId,targetId,after=false){
 if(rowId===targetId)return H.clone(project);
 const source=project.rows.find(r=>r.id===rowId),target=project.rows.find(r=>r.id===targetId),a=identity(source),b=identity(target);if(!a&&!b)return H.reorderRow(project,rowId,targetId,after);if(!a||!b||a.set!==b.set)throw Error('Move sample layers within their family, or addition families within their sampleset.');
 const p=H.clone(project);p.laneGroups??={};function moved(list,from,to){list=list.filter(v=>v!==from);const index=list.indexOf(to);if(index<0)return list;list.splice(index+(after?1:0),0,from);return list;}
 if(a.kind===b.kind){if(a.index===1||b.index===1)throw Error('The base sample stays at the top of its family.');const key=familyKey(a),ordered=entries(project,{expanded:new Set([key])}).filter(r=>r._layer&&r._family===key).map(r=>r.id);p.laneGroups.layerOrder??={};p.laneGroups.layerOrder[key]=moved(ordered,rowId,targetId);}
 else{if(a.kind==='normal'||b.kind==='normal')throw Error('Hitnormal stays above its addition lanes.');p.laneGroups.familyOrder??={};const ordered=[...new Set([...(project.laneGroups?.familyOrder?.[a.set]||[]),...kinds.filter(k=>k!=='normal')])];p.laneGroups.familyOrder[a.set]=moved(ordered,a.kind,b.kind);}
 return p;
}
const api={sets,kinds,identity,filename,familyKey,prepare,defaultProject,addLayer,entries,reorder};if(typeof module!=="undefined")module.exports=api;else root.HSEGroups=api;
})(typeof globalThis!=="undefined"?globalThis:this);
