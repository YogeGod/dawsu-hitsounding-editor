"use strict";
const $=id=>document.getElementById(id),H=HSE;
const SG=typeof HSEGroups!=="undefined"?HSEGroups:null;
const BM=typeof HSEBookmarks!=="undefined"?HSEBookmarks:null;
const useDemo=!SG||!window.location||new URLSearchParams(window.location.search).get("demo")==="1";
let history=new H.History(useDemo?H.demo():SG.defaultProject()),selected=new Set(),active=history.project.rows[0]?.id,tool="select",cursor=0,view=0,scale=.12,scrollY=0,range=null,gesture=null,playing=false,looping=false,snap=true,dirty=false,clipboard=null;
let masterGain=null,masterVolume=20,appVersion="";
let audioContext,audioGeneration=0,buffers=new Map(),bufferLoads=new Map(),waveforms=new Map(),voices=new Set(),playingOrigin=0,scheduledUntil=0,scheduler,songVoice=null;
let renamingLane=null;const laneNames=new Map(),laneElements=new Map();let laneRenderKey="",controllerAllSelection=null;
let rowH=31,controllerCollapsed=false,inspectorCollapsed=false,lanesCollapsed=false,infoCollapsed=false;
let altDraw=false,rowDrag=null,locatorDrag=null,transport=null,transportTick=null,songScheduledThrough=0;
const defaultAudioSettings={backend:"browser",deviceId:"",sampleRate:48000,latencyMs:30,bridgeFrames:512,channelOffset:0,latencyHint:"interactive"};
let playbackRequest=0,preparingPlayback=false;
let outputSettings={...defaultAudioSettings},nativeBridge=null,audioOpening=null,audioNeedsReapply=false;
const audioPreferencesReady=typeof desktop.preferences==="function"?desktop.preferences().then(value=>{outputSettings={...defaultAudioSettings,...value.audio};}).catch(()=>{}):Promise.resolve();
async function ensureAudioOutput(){
 await audioPreferencesReady;if(outputSettings.backend==="browser"){context();return;}if(nativeBridge)return;if(audioOpening)return audioOpening;
 audioOpening=(async()=>{const ctx=context();if(typeof HSEAudioBridge==="undefined")throw Error("Native audio bridge is unavailable.");const result=await desktop.audioConfigure({...outputSettings,sampleRate:ctx.sampleRate});nativeBridge=await HSEAudioBridge.AudioBridge.create(ctx,masterGain,outputSettings.bridgeFrames,data=>desktop.audioPcm(data),error=>recoverAudio(error.message));return result;})();
 try{return await audioOpening;}catch(e){await recoverAudio(e.message);throw Error("Native audio could not start: "+e.message+" Browser output is active; choose another audio setting.");}finally{audioOpening=null;}
}
async function recoverAudio(error){nativeBridge?.dispose();nativeBridge=null;outputSettings={...defaultAudioSettings};await desktop.audioClose?.().catch(()=>{});status("Audio output failed: "+error+" · using browser output");}
async function applyAudioSettings(next){stop();audioGeneration++;bufferLoads.clear();nativeBridge?.dispose();nativeBridge=null;await desktop.audioClose?.();if(audioContext){await audioContext.close();audioContext=null;}masterGain=null;songGain=null;lastSongVolume=null;buffers.clear();outputSettings={...defaultAudioSettings,...next};await ensureAudioOutput();audioNeedsReapply=false;}
desktop.onAudioError?.(error=>{stop();recoverAudio(error);});
const songVoices=new Set();
function modifierDraw(ev){return !!ev.altKey&&!ev.ctrlKey&&!ev.metaKey&&!ev.getModifierState?.("AltGraph");}
function effectiveTool(){return altDraw?"draw":tool;}
function syncTools(){
 const activeTool=gesture?.tool||effectiveTool();
 $("grid").dataset.tool=activeTool;$("bars").dataset.tool=activeTool;
 for(const bt of document.querySelectorAll("[data-tool]"))if(bt.tagName==="BUTTON")bt.classList.toggle("on",bt.dataset.tool===activeTool);
}
function setAltDraw(held){if(altDraw!==held){altDraw=held;syncTools();}}

const groupState={closed:new Set(),expanded:new Set(SG?SG.sets.flatMap(set=>SG.kinds.map(kind=>set+":"+kind)):[])};
const p=()=>history.project,visibleRows=()=>SG?SG.entries(p(),groupState,$("filter").value):p().rows.filter(r=>(H.laneLabel(r)+" "+r.name).toLowerCase().includes($("filter").value.toLowerCase())),activeRow=()=>p().rows.find(r=>r.id===active);
// Folder headers occupy half a sample lane; every drawing and input path shares these offsets.
const rowGeometryCache=new WeakMap();
function rowGeometry(rows){let g=rowGeometryCache.get(rows);if(g)return g;const tops=[0];for(const row of rows)tops.push(tops.at(-1)+(row._group?rowH/2:rowH));g={tops,total:tops.at(-1),index(y){if(y<0)return -1;if(y>=this.total)return rows.length;let lo=0,hi=rows.length;while(lo<hi){const mid=(lo+hi)>>1;if(tops[mid+1]<=y)lo=mid+1;else hi=mid;}return lo;}};rowGeometryCache.set(rows,g);return g;}
function rowTop(i,rows=visibleRows()){return rowGeometry(rows).tops[i]??rowGeometry(rows).total;}
function rowIndexAt(y,rows=visibleRows()){return rowGeometry(rows).index(y);}
function rowsHeight(rows=visibleRows()){return rowGeometry(rows).total;}
function resetSampleGroups(project){if(!SG)return;groupState.closed.clear();groupState.expanded=new Set(project.rows.map(SG.identity).filter(Boolean).map(SG.familyKey));}

function saveSampleGroups(){if(SG)p().laneGroups={...p().laneGroups,closed:[...groupState.closed],expanded:[...groupState.expanded]};}
function toggleSampleFolder(key){if(gesture||rowDrag)return;if(renamingLane)finishLaneRename(true,false);if(groupState.closed.has(key))groupState.closed.delete(key);else groupState.closed.add(key);saveSampleGroups();update();}
function toggleSampleFamily(key){if(gesture||rowDrag)return;if(groupState.expanded.has(key))groupState.expanded.delete(key);else groupState.expanded.add(key);saveSampleGroups();update();}
function revealSampleRow(rowId){if(!SG)return;const info=SG.identity(p().rows.find(r=>r.id===rowId));if(info){groupState.closed.delete(info.set);groupState.closed.delete(info.set+':additions');groupState.expanded.add(SG.familyKey(info));}else groupState.closed.delete('custom');}
function addSampleLayer(rowId){if(!SG||gesture||rowDrag||locatorDrag)return;const next=SG.addLayer(p(),p().rows.find(r=>r.id===rowId));active=next.row.id;revealSampleRow(rowId);groupState.expanded.add(SG.familyKey(SG.identity(next.row)));commit('Add numbered sample layer',next.project);saveSampleGroups();const i=visibleRows().findIndex(r=>r.id===active);if(i>=0)scrollY=Math.max(0,rowTop(i+1)-$('grid').clientHeight);update();}
function rowSampleAsset(row,event){
 if(!row)return null;
 if(event)return H.sampleAsset(row,event);
 const events=p().events.filter(e=>e.rowId===row.id);
 return H.sampleAsset(row,events.length&&events.every(e=>e.source?.explicitFilename)?events[0]:undefined);
}
function status(s){$("status").textContent=s;}
function text(tag,value,cls){const e=document.createElement(tag);e.textContent=value;if(cls)e.className=cls;return e;}
async function modal(title,body,apply="Apply"){setAltDraw(false);stop();$("dialogTitle").textContent=title;$("dialogBody").replaceChildren();if(typeof body==="string")$("dialogBody").append(text("p",body));else $("dialogBody").append(body);$("dialogApply").textContent=apply;$("dialog").showModal();return new Promise(resolve=>$("dialog").onclose=()=>resolve($("dialog").returnValue==="apply"));}
function form(fields){const div=document.createElement("div");for(const f of fields){const label=text("label",f.label),input=document.createElement(f.options?"select":"input");input.id=f.id;if(f.options)for(const [value,labelText] of f.options){const o=text("option",labelText);o.value=value;input.append(o);}else{input.type=f.type||"number";if(f.min!=null)input.min=f.min;if(f.max!=null)input.max=f.max;input.step=f.step||"any";}input.value=f.value;label.append(input);div.append(label);}return div;}
async function message(title,content){await modal(title,content,"OK");}
function run(fn){return async(...args)=>{try{return await fn(...args);}catch(e){status(e.message);await message("Could not complete operation",e.message);}};}
function commit(name,next,before=p()){if(history.commit(name,before,next)){dirty=true;status(name);}update();}
// Keep the full active lane visible; selection only narrows an edit gesture.
function targets(){return p().events.filter(e=>e.rowId===active).map(e=>e.id);}
function allControllerSelected(){return controllerAllSelection&&controllerAllSelection.projectId===p().id&&controllerAllSelection.active===active&&selected.size===controllerAllSelection.count&&[...selected].every(id=>controllerAllSelection.selection.has(id));}
function controllerTargets(){return allControllerSelected()?p().events.filter(e=>selected.has(e.id)).map(e=>e.id):targets();}
function controllerBars(){
 const scope=new Set(controllerTargets()),events=p().events.filter(e=>scope.has(e.id));
 if(!allControllerSelected())return events.map(event=>({event,x:xAt(event.timeMs)-3,width:6}));
 const groups=new Map();for(const event of events){if(!groups.has(event.timeMs))groups.set(event.timeMs,[]);groups.get(event.timeMs).push(event);}
 const result=[];for(const group of groups.values()){const width=Math.min(6,18/group.length),start=xAt(group[0].timeMs)-width*group.length/2;group.forEach((event,i)=>result.push({event,x:start+i*width,width}));}return result;
}
function controllerBarAt(x){return controllerBars().filter(b=>x>=b.x-2&&x<=b.x+b.width+2).sort((a,b)=>Math.abs(a.x+a.width/2-x)-Math.abs(b.x+b.width/2-x))[0]?.event;}

function settings(){return {enabled:snap,divisor:Number($("divisor").value),target:$("snapMode").value,cursorMs:cursor};}
const lanePalette={normal:['#262b3e','#343e58'],whistle:['#373222','#4b422b'],finish:['#35272b','#49343a'],clap:['#302737','#43344d']};
function laneBackground(row,chosen=false){if(row._group)return '#303039';const kind=SG?.identity(row)?.kind;return kind?lanePalette[kind][chosen?1:0]:chosen?'#293b50':'#252529';}
function sampleKindIcon(row){const kind=SG?.identity(row)?.kind;if(!kind)return null;const icon=text('img','','laneKindIcon');icon.src='src/icons/hit'+kind+'.svg';icon.alt=kind==='normal'?'Hitnormal':kind[0].toUpperCase()+kind.slice(1);icon.title=icon.alt;icon.draggable=false;return icon;}
function eventColour(e){if(e.muted)return "#777b81";if($("colour").value==="assignment")return e.ownerId?"#83b29b":"#d39e62";const v=$("colour").value==="velocity"?(e.performance?.velocity||0)*100/127:e.volume;return "hsl("+(215-v*2.15)+",68%,"+(selected.has(e.id)?66:51)+"%)";}
function resize(c){const w=Math.max(1,c.clientWidth),h=Math.max(1,c.clientHeight),d=window.devicePixelRatio||1;if(c.width!==Math.round(w*d)||c.height!==Math.round(h*d)){c.width=Math.round(w*d);c.height=Math.round(h*d);}const ctx=c.getContext("2d");ctx.setTransform(d,0,0,d,0,0);return {ctx,w,h};}
// Pixel geometry follows both zoom axes, with readable and row-safe bounds.
function diamondSize(){const radius=H.clamp(6*Math.sqrt(scale/.12)*Math.sqrt(rowH/31),3,Math.min(14,rowH*.4));return {w:radius,h:radius};}
function setRowHeight(value,anchorY=$("grid").clientHeight/2){
 if(gesture||rowDrag||locatorDrag||!Number.isFinite(value))return;
 const next=H.clamp(value,18,96),y=H.clamp(anchorY,0,$("grid").clientHeight),rowAtPointer=(scrollY+y)/rowH;
 scrollY=Math.max(0,rowAtPointer*next-y);rowH=next;update();
}
function xAt(t){return (t-view)*scale;}function timeAt(x){return view+x/scale;}
function lines(ctx,w,h,labels=false){
 let lastLabel=-100;const signatures=H.signatureSegments(p()),a=H.timeToBeat(view,p().tempos),b=H.timeToBeat(timeAt(w),p().tempos);let step=1/Number($("divisor").value);
 while((H.beatToTime(a+step,p().tempos)-view)*scale<10&&step<64)step*=2;
 const points=new Set(H.gridBeats(p(),a-step,b+step,step));
 for(let i=0;i<signatures.length;i++){
  const sig=signatures[i],unit=4/sig.denominator,barLength=sig.numerator*unit,start=i===0?a:Math.max(a,sig.beat),end=Math.min(b,i+1<signatures.length?signatures[i+1].beat:b);
  if(end<start)continue;const pixels=Math.abs((H.beatToTime(sig.beat+barLength,p().tempos)-H.beatToTime(sig.beat,p().tempos))*scale),stride=Math.max(1,Math.ceil(30/Math.max(.001,pixels)));
  for(let n=Math.floor((start-sig.beat)/barLength/stride)*stride;n*barLength+sig.beat<=end;n+=stride)points.add(sig.beat+n*barLength);
  const beatPixels=pixels/sig.numerator;if(beatPixels>=15)for(let n=Math.ceil((start-sig.beat)/unit);sig.beat+n*unit<=end;n++)points.add(sig.beat+n*unit);
 }
 for(const beat of [...points].sort((a,b)=>a-b)){
  const x=xAt(H.beatToTime(beat,p().tempos)),pos=H.beatPosition(p(),beat,signatures),bar=pos.isBar,whole=pos.isBeat;
  ctx.strokeStyle=bar?"#73737e":whole?"#4d4d57":"#34343c";ctx.lineWidth=bar?1.5:1;ctx.beginPath();ctx.moveTo(Math.round(x)+.5,0);ctx.lineTo(Math.round(x)+.5,h);ctx.stroke();
  if(labels&&whole&&x-lastLabel>=42){lastLabel=x;ctx.fillStyle=bar?"#e3e3e9":"#aaaab7";ctx.font="10px Consolas";ctx.fillText(String(pos.barNumber)+(bar?"":"."+pos.beatNumber),x+5,15);}
 }
}
function gameplayHit(time){
 const edges=p().objects.filter(o=>Math.abs(o.timeMs-time)*scale<10).sort((a,b)=>Math.abs(a.timeMs-time)-Math.abs(b.timeMs-time));if(edges.length)return edges;
 const span=H.sliderSpans(p()).filter(s=>time>=s.startMs&&time<=s.endMs).sort((a,b)=>(a.endMs-a.startMs)-(b.endMs-b.startMs))[0];
 if(!span)return [];const ids=new Set([span.headId,span.tailId,...span.repeatIds||[]]);return p().objects.filter(o=>ids.has(o.id));
}
function renderGameplay(){
 const o=resize($("objects")),ctx=o.ctx;ctx.fillStyle="#252529";ctx.fillRect(0,0,o.w,o.h);lines(ctx,o.w,o.h);
 const owners=new Set(p().events.filter(e=>selected.has(e.id)).map(e=>e.ownerId)),positions=new Map(),spans=H.sliderSpans(p());
 spans.forEach((span,i)=>{const y=o.h/2,ids=[span.headId,span.tailId,...span.repeatIds||[]];for(const id of ids)positions.set(id,y);const a=xAt(span.startMs),b=xAt(span.endMs);if(b<0||a>o.w)return;
  ctx.lineCap="round";ctx.strokeStyle=ids.some(id=>owners.has(id))?"#007aff":"#498a98";ctx.lineWidth=7;ctx.beginPath();ctx.moveTo(Math.max(-7,a),y);ctx.lineTo(Math.min(o.w+7,b),y);ctx.stroke();ctx.lineCap="butt";
 });
 for(const obj of p().objects){const x=xAt(obj.timeMs);if(x<-8||x>o.w+8)continue;const y=positions.get(obj.id)??o.h/2,chosen=owners.has(obj.id),slider=obj.role?.startsWith("slider-");ctx.fillStyle=chosen?"#007aff":slider?"#66adbb":"#78899d";ctx.strokeStyle=chosen?"#fff":slider?"#9bc0c9":"#bac7d8";ctx.lineWidth=1.5;
  if(obj.role==="slider-repeat"){ctx.beginPath();ctx.moveTo(x-4,y-5);ctx.lineTo(x+2,y);ctx.lineTo(x-4,y+5);ctx.stroke();}else{ctx.beginPath();ctx.arc(x,y,chosen?8.5:7,0,Math.PI*2);ctx.fill();ctx.stroke();}
  if(obj.role==="slider-head"||obj.role==="slider-tail"){ctx.fillStyle="#bad5db";ctx.font="8px Segoe UI";ctx.fillText(obj.role==="slider-head"?"H":"E",x-2,y+12);}
 }
 cursorLine(ctx,o.h);
}

function cursorLine(ctx,h){ctx.strokeStyle="#d34983";ctx.lineWidth=1;ctx.beginPath();ctx.moveTo(xAt(cursor)+.5,0);ctx.lineTo(xAt(cursor)+.5,h);ctx.stroke();}
function render(){
 const rows=visibleRows();const g=resize($("grid"));g.ctx.fillStyle="#1f1f23";g.ctx.fillRect(0,0,g.w,g.h);
 for(let i=0;i<rows.length;i++){const y=rowTop(i,rows)-scrollY,h=rows[i]._group?rowH/2:rowH;if(y+h<0||y>g.h)continue;g.ctx.fillStyle=laneBackground(rows[i],rows[i].id===active);g.ctx.fillRect(0,y,g.w,h-1);}lines(g.ctx,g.w,g.h);
 drawCycleShade(g.ctx,g.h);
 if(range){g.ctx.fillStyle="#007aff18";g.ctx.fillRect(xAt(Math.min(range.start,range.end)),0,Math.abs(range.end-range.start)*scale,g.h);}
 const rowMap=new Map(rows.map((r,i)=>[r.id,i])),marker=diamondSize();
 for(const e of p().events){const ri=rowMap.get(e.rowId);if(ri===undefined)continue;const y=rowTop(ri,rows)-scrollY+rowH/2,x=xAt(e.timeMs);if(x<-Math.max(marker.w+2,e.durationMs*scale)||x>g.w+marker.w+2||y<-rowH||y>g.h+rowH)continue;g.ctx.fillStyle=eventColour(e);g.ctx.strokeStyle=selected.has(e.id)?"#b9dcff":"#08090bc9";g.ctx.lineWidth=selected.has(e.id)?1.5:1;
  if($("showLength").checked){const width=Math.max(7,e.durationMs*scale);g.ctx.fillRect(x,y-6,width,12);g.ctx.strokeRect(x,y-6,width,12);}else{g.ctx.beginPath();g.ctx.moveTo(x,y-marker.h);g.ctx.lineTo(x+marker.w,y);g.ctx.lineTo(x,y+marker.h);g.ctx.lineTo(x-marker.w,y);g.ctx.closePath();g.ctx.fill();g.ctx.stroke();}
  if(e.muted){g.ctx.strokeStyle="#202125";g.ctx.beginPath();g.ctx.moveTo(x-marker.w*.7,y-marker.h*.65);g.ctx.lineTo(x+marker.w*.7,y+marker.h*.65);g.ctx.stroke();}
 }
 if(gesture?.type==="marquee"){g.ctx.fillStyle="#007aff1c";g.ctx.strokeStyle="#668fbd";g.ctx.fillRect(gesture.x,gesture.y,gesture.lastX-gesture.x,gesture.lastY-gesture.y);g.ctx.strokeRect(gesture.x,gesture.y,gesture.lastX-gesture.x,gesture.lastY-gesture.y);}
 cursorLine(g.ctx,g.h);
 const r=resize($("ruler"));r.ctx.fillStyle="#303035";r.ctx.fillRect(0,0,r.w,r.h);lines(r.ctx,r.w,r.h,true);r.ctx.fillStyle="#a3a3b1";r.ctx.font="9px Consolas";for(let x=0;x<r.w;x+=150)r.ctx.fillText((timeAt(x)/1000).toFixed(2)+" s",x+5,33);drawLocators(r.ctx,r.w,r.h);for(const signature of H.signatureMap(p())){const x=xAt(signature.timeMs);if(x<0||x>r.w)continue;r.ctx.fillStyle="#41414a";r.ctx.fillRect(x+1,20,36,18);r.ctx.fillStyle="#d5d9e5";r.ctx.font="10px Consolas";r.ctx.fillText(signature.numerator+"/"+signature.denominator,x+4,33);}drawBookmarks(r.ctx,r.w,r.h);cursorLine(r.ctx,r.h);
 const wave=resize($("wave"));wave.ctx.fillStyle="#26262b";wave.ctx.fillRect(0,0,wave.w,wave.h);const peaks=waveforms.get(p().audio?.assetKey);if(peaks){const offset=H.audioSettings(p().audio).offsetMs,mid=wave.h/2,amp=mid-3;wave.ctx.fillStyle="#a0abba";for(let x=0;x<wave.w;x++){const v=HSEWaveform.range(peaks,timeAt(x)-offset,timeAt(x+1)-offset);wave.ctx.fillRect(x,mid-v.max*amp,1,Math.max(1,(v.max-v.min)*amp));}}else{wave.ctx.fillStyle="#8e8e9c";wave.ctx.font="10px Segoe UI";wave.ctx.fillText(p().audio?"Preparing waveform…":"Load song audio to display its waveform",10,24);}wave.ctx.globalAlpha=.65;lines(wave.ctx,wave.w,wave.h);wave.ctx.globalAlpha=1;cursorLine(wave.ctx,wave.h);
 renderGameplay();
 if(!controllerCollapsed){const b=resize($("bars"));b.ctx.fillStyle="#222226";b.ctx.fillRect(0,0,b.w,b.h);lines(b.ctx,b.w,b.h);b.ctx.strokeStyle="#41414b";for(const q of [.25,.5,.75]){b.ctx.beginPath();b.ctx.moveTo(0,b.h*q+.5);b.ctx.lineTo(b.w,b.h*q+.5);b.ctx.stroke();}const velocity=$("controller").value==="velocity";for(const bar of controllerBars()){const e=bar.event,x=bar.x;if(x+bar.width<0||x>b.w)continue;const v=velocity?(e.performance?.velocity||0)/127:e.volume/100;b.ctx.fillStyle=selected.has(e.id)?"#007aff":eventColour(e);b.ctx.fillRect(x,b.h*(1-v),bar.width,Math.max(2,b.h*v));}cursorLine(b.ctx,b.h);}
 const signature=H.beatPosition(p(),H.timeToBeat(cursor,p().tempos));$("timeSignature").textContent=signature.numerator+"/"+signature.denominator;
 $("clock").textContent=new Date(Math.max(0,cursor)).toISOString().slice(14,23);
}
function renderRows(){const list=$("rows"),rows=visibleRows(),height=$("grid").clientHeight;list.classList.toggle("grouped",!!SG);scrollY=H.clamp(scrollY,0,Math.max(0,rowsHeight(rows)-height));$("vertical").max=Math.max(0,rowsHeight(rows)-height);$("vertical").value=scrollY;if(renamingLane&&document.activeElement===renamingLane.input)return;const key=JSON.stringify([rowH,scrollY,height,lanesCollapsed,rows.map(r=>[r.id,r.name,r.label,r.note,r.muted,r.solo,r._closed,r._expanded,r._layerCount,!!rowSampleAsset(r)])]);if(key===laneRenderKey){for(const [id,node] of laneElements){node.classList.toggle("active",id===active);const row=rows.find(r=>r.id===id);if(row)node.style.backgroundColor=laneBackground(row,id===active);}return;}laneRenderKey=key;laneNames.clear();laneElements.clear();scrollY=H.clamp(scrollY,0,Math.max(0,rowsHeight(rows)-height));list.replaceChildren();const geometry=rowGeometry(rows),start=Math.max(0,geometry.index(scrollY));
 const count=Math.max(1,geometry.index(scrollY+height)-start+1);
 const holder=document.createElement("div");holder.style.transform="translateY("+(rowTop(start,rows)-scrollY)+"px)";
 rows.slice(start,start+count).forEach(row=>{
 if(row._group){const folder=text('div','','sampleGroup');folder.style.height=(rowH/2)+'px';folder.style.paddingLeft=(7+row._depth*10)+'px';folder.dataset.groupKey=row._key;const toggle=text('button',row._closed?'▸':'▾','groupDisclosure');toggle.setAttribute('aria-label',(row._closed?'Expand ':'Collapse ')+row.name);toggle.setAttribute('aria-expanded',String(!row._closed));toggle.onclick=ev=>{ev.stopPropagation();toggleSampleFolder(row._key);};folder.append(toggle,text('span',row.name,'groupName'),text('span',String(row._count),'groupCount'));folder.onclick=()=>toggleSampleFolder(row._key);holder.append(folder);return;}
 const el=text("div","","sampleRow"+(row.id===active?" active":""));const num=text("span",String(row.note),"num"),dot=text("span","","dot"+(rowSampleAsset(row)?" loaded":"")),name=text("span",H.laneLabel(row),"sampleName");laneNames.set(row.id,name);el.title=H.laneLabel(row)+(row.label?" · Sample: "+row.name:"");el.dataset.rowId=row.id;laneElements.set(row.id,el);el.style.height=rowH+"px";el.style.backgroundColor=laneBackground(row,row.id===active);el.dataset.sampleKind=SG?.identity(row)?.kind||"custom";
 if(SG){el.style.paddingLeft=(5+(row._depth||0)*10+(row._addition?8:0)+(row._layer?18:0))+'px';el.classList.toggle('parentLane',!!row._base&&!row._addition&&!!row._family);el.classList.toggle('setChild',!!row._setChild);el.classList.toggle('additionLane',!!row._addition);el.classList.toggle('lastInSet',!!row._lastInSet);num.textContent=String(SG.identity(row)?.index||row.note);num.title=SG.identity(row)?'Sample index '+SG.identity(row).index:'MIDI note '+row.note;}
 const handle=text("button","⠿","rowHandle");handle.title="Drag to reorder sample rows";handle.setAttribute("aria-label","Reorder "+row.name);handle.onpointerdown=ev=>{ev.preventDefault();ev.stopPropagation();focusEditor();rowDrag={id:row.id,target:row.id,after:false};list.setPointerCapture(ev.pointerId);};handle.onclick=ev=>ev.stopPropagation();el.append(handle);
 if(row._base){const fold=text('button',row._layerCount?(row._expanded?'▾':'▸'):'·','familyDisclosure');fold.disabled=!row._layerCount;fold.title=row._layerCount+' numbered layers';fold.setAttribute('aria-expanded',String(!!row._expanded));fold.onclick=ev=>{ev.stopPropagation();toggleSampleFamily(row._family);};el.append(fold);}
 el.append(num,dot,name);const kindIcon=sampleKindIcon(row);if(kindIcon)el.append(kindIcon);
 if(row._base){if(row._layerCount)el.append(text('span','+'+row._layerCount,'layerCount'));const plus=text('button','+','addLayer');plus.title='Add next numbered '+row._family.replace(':','-hit')+' layer';plus.setAttribute('aria-label',plus.title);plus.onclick=ev=>{ev.stopPropagation();addSampleLayer(row.id);};el.append(plus);}
 for(const [letter,prop,cls] of [["M","muted","muted"],["S","solo","solo"]]){const bt=text("button",letter,row[prop]?cls:"");bt.title=(prop==="muted"?"Mute ":"Solo ")+row.name;bt.onclick=e=>{e.stopPropagation();const q=H.clone(p());q.rows.find(r=>r.id===row.id)[prop]=!row[prop];commit(prop,q);};el.append(bt);}el.onclick=ev=>{focusEditor();controllerAllSelection=null;active=row.id;if(ev.ctrlKey||ev.shiftKey){const ids=p().events.filter(e=>e.rowId===row.id).map(e=>e.id);selected=new Set(ev.shiftKey?[...selected,...ids]:ids);}update();};holder.append(el);});list.append(holder);
 $("vertical").max=Math.max(0,rowsHeight(rows)-height);$("vertical").value=scrollY;}
function update(){
 syncSidebars();syncInfoPanel();syncControllerPanel();renderBookmarkList();$("rowHeight").value=rowH;$("rowHeightValue").textContent=Math.round(rowH)+" px";document.title=p().name+" — osu!daw hitsounding editor"+(appVersion?" "+appVersion:"");
 const eventIDs=new Set(p().events.map(e=>e.id));selected=new Set([...selected].filter(id=>eventIDs.has(id)));if(!p().rows.some(r=>r.id===active))active=p().rows[0]?.id;
 $("projectName").textContent=p().name+(dirty?" *":"");$("selectionCount").textContent=selected.size?selected.size+" event"+(selected.size===1?"":"s"):"No events";const sel=p().events.filter(e=>selected.has(e.id)),first=sel[0];
 for(const [id,value] of [["eventTime",first?.timeMs],["eventLength",first?.durationMs],["eventVolume",first?.volume],["eventVelocity",first?.performance?.velocity]]){$(id).value=value==null?"":Math.round(value*1000)/1000;$(id).disabled=!first||(id==="eventVelocity"&&!first.performance);}
 $("owner").textContent=first?(first.ownerId?p().objects.find(o=>o.id===first.ownerId)?.role||"Assigned":first.assignment):"—";
 $("undo").disabled=!history.undoStack.length;$("redo").disabled=!history.redoStack.length;
 const row=activeRow();$("rowName").value=row?H.laneLabel(row):"";$("sampleFilename").textContent=row?(row.sourceSampleName?"Exports as "+row.name+" · File: "+row.sourceSampleName:"Sample: "+row.name):"No lane selected";for(const id of ["rowName","rowNote","rowChannel","replaceSample","deleteLane","deleteLaneQuick"])$(id).disabled=!row;$("rowNote").value=row?.note??35;$("rowChannel").value=row?.channel??10;$("assetState").textContent=row?(row.assetKey&&!row.assetKey.startsWith("osu-default:")?"Custom sample file loaded":rowSampleAsset(row)?"osu! default skin · built-in fallback":"No sample file · choose one for this row"):"Add samples or import a file";
 $("audioState").textContent=p().audio?.name||"No song audio";syncSongControls();const laneSelected=p().events.filter(e=>e.rowId===active&&selected.has(e.id)).length;$("controllerScope").textContent=allControllerSelected()?"All selected sounds | "+selected.size+" events":(row?H.laneLabel(row):"No row")+(laneSelected?" | "+laneSelected+" selected":"");
 $("rangeState").textContent=range?Math.round(Math.min(range.start,range.end))+" → "+Math.round(Math.max(range.start,range.end))+" ms":"Drag with the range tool";
 $("history").replaceChildren(...history.undoStack.slice(-5).reverse().map(e=>text("div",e.name)));const signature=H.beatPosition(p(),H.timeToBeat(cursor,p().tempos));$("timeSignature").textContent=signature.numerator+"/"+signature.denominator;$("tempo").value=H.segments(p().tempos).filter(t=>t.timeMs<=cursor).at(-1)?.bpm||p().tempos[0].bpm;
 syncTools();$("snap").classList.toggle("on",snap);looping=H.cycleSettings(p()).enabled;$("loop").classList.toggle("on",looping);$("loop").setAttribute("aria-pressed",String(looping));
 const cycle=H.cycleSettings(p());$("loopLeft").value=cycle.leftMs;$("loopRight").value=cycle.rightMs;$("loopDuration").textContent=(cycle.leftMs>cycle.rightMs?"Skip ":"Loop ")+Math.round(Math.abs(cycle.rightMs-cycle.leftMs))+" ms";
 $("rowUp").disabled=!row||p().rows[0]?.id===row.id;$("rowDown").disabled=!row||p().rows.at(-1)?.id===row.id;
 refreshTransport();
 $("horizontal").max=Math.max(0,Math.max(H.duration(p()),cycle.leftMs,cycle.rightMs)-($("grid").clientWidth/scale));$("horizontal").value=view;
 updateEditCommands();renderRows();render();
}
function fit(){scale=Math.min(1,Math.max(.0001,($("grid").clientWidth-40)/H.duration(p())));view=0;update();}
function setTool(t){tool=t;update();}function local(ev,c){const r=c.getBoundingClientRect();return {x:ev.clientX-r.left,y:ev.clientY-r.top};}
function hit(x,y){
 const rows=visibleRows(),i=rowIndexAt(y+scrollY,rows),r=rows[i];if(!r||r._group)return null;
 const t=timeAt(x),size=diamondSize(),dy=Math.abs(y-(rowTop(i,rows)-scrollY+rowH/2));
 const events=p().events.filter(e=>e.rowId===r.id).sort((a,b)=>Math.abs(a.timeMs-t)-Math.abs(b.timeMs-t));
 return events.find(e=>$("showLength").checked?dy<=9&&t>=e.timeMs-6/scale&&t<=e.timeMs+Math.max(7/scale,e.durationMs):dy<=Math.max(6,size.h+2)&&Math.abs(e.timeMs-t)*scale<=Math.max(6,size.w+2));
}
var bookmarkListKey='';
function addBookmark(){if(!BM||gesture||rowDrag||locatorDrag)return;commit('Add bookmark',BM.set(p(),cursor,$('bookmarkName').value.trim()||undefined));$('bookmarkName').value='';}
function removeBookmarkAtCursor(){const mark=BM?.list(p()).find(b=>Math.abs(b.timeMs-cursor)<=.5);if(mark)commit('Delete bookmark',BM.remove(p(),mark.id));}
function jumpBookmark(mark){if(!mark)return;stop();cursor=mark.timeMs;if(xAt(cursor)<0||xAt(cursor)>$('grid').clientWidth)view=Math.max(0,cursor-$('grid').clientWidth/scale*.2);update();}
function drawBookmarks(ctx,width,height){if(!BM)return;for(const mark of BM.list(p())){const x=xAt(mark.timeMs);if(x<-6||x>width+6)continue;ctx.fillStyle='#a9c5e9';ctx.beginPath();ctx.moveTo(x,height-10);ctx.lineTo(x+5,height-3);ctx.lineTo(x-5,height-3);ctx.closePath();ctx.fill();}}
function renderBookmarkList(){if(!BM)return;const marks=BM.list(p()),key=JSON.stringify(marks);if(key===bookmarkListKey)return;bookmarkListKey=key;const list=$('bookmarkList');list.replaceChildren();if(!marks.length){list.append(text('div','No bookmarks · Ctrl+B adds one at the playhead.','hint'));return;}for(const mark of marks){const row=text('div','','bookmarkItem'),go=text('button',new Date(mark.timeMs).toISOString().slice(14,23)+' · '+mark.label,'bookmarkJump'),edit=text('button','✎','bookmarkEdit'),remove=text('button','×','bookmarkRemove');go.title='Jump to '+mark.label;go.onclick=()=>jumpBookmark(mark);edit.title='Rename or move bookmark';edit.onclick=run(async()=>{const d=form([{label:'Name',id:'bookmarkEditName',type:'text',value:mark.label},{label:'Time · ms',id:'bookmarkEditTime',value:mark.timeMs,min:0,max:86400000}]);if(await modal('Edit bookmark',d))commit('Edit bookmark',BM.set(p(),Number($('bookmarkEditTime').value),$('bookmarkEditName').value,mark.id));});remove.title='Delete '+mark.label;remove.onclick=()=>commit('Delete bookmark',BM.remove(p(),mark.id));row.append(go,edit,remove);list.append(row);}}
$('addBookmark').onclick=run(addBookmark);$('previousBookmark').onclick=()=>jumpBookmark(BM?.neighbor(p(),cursor,-1));$('nextBookmark').onclick=()=>jumpBookmark(BM?.neighbor(p(),cursor,1));
function gridCursorTime(ms){return Math.max(0,H.snapTime(ms,p(),{enabled:true,target:"grid",divisor:Number($("divisor").value)}));}
function begin(ev,controller=false){
 if(renamingLane)finishLaneRename(true,false);
 if(ev.button===2&&!controller){ev.preventDefault();if(gesture||$("dialog").open)return;focusEditor();const pt=local(ev,$("grid")),found=hit(pt.x,pt.y);if(found){active=found.rowId;commit("Delete event",H.remove(p(),[found.id]));}return;}
 if(ev.button!==0)return;setAltDraw(modifierDraw(ev));const activeTool=effectiveTool();ev.preventDefault();const c=controller?$("bars"):$("grid"),pt=local(ev,c),rows=visibleRows(),row=rows[rowIndexAt(pt.y+scrollY,rows)],t=H.snapTime(timeAt(pt.x),p(),settings()),before=p();c.focus({preventScroll:true});c.setPointerCapture(ev.pointerId);
 if(controller){
  if(controllerCollapsed)return;
  if(allControllerSelected()){const clicked=controllerBarAt(pt.x);if(clicked)active=clicked.rowId;controllerAllSelection=null;}
  const laneIds=targets(),lane=new Set(laneIds);if(!laneIds.length)return;
  const near=before.events.filter(e=>lane.has(e.id)).sort((a,b)=>Math.abs(xAt(a.timeMs)-pt.x)-Math.abs(xAt(b.timeMs)-pt.x))[0];
  const ramp=ev.shiftKey||activeTool==="ramp";let ids=laneIds,group=false;
  if(!ramp&&activeTool==="select"){
   if(!near||Math.abs(xAt(near.timeMs)-pt.x)>=10)return;
   if(!selected.has(near.id))selected=new Set([near.id]);
   ids=laneIds.filter(id=>selected.has(id));group=true;
  }else if(ramp){const inLane=laneIds.filter(id=>selected.has(id));if(inLane.length)ids=inLane;}
  gesture={type:ramp?"ramp":"bar",original:before,before,ids,x:pt.x,y:pt.y,time:timeAt(pt.x),value:controllerValue(pt.y),controller:true,group,primary:near};
  editController(pt);return;
 }
 if(row?._group)return;
 if(row)active=row.id;
 const found=hit(pt.x,pt.y);
 if(activeTool==="audition"){if(row)audition(row.id,found?.volume??80,found);return;}
 if(activeTool==="draw"||activeTool==="erase"){
  if(!row)return;
  const mode=activeTool==="erase"||found?"erase":"draw",strokeSettings={...settings()};
  gesture={type:"paint",mode,before,rowId:row.id,lastTime:mode==="erase"?timeAt(pt.x):t,settings:strokeSettings,visited:new Set()};
  if(mode==="erase")history.project=found?H.remove(before,[found.id]):before;
  else history.project=H.paint(before,row.id,t,t,strokeSettings,"draw",Number($("insertVolume").value),Number($("insertLength").value),gesture.visited);
  render();return;
 }
 if(activeTool==="mute"){if(found){commit("Mute events",H.mute(p(),selected.has(found.id)?[...selected]:[found.id]));}return;}
 if(activeTool==="range"){gesture={type:"range",before,time:t};range={start:t,end:t,rowIds:ev.shiftKey&&row?[row.id]:p().rows.map(r=>r.id)};render();return;}
 if(found){if(ev.ctrlKey){if(selected.has(found.id))selected.delete(found.id);else selected.add(found.id);}else if(ev.shiftKey)selected.add(found.id);else if(!selected.has(found.id))selected=new Set([found.id]);gesture={type:$("showLength").checked&&Math.abs(pt.x-xAt(found.timeMs+found.durationMs))<7?"length":"move",before,ids:[...selected],x:pt.x,y:pt.y,time:found.timeMs,rowIndex:rows.findIndex(r=>r.id===found.rowId),lastX:pt.x,lastY:pt.y};}
 else{if(!ev.ctrlKey&&!ev.shiftKey)selected.clear();gesture={type:"marquee",before,x:pt.x,y:pt.y,lastX:pt.x,lastY:pt.y,base:new Set(selected)};stop();cursor=gridCursorTime(timeAt(pt.x));}update();
}
function syncInfoPanel(){
 $("info").hidden=infoCollapsed;const button=$("toggleInfo");button.setAttribute("aria-expanded",String(!infoCollapsed));button.setAttribute("aria-label",(infoCollapsed?"Show":"Hide")+" selection info");button.textContent=infoCollapsed?"▸ Info":"▾ Info";
}
function setInfoCollapsed(value){
 if(gesture||rowDrag||locatorDrag)return;infoCollapsed=!!value;
 if(infoCollapsed&&$("info").contains(document.activeElement))focusEditor();
 try{localStorage.setItem("hse-info-collapsed",JSON.stringify(infoCollapsed));}catch{}
 update();
}
$("toggleInfo").onclick=()=>setInfoCollapsed(!infoCollapsed);
function savePanelLayout(){try{localStorage.setItem("hse-panel-layout",JSON.stringify({inspectorCollapsed,lanesCollapsed}));}catch{}}
function syncSidebars(){
 $("inspector").classList.toggle("collapsed",inspectorCollapsed);$("editor").classList.toggle("lanesCollapsed",lanesCollapsed);
 for(const [id,collapsed,name] of [["toggleInspector",inspectorCollapsed,"inspector"],["toggleLanes",lanesCollapsed,"sample lane names"]]){const button=$(id);button.setAttribute("aria-expanded",String(!collapsed));button.setAttribute("aria-label",(collapsed?"Show ":"Hide ")+name);button.textContent=collapsed?"›":"‹";}
}
function setInspectorCollapsed(value){if(gesture||rowDrag||locatorDrag)return;inspectorCollapsed=!!value;if(inspectorCollapsed&&document.activeElement!==$("toggleInspector")&&$("inspector").contains(document.activeElement))focusEditor();syncSidebars();savePanelLayout();update();}
function setLanesCollapsed(value){if(gesture||rowDrag||locatorDrag)return;lanesCollapsed=!!value;if(lanesCollapsed&&document.activeElement===$("filter"))focusEditor();syncSidebars();savePanelLayout();update();}
$("toggleInspector").onclick=()=>setInspectorCollapsed(!inspectorCollapsed);
$("toggleLanes").onclick=()=>setLanesCollapsed(!lanesCollapsed);
function syncControllerPanel(){
 $("controllerBody").hidden=controllerCollapsed;
 $("toggleController").setAttribute("aria-expanded",String(!controllerCollapsed));
 $("toggleController").setAttribute("aria-label",controllerCollapsed?"Show controller lane":"Hide controller lane");
 $("toggleController").textContent=controllerCollapsed?"\u25b8":"\u25be";
}
function setControllerCollapsed(value){
 controllerCollapsed=!!value;
 if(controllerCollapsed&&gesture?.controller){history.project=gesture.original||gesture.before;gesture=null;syncTools();}
 if(controllerCollapsed&&document.activeElement===$("bars"))focusEditor();
 syncControllerPanel();update();
}
$("toggleController").onclick=()=>setControllerCollapsed(!controllerCollapsed);

function controllerValue(y){const max=$("controller").value==="velocity"?127:100;return H.clamp(Math.round((1-y/$("bars").clientHeight)*max),1,max);}
function editController(pt){const g=gesture,q=H.clone(g.before),keys=new Set(g.ids),velocity=$("controller").value==="velocity",val=controllerValue(pt.y);
 if(g.type==="ramp"){if(velocity){const a=Math.min(g.time,timeAt(pt.x)),b=Math.max(g.time,timeAt(pt.x));for(const e of q.events)if(keys.has(e.id)&&e.performance&&e.timeMs>=a&&e.timeMs<=b){let f=g.time===timeAt(pt.x)?1:(e.timeMs-g.time)/(timeAt(pt.x)-g.time);const shape=$("rampShape").value;if(shape==="sine")f=(1-Math.cos(f*Math.PI))/2;else if(shape==="parabola")f*=f;else if(shape==="triangle")f=1-Math.abs(2*f-1);else if(shape==="square")f=f<.5?0:1;e.performance.velocity=H.clamp(Math.round(g.value+(val-g.value)*f),1,127);}}else{history.project=H.ramp(g.before,g.ids,g.time,timeAt(pt.x),g.value,val,$("rampShape").value);render();return;}}
 else if(g.group&&g.primary){const primary=g.primary,old=velocity?primary.performance?.velocity:primary.volume,delta=val-old;for(const e of q.events)if(keys.has(e.id)){if(velocity&&e.performance)e.performance.velocity=H.clamp($("valueMode").value==="relative"?e.performance.velocity+delta:val,1,127);else if(!velocity)e.volume=H.clamp($("valueMode").value==="relative"?e.volume+delta:val,1,100);}}
 else{const nearest=q.events.filter(e=>keys.has(e.id)).sort((a,b)=>Math.abs(xAt(a.timeMs)-pt.x)-Math.abs(xAt(b.timeMs)-pt.x))[0];if(nearest&&Math.abs(xAt(nearest.timeMs)-pt.x)<12){if(velocity&&nearest.performance)nearest.performance.velocity=val;else if(!velocity)nearest.volume=val;g.before=q;}}
 history.project=q;render();
}
function moveGesture(ev,controller=false){if(!gesture)return;const pt=local(ev,controller?$("bars"):$("grid")),g=gesture;if(g.controller){editController(pt);return;}
 if(g.type==="paint"){
  if(g.mode==="erase"){
   const t=timeAt(pt.x),a=Math.min(g.lastTime,t),b=Math.max(g.lastTime,t);
   history.project=H.remove(p(),p().events.filter(e=>e.rowId===g.rowId&&e.timeMs>=a&&e.timeMs<=b).map(e=>e.id));g.lastTime=t;
  }else if(g.settings.enabled){
   const t=H.snapTime(timeAt(pt.x),p(),g.settings);history.project=H.paint(p(),g.rowId,g.lastTime,t,g.settings,"draw",Number($("insertVolume").value),Number($("insertLength").value),g.visited);g.lastTime=t;
  }
 }
 if(g.type==="move"){const delta=H.snapTime(g.time+(pt.x-g.x)/scale,g.before,settings(),g.time)-g.time,rows=visibleRows(),target=rows[rowIndexAt(pt.y+scrollY,rows)],origin=rows[g.rowIndex];const dy=target&&!target._group&&origin&&!origin._group?p().rows.findIndex(r=>r.id===target.id)-p().rows.findIndex(r=>r.id===origin.id):0;history.project=H.move(g.before,g.ids,delta,dy);}
 if(g.type==="length"){const q=H.clone(g.before),delta=(pt.x-g.x)/scale,keys=new Set(g.ids);for(const e of q.events)if(keys.has(e.id))e.durationMs=Math.max(0,H.snapTime(e.timeMs+e.durationMs+delta,q,settings())-e.timeMs);history.project=q;}
 if(g.type==="range")range.end=H.snapTime(timeAt(pt.x),p(),settings());
 if(g.type==="marquee"){g.lastX=pt.x;g.lastY=pt.y;const rows=visibleRows(),a=Math.min(g.x,pt.x),b=Math.max(g.x,pt.x),y0=Math.min(g.y,pt.y),y1=Math.max(g.y,pt.y);selected=new Set([...g.base,...p().events.filter(e=>{const i=rows.findIndex(r=>r.id===e.rowId),x=xAt(e.timeMs),y=rowTop(i,rows)-scrollY+rowH/2;return i>=0&&x>=a&&x<=b&&y>=y0&&y<=y1;}).map(e=>e.id)]);}
 render();
}
function endGesture(){if(!gesture)return;const g=gesture;gesture=null;if(!["marquee","range"].includes(g.type))commit(g.type==="paint"?(g.mode==="draw"?"Draw events":"Erase events"):g.type==="move"?"Move events":g.type==="length"?"Resize events":g.type==="ramp"?"Volume ramp":"Edit controller",p(),g.original||g.before);else update();}
for(const [id,isController] of [["grid",false],["bars",true]]){$(id).onpointerdown=e=>{begin(e,isController);if(gesture)gesture.tool=effectiveTool();syncTools();};$(id).onpointerenter=e=>{if(!gesture)setAltDraw(modifierDraw(e));};$(id).onpointermove=e=>{if(!gesture)setAltDraw(modifierDraw(e));moveGesture(e,isController);};$(id).onpointerup=endGesture;$(id).onpointercancel=()=>{if(gesture){history.project=gesture.original||gesture.before;gesture=null;update();}};}
$("grid").oncontextmenu=ev=>ev.preventDefault();
$("grid").ondblclick=ev=>{
 if(ev.button!==0||effectiveTool()!=="select"||gesture||$("dialog").open)return;
 ev.preventDefault();focusEditor();const pt=local(ev,$("grid")),rows=visibleRows(),row=rows[rowIndexAt(pt.y+scrollY,rows)];if(!row||row._group)return;const found=hit(pt.x,pt.y);if(found){audition(found.rowId,found.volume,found);return;}
 const before=p(),t=H.snapTime(timeAt(pt.x),before,settings()),next=H.paint(before,row.id,t,t,settings(),"draw",Number($("insertVolume").value),Number($("insertLength").value));
 if(next.events.length===before.events.length)return;
 active=row.id;const old=new Set(before.events.map(e=>e.id));selected=new Set(next.events.filter(e=>!old.has(e.id)).map(e=>e.id));commit("Draw event",next,before);
};
function stepSnapWheel(ev){
 ev.preventDefault();ev.stopPropagation?.();
 if(gesture||rowDrag||locatorDrag||$("dialog").open||!ev.deltaY)return;
 const control=$("divisor"),values=Array.from(control.options,o=>o.value),index=values.indexOf(control.value);
 if(index<0)return;
 const next=H.clamp(index+(ev.deltaY<0?1:-1),0,values.length-1);
 if(next===index)return;
 control.value=values[next];update();status("Beat snap: 1/"+control.value);
}
$("divisor").onwheel=stepSnapWheel;
document.addEventListener("wheel",ev=>{
 if(!ev.defaultPrevented&&ev.altKey&&!ev.ctrlKey&&!ev.metaKey&&!ev.shiftKey)stepSnapWheel(ev);
},{passive:false});

function timelineWheel(ev,id){
 if(renamingLane)finishLaneRename(true,false);
 if(ev.altKey&&!ev.ctrlKey&&!ev.metaKey&&!ev.shiftKey){stepSnapWheel(ev);return;}
 ev.preventDefault();if(gesture||rowDrag||locatorDrag)return;
 const c=$(id),pt=local(ev,c),factor=ev.deltaMode===1?16:ev.deltaMode===2?$("grid").clientHeight:1,dy=ev.deltaY*factor,dx=ev.deltaX*factor;
 if(ev.ctrlKey&&ev.shiftKey){setRowHeight(rowH*Math.exp(H.clamp(-dy*.002,-4,4)),id==="grid"||id==="rows"?pt.y:$("grid").clientHeight/2);return;}
 if(ev.ctrlKey){const x=id==="rows"?$("grid").clientWidth/2:pt.x,t=timeAt(x),next=H.clamp(scale*Math.exp(H.clamp(-dy*.002,-4,4)),.0001,5);view=Math.max(0,t-x/next);scale=next;}
 else if(ev.shiftKey||Math.abs(dx)>Math.abs(dy))view=Math.max(0,view+(dx||dy)/scale);
 else scrollY+=dy;
 update();
}
for(const id of ["grid","rows","bars","ruler","objects","wave"])$(id).onwheel=ev=>timelineWheel(ev,id);
$("rowHeight").oninput=()=>setRowHeight(Number($("rowHeight").value));
$("vertical").oninput=()=>{scrollY=Number($("vertical").value);update();};
for(const id of ["ruler","objects","wave"])$(id).onpointerdown=e=>{focusEditor();stop();const t=timeAt(local(e,$(id)).x);cursor=gridCursorTime(t);if(id==="objects"){const ob=gameplayHit(t);selected=new Set(p().events.filter(e=>ob.some(o=>o.id===e.ownerId)).map(e=>e.id));}update();};
$("objects").ondblclick=ev=>{if(ev.button!==0||effectiveTool()!=="select"||gesture||$("dialog").open)return;const owners=new Set(gameplayHit(timeAt(local(ev,$("objects")).x)).map(o=>o.id));if(!owners.size)return;ev.preventDefault();for(const e of p().events)if(owners.has(e.ownerId)&&!e.muted)audition(e.rowId,e.volume,e);};
$("horizontal").oninput=()=>{view=Number($("horizontal").value);render();};
$("left").onclick=()=>{view=Math.max(0,view-$("grid").clientWidth/scale*.5);update();};$("right").onclick=()=>{view+=$("grid").clientWidth/scale*.5;update();};
$("zoom").oninput=()=>{scale=Math.pow(10,Number($("zoom").value)/25-3);update();};$("zoomIn").onclick=()=>{scale=Math.min(5,scale*1.5);update();};$("zoomOut").onclick=()=>{scale=Math.max(.0001,scale/1.5);update();};$("fit").onclick=fit;
for(const bt of document.querySelectorAll("[data-tool]"))bt.onclick=()=>setTool(bt.dataset.tool);
for(const id of ["filter","showLength","colour","controller","divisor","snapMode"])$(id).onchange=update;$("filter").oninput=()=>{scrollY=0;update();};$("snap").onclick=()=>{snap=!snap;update();};
$("undo").onclick=()=>{if(history.undo()){dirty=true;update();}};$("redo").onclick=()=>{if(history.redo()){dirty=true;update();}};
function selectedField(id,fn){$(id).onchange=run(()=>{if(!selected.size)return;if(!$(id).value.trim()||!Number.isFinite(Number($(id).value))){update();return status("Enter a valid number.");}commit("Edit "+id.replace("event",""),fn(Number($(id).value)));});}
selectedField("eventTime",v=>{const first=p().events.find(e=>selected.has(e.id));return H.move(p(),[...selected],v-first.timeMs);});
selectedField("eventLength",v=>{const q=H.clone(p());for(const e of q.events)if(selected.has(e.id))e.durationMs=Math.max(0,v);return q;});
selectedField("eventVolume",v=>H.setVolumes(p(),p().events.filter(e=>selected.has(e.id)).map(e=>e.id),v,$("valueMode").value));
selectedField("eventVelocity",v=>{v=Math.round(v);const q=H.clone(p()),first=p().events.find(e=>selected.has(e.id)),delta=v-first.performance.velocity;for(const e of q.events)if(selected.has(e.id)&&e.performance)e.performance.velocity=H.clamp($("valueMode").value==="relative"?e.performance.velocity+delta:v,1,127);return q;});
$("rowName").onchange=run(()=>{if(activeRow())commit("Rename lane",H.renameLane(p(),active,$("rowName").value));});
function finishLaneRename(apply,focus=true){const edit=renamingLane;if(!edit)return;renamingLane=null;if(apply&&p().rows.some(r=>r.id===edit.rowId))commit("Rename lane",H.renameLane(p(),edit.rowId,edit.input.value));else update();if(focus)focusEditor();}
function beginLaneRename(rowId){if(gesture||rowDrag||locatorDrag||$("dialog").open)return;if(renamingLane)finishLaneRename(true);active=rowId;lanesCollapsed=false;update();const row=activeRow(),label=laneNames.get(rowId);if(!row||!label)return;const input=document.createElement("input");input.type="text";input.className="laneNameEdit";input.maxLength=128;input.value=H.laneLabel(row);input.setAttribute("aria-label","Lane display name");renamingLane={rowId,input};laneRenderKey="";label.replaceChildren(input);input.onpointerdown=ev=>ev.stopPropagation();input.onclick=ev=>ev.stopPropagation();input.ondblclick=ev=>ev.stopPropagation();input.onkeydown=ev=>{ev.stopPropagation();if(ev.key==="Enter"||ev.key==="Escape"){ev.preventDefault();finishLaneRename(ev.key==="Enter");}};input.onblur=()=>finishLaneRename(true,false);input.focus();input.select();}
$("rows").ondblclick=ev=>{if((ev.button!=null&&ev.button!==0)||gesture||rowDrag||locatorDrag||$("dialog").open||ev.target.closest?.("button,input"))return;const lane=ev.target.closest?.("[data-row-id]"),rowId=lane?.dataset.rowId||visibleRows()[rowIndexAt(local(ev,$("rows")).y+scrollY)]?.id;if(rowId&&!rowId.startsWith("group:")){ev.preventDefault();if(effectiveTool()==="select")audition(rowId,Number($("insertVolume").value));beginLaneRename(rowId);}};
$("addLane").onclick=()=>{if(gesture||rowDrag||locatorDrag)return;if(SG?.identity(activeRow()))return addSampleLayer(active);const q=H.addLane(p(),activeRow());active=q.rows.at(-1).id;commit("Add lane",q);scrollY=Math.max(0,rowsHeight()-$("grid").clientHeight);update();beginLaneRename(active);};
$("deleteLane").onclick=run(async()=>{if(!activeRow()||gesture||rowDrag||locatorDrag)return;const row=activeRow(),count=p().events.filter(e=>e.rowId===row.id).length;if(count&&!await modal("Delete lane",'Delete "'+H.laneLabel(row)+'" and its '+count+' hitsounds? Gameplay objects and sample files are kept. You can undo this.',"Delete lane"))return;const index=p().rows.findIndex(r=>r.id===row.id),q=H.deleteLane(p(),row.id);active=q.rows[Math.min(index,q.rows.length-1)]?.id||null;commit("Delete lane",q);});
$("addLaneQuick").onclick=()=>$("addLane").click();$("deleteLaneQuick").onclick=()=>$("deleteLane").click();
for(const [id,field,max] of [["rowNote","note",127],["rowChannel","channel",16]])$(id).onchange=run(()=>{if(!$(id).value.trim()||!Number.isFinite(Number($(id).value))){update();return;}const q=H.clone(p()),val=H.clamp(Math.round(Number($(id).value)),field==="channel"?1:0,max),r=q.rows.find(r=>r.id===active);if(!r)return;r[field]=val;for(const e of q.events)if(e.rowId===active&&e.performance)e.performance[field]=val;commit("Change MIDI mapping",q);});
$("tempo").onchange=run(async()=>{stop();const points=H.timingPoints(p()),current=points.filter(t=>t.red&&t.timeMs<=cursor).at(-1)||points.find(t=>t.red);current.value=Number($("tempo").value);const q=await desktop.editTiming(p(),points);selected.clear();commit("Change tempo",q);});


$("quantize").onclick=run(async()=>{if(!selected.size)return status("Select events first.");const d=form([{label:"Strength · %",id:"strength",value:100,min:0,max:100}]);d.append(text("p","Uses the toolbar snap target. Sample times change; gameplay objects stay in place."));if(await modal("Snap selected events",d))commit("Snap selected events",H.snapSelected(p(),[...selected],{...settings(),enabled:true},Number($("strength").value)/100));});
async function generateGameplayObjects(){
 if(gesture||exportingBeatmap||$("dialog").open)return;
 const ids=selected.size?[...selected]:null,d=form([{label:"Sounds",id:"generateScope",value:ids?"selected":"all",options:ids?[["selected","Unassigned sounds in selection"],["all","All unassigned sounds"]]:[["all","All unassigned sounds"]]},{label:"Position X",id:"generateX",value:256,min:0,max:512},{label:"Position Y",id:"generateY",value:192,min:0,max:384}]);
 d.append(text("p","Create osu! hit circles for sounds without a valid gameplay-object assignment. Simultaneous sounds share one circle and keep their individual volumes through mixed-sample export. A unique existing circle at the exact time is reused. Existing objects stay unchanged. New circles are stacked at the chosen position; this creates a hitsound donor map, not a finished gameplay pattern. Fractional timestamps round to the nearest millisecond (up to 0.5 ms). Load song audio before exporting a new beatmap. For imported maps, edit timing before generating missing circles. Undo restores the full project."));
 if(!await modal("Generate gameplay objects",d,"Generate objects"))return;
 const generationSource=p(),result=await desktop.generateObjects(generationSource,{ids:$("generateScope").value==="selected"?ids:null,x:Number($("generateX").value),y:Number($("generateY").value)});
 if(p()!==generationSource)return status("Project changed while generating objects. Retry generation to keep your latest edits.");
 if(!result.assigned)return status("No unassigned sounds to generate objects for.");
 stop();commit("Generate gameplay objects",result.project);status("Created "+result.created+" circles for "+result.assigned+" sounds"+(result.rounded?" · "+result.rounded+" timestamps rounded to whole milliseconds":"")+". Undo is available.");
}
$("generateObjects").onclick=run(generateGameplayObjects);
$("assign").onclick=run(async()=>{if(!selected.size)return status("Select events first.");const d=form([{label:"Maximum distance · ms",id:"tolerance",value:2,min:0,max:1000},{label:"Simultaneous objects",id:"ambiguity",value:"keep",options:[["keep","Keep ambiguous events unassigned"],["first","Use first nearest object in file order"]]}]);d.append(text("p","By default, only unique matches are bound. The optional first-object rule resolves simultaneous mania objects explicitly. Gameplay objects stay in place."));if(await modal("Assign to gameplay objects",d))commit("Assign events",H.assign(p(),[...selected],Number($("tolerance").value),$("ambiguity").value));});
$("repeat").onclick=run(async()=>{if(!selected.size)return status("Select events first.");const es=p().events.filter(e=>selected.has(e.id)),start=H.minValue(es.map(e=>e.timeMs)),end=H.maxValue(es.map(e=>e.timeMs+e.durationMs));const d=form([{label:"Copies",id:"copies",value:1,min:1,max:100},{label:"Offset per copy · ms",id:"copyOffset",value:Math.max(1,end-start),min:1}]);if(await modal("Repeat selected events",d))commit("Repeat events",H.duplicate(p(),[...selected],H.clamp(Number($("copies").value),1,100),Math.max(1,Number($("copyOffset").value))));});
$("rangeAction").onclick=run(async()=>{if(!range)return status("Choose the range tool and drag a time range first.");const d=form([{label:"Operation",id:"rangeOp",value:"copy",options:[["copy","Copy"],["move","Move"],["delete","Delete"]]},{label:"Offset · ms",id:"rangeOffset",value:range.end-range.start},{label:"Split crossing MIDI lengths",id:"rangeSplit",value:"no",options:[["no","No · use event start times"],["yes","Yes · split at range boundaries"]]}]);d.append(text("p","Range end is excluded. Linked gameplay objects are never moved or split."));if(await modal("Edit time range",d))commit("Range "+$("rangeOp").value,H.rangeEdit(p(),range,$("rangeOp").value,Number($("rangeOffset").value),$("rangeSplit").value==="yes"));});
$("bulk").onclick=run(async()=>{const d=form([{label:"Scope",id:"bulkScope",value:selected.size?"selection":"all",options:[["selection","Selected events"],["row","Active row"],["all","All events"]]},{label:"Minimum volume",id:"bulkMin",value:1,min:1,max:100},{label:"Maximum volume",id:"bulkMax",value:100,min:1,max:100},{label:"Action",id:"bulkAction",value:"select",options:[["select","Select matching events"],["fixed","Set volume"],["scale","Scale volume by %"],["mute","Mute"]]},{label:"Value",id:"bulkValue",value:80}]);const preview=text("p","");function match(){const scope=$("bulkScope").value==="selection"?[...selected]:p().events.filter(e=>$("bulkScope").value==="all"||e.rowId===active).map(e=>e.id);return H.bulk(p(),scope,{min:Number($("bulkMin").value),max:Number($("bulkMax").value),action:$("bulkAction").value,value:Number($("bulkValue").value)});}d.append(preview);d.oninput=()=>preview.textContent=match().ids.length+" matching events";const promise=modal("Select and transform",d);d.oninput();if(await promise){const r=match();selected=new Set(r.ids);commit("Transform "+r.ids.length+" events",r.project);}});
$("useSample").onclick=()=>{if(!active||!selected.size)return status("Select events, then choose a sample row.");const q=H.clone(p());for(const e of q.events)if(selected.has(e.id))e.rowId=active;commit("Change selected samples",q);};
async function prepareMidi(next){
 const source=next.midiSources[0],trackNames=source.raw.tracks.map((tr,i)=>[String(i),tr.find(e=>e.type==="trackName")?.text||"Track "+(i+1)]).filter(([i])=>next.events.some(e=>e.source.track===Number(i))),channels=[...new Set(next.events.map(e=>e.performance.channel))].sort((a,b)=>a-b);
 const d=form([{label:"Track",id:"midiTrack",value:"all",options:[["all","All note tracks"],...trackNames]},{label:"Channel",id:"midiChannel",value:"all",options:[["all","All channels"],...channels.map(c=>[String(c),"Channel "+c])]},{label:"Time offset · ms",id:"midiOffset",value:0}]);
 d.prepend(text("p",next.events.length+" notes · "+next.rows.length+" rows. Original tempo timing is preserved. Simultaneous notes stay separate; overlapping notes of the same pitch use FIFO pairing."));
 const preview=text("p",""),refresh=()=>preview.textContent=next.events.filter(e=>($("midiTrack").value==="all"||e.source.track===Number($("midiTrack").value))&&($("midiChannel").value==="all"||e.performance.channel===Number($("midiChannel").value))).length+" notes will be imported";d.append(preview);d.oninput=refresh;
 const waiting=modal("Import MIDI",d,"Import");refresh();if(!await waiting)return false;
 next.events=next.events.filter(e=>($("midiTrack").value==="all"||e.source.track===Number($("midiTrack").value))&&($("midiChannel").value==="all"||e.performance.channel===Number($("midiChannel").value)));const used=new Set(next.events.map(e=>e.rowId));next.rows=next.rows.filter(r=>used.has(r.id));const offset=Number($("midiOffset").value);if(next.events.some(e=>e.timeMs+offset<0))throw Error("Offset would place notes before zero.");for(const e of next.events)e.timeMs+=offset;source.offsetMs=offset;
 // Audio time is absolute. Shift tempo points together with a replacement MIDI project.
 if(offset){next.tempos=next.tempos.map(t=>({...t,timeMs:t.timeMs+offset}));if(next.timeSignatures)next.timeSignatures=next.timeSignatures.map(t=>({...t,timeMs:t.timeMs+offset}));}return true;
}

async function loadResult(result){if(!result)return;let next=H.validate(result.project);stop();if(result.kind==="midi"&&!await prepareMidi(next))return;if(SG)next=SG.prepare(next);resetSampleGroups(next);
 if(!p().demo&&(p().events.length||dirty)){const d=form([{label:"Import",id:"importMode",value:"replace",options:[["replace","Replace current project"],["append","Append as separate sample rows"]]},{label:"Time offset · ms",id:"importOffset",value:0}]);d.append(text("p","Appending keeps the current song and tempo map. Imported MIDI uses its original timing in milliseconds. Save the current project first if needed."));if(!await modal("Import "+next.name,d))return;if($("importMode").value==="append"){const offset=Number($("importOffset").value),q=H.appendProject(p(),next,offset),first=q.rows[p().rows.length]?.id;commit("Import "+next.name,q);active=first;selected.clear();fit();return;}}
 history=new H.History(next);if(next.audio?.assetKey){const key=next.audio.assetKey,projectId=p().id;getBuffer(key).then(b=>{if(p().id!==projectId||p().audio?.assetKey!==key)return;p().audio.durationMs=b.duration*1000;makeWave(key,b);update();}).catch(e=>status(e.message));}selected.clear();active=next.rows[0]?.id;range=null;dirty=false;view=0;scrollY=0;cursor=Math.min(H.minValue(next.events.map(e=>e.timeMs)),0);fit();if(result.warnings?.length)await message("Import notes",result.warnings.join("\n"));status("Imported "+next.events.length+" events · "+next.rows.length+" sample rows");}
$("open").onclick=run(async()=>loadResult(await desktop.open()));desktop.onLoaded(run(loadResult));
$("save").onclick=run(async()=>{const project=p(),snapshot=JSON.stringify(project),path=await desktop.save(project);if(path){if(p()===project&&JSON.stringify(p())===snapshot)dirty=false;status("Project saved with its loaded samples and song audio.");update();}});
$("exportMidi").onclick=run(async()=>{const file=await desktop.midi(p());if(file)status("MIDI copy exported. Output volume remains in the project.");});
let exportingBeatmap=false;
async function renderExportMixes(plan){
 const cache=new Map(),mixes=new Map(),result=[];let decoder=null,attenuated=0;
 async function decoded(key){if(!cache.has(key))cache.set(key,(async()=>{decoder??=new OfflineAudioContext(2,1,HSESampleRender.RATE);const asset=await desktop.asset(key);if(!asset)throw Error("An export sample is missing. Choose its sample file again.");const bytes=Uint8Array.from(atob(asset.data),c=>c.charCodeAt(0)),audio=await decoder.decodeAudioData(bytes.buffer);if(audio.numberOfChannels>2)throw Error("Convert "+asset.name+" to mono or stereo before mixing; multichannel audio is not discarded silently.");return {sampleRate:audio.sampleRate,channels:Array.from({length:audio.numberOfChannels},(_,i)=>audio.getChannelData(i))};})());return cache.get(key);}
 for(let i=0;i<plan.renderRequests.length;i++){const job=plan.renderRequests[i],layers=[...job.layers].sort((a,b)=>(a.assetKey+":"+a.volume).localeCompare(b.assetKey+":"+b.volume)),key=JSON.stringify(layers.map(l=>[l.assetKey,l.volume]));let mix=mixes.get(key);if(!mix){const pcm=[];for(const layer of layers)pcm.push({...await decoded(layer.assetKey),volume:layer.volume});mix=HSESampleRender.mix(pcm);mixes.set(key,mix);if(mix.headroomGain<1)attenuated++;}result.push({ownerId:job.ownerId,bytes:mix.bytes});status("Preparing export: "+(i+1)+" / "+plan.renderRequests.length+" mixed objects");}
 return {mixes:result,attenuated};
}
async function exportBeatmap(){
 if(exportingBeatmap||gesture||rowDrag||locatorDrag||$("dialog").open)return;
 exportingBeatmap=true;$("exportOsu").disabled=true;
 try{
  let snapshot=H.clone(p()),guide=null;
  const map=snapshot.beatmaps[0],objectless=snapshot.beatmaps.length<=1&&!snapshot.objects.some(o=>o.beatmapId===map?.id);
  const choice=form([{label:"Export as",id:"beatmapExportMode",value:objectless?"guide":"normal",options:[["normal","Normal beatmap"],["guide","HS diff (rhythm guide)"]]}]);
  choice.append(text("p","Normal beatmap: keep the existing gameplay objects and apply your hitsound edits."),text("p","HS diff (rhythm guide): create a separate difficulty with one circle at each hitsound time, including events that are not assigned to gameplay objects. Your project stays unchanged."));
  if(!await modal("Export beatmap",choice,"Continue"))return;
  const mode=$("beatmapExportMode").value;
  if(mode!=="normal"&&mode!=="guide")throw Error("Choose an export type.");
  if(mode==="normal"&&objectless){await message("Normal beatmap export","This project has no gameplay objects. Choose HS diff (rhythm guide) to create a difficulty from its hitsound events.");return;}
  if(mode==="guide"){
   if(!snapshot.events.length){await message("Export rhythm guide","Add hitsound events before exporting a rhythm guide.");return;}
   if(!snapshot.audio?.assetKey){await message("Export rhythm guide","Load song audio using the Song audio panel, then export again. The guide archive includes the song and its hitsounds.");return;}
   const times=[...new Set(snapshot.events.map(e=>Math.round(e.timeMs)))],rounded=snapshot.events.filter(e=>Math.round(e.timeMs)!==e.timeMs).length;
   const d=form([{label:"Difficulty name",id:"guideName",type:"text",value:"HS"},{label:"New combo each circle",id:"guideCombo",value:"yes",options:[["yes","Yes"],["no","No"]]},{label:"Position X",id:"guideX",value:256,min:0,max:512},{label:"Position Y",id:"guideY",value:192,min:0,max:384}]);
   d.prepend(text("p",times.length+" circles from "+snapshot.events.length+" sounds · "+H.minValue(times)+"–"+H.maxValue(times)+" ms"+(rounded?" · "+rounded+" fractional timestamps rounded to whole milliseconds":"")));
   d.append(text("p","Export an osu! standard rhythm-guide difficulty with one circle at each hitsound time. Simultaneous layers share a circle and retain their individual volumes through mixed samples. Circles use the chosen position. Times are not snapped to the grid; only fractional milliseconds are rounded (up to 0.5 ms). This creates an export copy and leaves your project unchanged."));
   if(!await modal("Export rhythm guide",d,"Continue to export"))return;
   guide=await desktop.rhythmGuide(snapshot,{name:$("guideName").value,newCombo:$("guideCombo").value==="yes",x:Number($("guideX").value),y:Number($("guideY").value)});snapshot=guide.project;
  }
  const plan=await desktop.planOsu(snapshot);
  if(plan.conflicts?.length){await message("Beatmap export needs attention",plan.conflicts.slice(0,25).join("\n"));return;}
  status(plan.renderRequests.length?"Rendering mixed hitsounds…":"Preparing beatmap export…");
  const rendered=await renderExportMixes(plan),r=await desktop.osu(snapshot,rendered.mixes);
  if(r.conflicts?.length)await message("Beatmap export needs attention",r.conflicts.slice(0,25).join("\n")+(r.conflicts.length>25?"\n\n"+(r.conflicts.length-25)+" more conflicts.":""));
  else if(r.path){status(guide?"Rhythm guide exported · "+guide.created+" circles. Your project is unchanged.":"Beatmap exported"+(r.generatedSamples?" · "+r.generatedSamples+" generated samples for "+r.mixedObjects+" objects":"")+". Existing gameplay geometry preserved.");if(rendered.attenuated)await message("Mixed hitsound headroom",rendered.attenuated+" unique mixes exceeded full-scale audio. Their overall level was reduced uniformly to prevent clipping; the relative layer volumes were preserved. Your project volumes are unchanged.");}
 }finally{exportingBeatmap=false;$("exportOsu").disabled=false;}
}
$("exportOsu").onclick=run(exportBeatmap);
$("screenshot").onclick=run(async()=>{await desktop.capture();status("Image saved.");});
$("newProject").onclick=run(async()=>{if((dirty||p().events.length)&&!await modal("New project","Start an empty project? Save the current project first to keep your work.","New project"))return;history=new H.History(SG?SG.defaultProject():H.project());resetSampleGroups(history.project);selected.clear();active=history.project.rows[0]?.id||null;dirty=false;stop();view=cursor=scrollY=0;range=null;update();});
$("addSamples").onclick=run(async()=>{const samples=await desktop.samples();if(!samples.length)return;const q=H.clone(p());for(const s of samples)q.rows.push({id:H.id("sample"),...s,note:35,channel:10,muted:false,solo:false});active=q.rows.at(-1).id;commit("Add samples",q);});
$("replaceSample").onclick=run(async()=>{const row=activeRow();if(!row)return;const samples=await desktop.samples();if(!samples.length)return;const q=H.clone(p()),r=q.rows.find(r=>r.id===active);if(SG?.identity(r)){r.sampleMapping=SG.identity(r);r.assetKey=samples[0].assetKey;r.sourceSampleName=samples[0].name;r.name=SG.filename(r.sampleMapping.set,r.sampleMapping.kind,r.sampleMapping.index).replace(/\.wav$/i,/\.(wav|ogg|mp3)$/i.exec(samples[0].name)?.[0]||".wav");}else {Object.assign(r,samples[0]);if(SG?.identity(r))r.sampleMapping=SG.identity(r);}commit("Change sample",q);await audition(r.id,80);});
$("loadAudio").onclick=run(async()=>{const a=await desktop.audio();if(!a)return;const buffer=await getBuffer(a.assetKey);Object.assign(a,H.audioSettings(p().audio));a.durationMs=buffer.duration*1000;makeWave(a.assetKey,buffer);const q=H.clone(p());q.audio=a;commit("Load song audio",q);});
function context(){if(!audioContext){audioContext=new AudioContext({latencyHint:outputSettings.latencyHint,sampleRate:outputSettings.sampleRate});masterGain=audioContext.createGain();masterGain.gain.value=masterVolume/100;masterGain.connect(audioContext.destination);}audioContext.resume();return audioContext;}
$("masterVolume").oninput=()=>{masterVolume=H.clamp(Number($("masterVolume").value)||0,0,100);$("masterVolumeValue").textContent=masterVolume+"%";if(masterGain){const now=audioContext.currentTime;masterGain.gain.cancelScheduledValues(now);masterGain.gain.setTargetAtTime(masterVolume/100,now,.01);}};
function makeWave(key,buffer){if(!waveforms.has(key))waveforms.set(key,HSEWaveform.build(buffer));render();}

async function getBuffer(key){
 await ensureAudioOutput();if(buffers.has(key))return buffers.get(key);if(bufferLoads.has(key))return bufferLoads.get(key);
 const generation=audioGeneration,ctx=context(),pending=(async()=>{const data=await desktop.asset(key);if(!data)throw Error("Sample data is missing. Choose the sample file again.");const raw=Uint8Array.from(atob(data.data),ch=>ch.charCodeAt(0)),buffer=await ctx.decodeAudioData(raw.buffer);if(generation===audioGeneration){buffers.set(key,buffer);if(p().audio?.assetKey===key)makeWave(key,buffer);}return buffer;})();bufferLoads.set(key,pending);
 try{return await pending;}finally{if(bufferLoads.get(key)===pending)bufferLoads.delete(key);}
}
function emit(row,volume,when,buffer){const ctx=context(),gain=ctx.createGain();gain.gain.value=volume/100;gain.connect(masterGain);let source;
 if(buffer){source=ctx.createBufferSource();source.buffer=buffer;source.connect(gain);source.start(when);}
 else if($("demoTones").checked){source=ctx.createOscillator();source.type="triangle";source.frequency.value=80+row.note*8;gain.gain.setValueAtTime(volume/100*.18,when);gain.gain.exponentialRampToValueAtTime(.001,when+.09);source.connect(gain);source.start(when);source.stop(when+.1);}
 if(source){voices.add(source);source.onended=()=>{voices.delete(source);gain.disconnect();};}}
async function audition(rowId,volume,event){const row=p().rows.find(r=>r.id===rowId);if(!row)return;try{await ensureAudioOutput();const key=rowSampleAsset(row,event),buffer=key?await getBuffer(key):null;emit(row,volume,context().currentTime+.01,buffer);if(!buffer)status("Preview tone · no sample file assigned");}catch(e){status("Sample playback failed: "+e.message);}}
let playbackRate=1;
$("playbackSpeed").onchange=()=>{const next=Number($("playbackSpeed").value);if(![1,.75,.5,.25].includes(next))return;const position=playing?transportNow():cursor;playbackRate=next;if(playing){cursor=position;cancelVoices();transport={...transport,start:cursor,clock:context().currentTime,rate:next};scheduledUntil=0;songScheduledThrough=0;transportTick?.();}status("Playback speed · "+Math.round(next*100)+"%");};
function transportNow(){return transport?H.transportPosition(transport.start,Math.max(0,(context().currentTime-transport.clock)*1000*transport.rate),transport.cycle,H.duration(p())):cursor;}
function cancelVoices(){for(const v of voices)try{v.stop();}catch{}voices.clear();stopSongVoice();}
function stop(returnToStart=false){playbackRequest++;preparingPlayback=false;const wasPlaying=playing,back=transport?.returnMs;if(wasPlaying)cursor=transportNow();playing=false;clearInterval(scheduler);cancelVoices();if(returnToStart&&wasPlaying&&$("returnStart").checked&&back!=null){cursor=back;if(xAt(cursor)<0||xAt(cursor)>$("grid").clientWidth){view=Math.max(0,cursor-$("grid").clientWidth/scale*.15);$("horizontal").value=view;}}$("play").classList.remove("on");render();}
function refreshTransport(){
 if(!playing||!transport)return;
 const c=H.cycleSettings(p());if(JSON.stringify(c)===JSON.stringify(transport.cycle))return;
 cursor=transportNow();cancelVoices();transport={start:cursor,clock:context().currentTime,cycle:c,rate:playbackRate,returnMs:transport.returnMs};playingOrigin=transport.clock-cursor/1000;scheduledUntil=0;songScheduledThrough=0;transportTick?.();
}
function selectionBounds(){if(range&&range.start!==range.end)return [Math.min(range.start,range.end),Math.max(range.start,range.end)];const es=p().events.filter(e=>selected.has(e.id));return es.length?[H.minValue(es.map(e=>e.timeMs)),H.maxValue(es.map(e=>e.timeMs+Math.max(1,e.durationMs)))]:null;}
async function play(){
 if(playing||preparingPlayback){stop(true);return;}const request=++playbackRequest;preparingPlayback=true;try{await ensureAudioOutput();if(request!==playbackRequest)return;const ctx=context(),missing=p().rows.filter(r=>!rowSampleAsset(r)).length;status("Preparing audio…");const keys=new Set(p().rows.map(r=>rowSampleAsset(r)).filter(Boolean));for(const e of p().events){const key=rowSampleAsset(p().rows.find(r=>r.id===e.rowId),e);if(key)keys.add(key);}for(const key of keys)await getBuffer(key);const song=p().audio?.assetKey?await getBuffer(p().audio.assetKey):null;if(song)p().audio.durationMs=song.duration*1000;
 if(request!==playbackRequest)return;const mode=$("startMode").value,bounds=selectionBounds(),c=H.cycleSettings(p());
 if(mode==="cycle"||(mode==="selectionCycle"&&!bounds))cursor=c.leftMs;else if((mode==="selection"||mode==="selectionCycle")&&bounds)cursor=bounds[0];
 playing=true;transport={start:cursor,clock:ctx.currentTime+.05,cycle:c,rate:playbackRate,returnMs:cursor};playingOrigin=transport.clock-cursor/1000;scheduledUntil=0;songScheduledThrough=0;$("play").classList.add("on");
 lastSongAsset=p().audio?.assetKey||null;lastSongOffset=H.audioSettings(p().audio).offsetMs;
 transportTick=function(){
  if(!playing)return;
  const now=Math.max(0,(ctx.currentTime-transport.clock)*1000*transport.rate),end=H.duration(p()),horizon=now+140*transport.rate;
  const rows=new Map(p().rows.map(r=>[r.id,r])),hasSolo=p().rows.some(r=>r.solo);
  for(const slice of H.transportSlices(transport.start,Math.max(scheduledUntil,now-30*transport.rate),horizon,transport.cycle,end)){
   for(const e of p().events){if(e.timeMs<slice.fromMs||e.timeMs>=slice.toMs||e.muted)continue;const r=rows.get(e.rowId);if(!r||r.muted||(hasSolo&&!r.solo))continue;
    emit(r,e.volume,Math.max(ctx.currentTime,transport.clock+(slice.fromElapsed+e.timeMs-slice.fromMs)/(1000*transport.rate)),buffers.get(rowSampleAsset(r,e))||null);
   }
  }scheduledUntil=horizon;
  scheduleSongAhead(Math.max(0,now),horizon);
  cursor=transportNow();const leg=H.transportLeg(transport.start,now,transport.cycle,end);
  if(now>=leg.elapsedEnd&&leg.endMs===end){stop(true);return;}
  if($("follow").checked&&(xAt(cursor)>$("grid").clientWidth*.85||xAt(cursor)<0)){view=Math.max(0,cursor-$("grid").clientWidth/scale*.15);$("horizontal").value=view;}
  render();
 };
 scheduler=setInterval(transportTick,25);transportTick();status(missing?missing+" rows have no sample file":"Playing samples");
 }catch(e){if(request===playbackRequest)throw e;}finally{if(request===playbackRequest)preparingPlayback=false;}
}
$("play").onclick=run(play);$("stop").onclick=()=>{stop(true);update();};$("toStart").onclick=()=>{stop();cursor=view=0;update();};
$("loop").onclick=()=>commit("Toggle cycle",H.setCycle(p(),{enabled:!H.cycleSettings(p()).enabled}));
function audioPreferencesBody(initial){
 const settings={...defaultAudioSettings,...initial},d=text("section","","audioPreferences"),fields={};let devices=[],loaded=false;
 function select(key,label,options){const line=text("label",label),el=document.createElement("select");if(!options.some(o=>String(o[0])===String(settings[key])))options.push([settings[key],String(settings[key])]);for(const [value,name] of options){const o=text("option",name);o.value=String(value);el.append(o);}el.value=String(settings[key]);el.setAttribute("aria-label",label);fields[key]=el;line.append(el);d.append(line);return el;}
 const backend=select("backend","Audio system",[["browser","Browser / Windows default"],["wasapi-shared","WASAPI · shared"],["wasapi-exclusive","WASAPI · exclusive"],["asio","ASIO"],["directsound","DirectSound"],["mme","MME / WaveOut"]]),device=select("deviceId","Output device",[[settings.deviceId,settings.deviceId||"System default"]]);
 select("sampleRate","Sample rate",[[44100,"44,100 Hz"],[48000,"48,000 Hz"],[88200,"88,200 Hz"],[96000,"96,000 Hz"]]);
 select("bridgeFrames","Editor buffer",[[256,"256 samples"],[512,"512 samples"],[1024,"1024 samples"],[2048,"2048 samples"],[4096,"4096 samples"]]);
 select("latencyMs","Requested native latency",[[10,"10 ms"],[20,"20 ms"],[30,"30 ms"],[50,"50 ms"],[100,"100 ms"],[200,"200 ms"],[500,"500 ms"]]);
 select("latencyHint","Browser latency mode",[["interactive","Interactive"],["balanced","Balanced"],["playback","Playback"]]);
 const offsetLabel=text("label","First ASIO output (stereo pair)"),offset=document.createElement("input");offset.type="number";offset.min=1;offset.max=127;offset.step=1;offset.value=settings.channelOffset+1;offset.setAttribute("aria-label","First ASIO output channel");offsetLabel.append(offset);d.append(offsetLabel);
 const hint=text("p","","hint"),buttons=text("div","","audioButtons"),refresh=text("button","Refresh devices"),panel=text("button","ASIO control panel…"),stats=text("button","Refresh status"),test=text("button","Test current output"),info=text("pre","","audioStatus");for(const b of [refresh,panel,stats,test])b.type="button";buttons.append(refresh,panel,stats,test);d.append(hint,buttons,info);
 function sync(){const api=backend.value==="wasapi-shared"||backend.value==="wasapi-exclusive"?"wasapi":backend.value;device.disabled=api==="browser";fields.bridgeFrames.disabled=api==="browser";fields.latencyMs.disabled=api==="browser"||api==="asio";fields.latencyHint.disabled=api!=="browser";offset.disabled=api!=="asio";panel.disabled=api!=="asio"||outputSettings.backend!=="asio";panel.title="Save ASIO settings first, then reopen Audio preferences to open the driver panel.";
 hint.textContent=api==="browser"?"Uses Windows' default output. Sample rate and browser latency mode apply when saved.":api==="asio"?"ASIO hardware buffer size is controlled by the driver's panel. The editor bridge adds buffering; this is separate from the driver buffer. Close the panel and save/reapply settings after driver changes.":backend.value==="wasapi-exclusive"?"Exclusive mode reserves this device and may prevent other apps from playing through it. The editor bridge adds buffering in addition to the requested native latency.":"Editor buffering adds to native/device latency. Increase buffers if status reports underruns. All playback follows this output, including song audio and previews.";
 if(loaded){const previous=device.value;device.replaceChildren();const options=[["",api==="asio"?"Select an ASIO driver":"System default"],...devices.filter(v=>v.api===api).map(v=>[v.id,v.name])];for(const [id,name] of options){const option=text("option",name);option.value=id;device.append(option);}device.value=options.some(v=>v[0]===previous)?previous:"";}}
 backend.onchange=()=>{device.value="";sync();};
 refresh.onclick=run(async()=>{const result=await desktop.audioDevices();devices=result.devices;loaded=true;sync();info.textContent=result.errors?.length?result.errors.join("\n"):devices.length+" output devices/drivers found. Installed ASIO drivers may be disconnected.";});
 stats.onclick=run(async()=>{const value=await desktop.audioStats();info.textContent=value.backend==="browser"||value.backend==="none"?"Native output is not running.":"Active: "+value.backend+" · "+value.sampleRate+" Hz\nEditor buffer: "+value.bridgeFrames+" samples"+(value.driverFrames?" · ASIO buffer: "+value.driverFrames+" samples":"")+"\nQueued: "+value.queuedFrames+" frames · Underruns: "+value.underruns+" · Overflows: "+value.overflows+(value.error?"\n"+value.error:"");});
 test.onclick=run(async()=>{await ensureAudioOutput();const buffer=await getBuffer("osu-default:normal-hitnormal.wav");emit({note:35},50,context().currentTime+.02,buffer);info.textContent="Test sound sent to the currently applied output. Save preferences to apply edited device settings.";});
 panel.onclick=run(async()=>{if(nativeBridge)nativeBridge.suspended=true;try{await desktop.audioControlPanel();audioNeedsReapply=true;info.textContent="Driver panel closed. Save/reapply Audio preferences to use driver changes.";}finally{if(nativeBridge)nativeBridge.suspended=false;}});sync();
 return {element:d,refresh:()=>refresh.onclick(),value:()=>({backend:backend.value,deviceId:device.value,sampleRate:Number(fields.sampleRate.value),latencyMs:Number(fields.latencyMs.value),bridgeFrames:Number(fields.bridgeFrames.value),channelOffset:Number(offset.value)-1,latencyHint:fields.latencyHint.value})};
}
function preferencesBody(initial){
 const d=text("div","","preferencesBody"),tabs=text("div","","preferencesTabs"),general=text("button","General","on"),audio=text("button","Audio");general.type=audio.type="button";general.setAttribute("role","tab");general.setAttribute("aria-selected","true");audio.setAttribute("role","tab");audio.setAttribute("aria-selected","false");tabs.setAttribute("role","tablist");tabs.append(general,audio);
 const panel=text("section","","preferencesPanel"),label=text("label","osu! Songs folder"),folder=document.createElement("input");folder.type="text";folder.readOnly=true;folder.value=initial.songsFolder||"";folder.setAttribute("aria-label","osu! Songs folder");label.append(folder);
 const browse=text("button","Choose folder…"),clear=text("button","Clear");browse.type=clear.type="button";
 browse.onclick=run(async()=>{const picked=await desktop.pickSongsFolder(folder.value);if(picked)folder.value=picked;});clear.onclick=()=>folder.value="";
 panel.append(label,browse,clear,text("p","Open starts in this folder. The preference is saved on this computer; beatmap files are unchanged.","hint"));const audioPanel=audioPreferencesBody(initial.audio);audioPanel.element.hidden=true;
 function tab(showAudio){panel.hidden=showAudio;audioPanel.element.hidden=!showAudio;general.classList.toggle("on",!showAudio);audio.classList.toggle("on",showAudio);general.setAttribute("aria-selected",String(!showAudio));audio.setAttribute("aria-selected",String(showAudio));}general.onclick=()=>tab(false);audio.onclick=()=>{tab(true);audioPanel.refresh();};
 d.append(tabs,panel,audioPanel.element);return {element:d,value:()=>({songsFolder:folder.value,audio:audioPanel.value()})};
}
$("preferences").onclick=run(async()=>{await audioPreferencesReady;const initial=await desktop.preferences(),body=preferencesBody(initial);if(await modal("Preferences",body.element,"Save preferences")){const next=body.value();if(audioNeedsReapply||JSON.stringify(next.audio)!==JSON.stringify(outputSettings))await applyAudioSettings(next.audio);await desktop.savePreferences(next);status("Preferences saved");}});
function timingEditor(){
 const d=text("div","","timingEditor"),entries=[],osu=!!p().beatmaps.length;
 d.append(text("p",osu?"Red points set BPM and beat count; green points set slider velocity. Offsets use milliseconds. Changes keep gameplay start times fixed, update inherited sounds and slider edges, and support Undo. Explicit per-object samples and volumes remain explicit.":"Edit tempo changes without moving sound events. Inherited points are available for imported osu! beatmaps. MIDI exports the tempo map; osu! sample settings and effects require a beatmap."));
 const filter=document.createElement("input");filter.type="text";filter.placeholder="Filter offsets or point types…";filter.setAttribute("aria-label","Filter timing points");d.append(filter);
 const table=text("div","","timingTable"),head=text("div","","timingRow timingHead");for(const label of ["Type","Offset · ms","BPM / SV ×","Beats","Sample set","Index","Volume %","Kiai","Omit bar",""])head.append(text("span",label));table.append(head);d.append(table);
 function field(type,value,min,max){const e=document.createElement("input");e.type=type;e.value=value;if(min!==undefined)e.min=min;if(max!==undefined)e.max=max;e.step="any";return e;}
 function choice(options,value){const e=document.createElement("select");for(const [v,label] of options){const o=text("option",label);o.value=v;e.append(o);}e.value=String(value);return e;}
 function add(point){const line=text("div","","timingRow"),type=choice([["red","Red · tempo"],["green","Green · inherited"]],point.red?"red":"green"),time=field("number",point.timeMs),value=field("number",point.value,.1,6000),meter=field("number",point.meter,1,255),set=choice([[0,"Default"],[1,"Normal"],[2,"Soft"],[3,"Drum"]],point.set),index=field("number",point.index,0,100000),volume=field("number",point.volume,0,100),kiai=field("checkbox",""),omit=field("checkbox",""),actions=text("div","","timingActions");kiai.checked=!!(point.effects&1);omit.checked=!!(point.effects&8);type.disabled=!osu;
 const entry={line,type,time,value,meter,set,index,volume,kiai,omit,point};entries.push(entry);
 const jump=text("button","Go"),copy=text("button","+"),remove=text("button","×");jump.title="Move playhead to this timing point";copy.title="Duplicate timing point at the playhead";remove.title="Remove timing point";for(const b of [jump,copy,remove])b.type="button";
 jump.onclick=()=>{const t=Number(time.value);if(Number.isFinite(t)){cursor=t;view=Math.max(0,t-500/scale);render();}};copy.onclick=()=>add({...read(entry),timeMs:cursor});remove.onclick=()=>{entries.splice(entries.indexOf(entry),1);line.remove();};actions.append(jump,copy,remove);
 function sync(){const red=type.value==="red";line.dataset.type=red?"red":"green";meter.disabled=!red;value.min=red?.001:.1;value.max=red?6000:10;value.title=red?"Beats per minute":"Slider velocity multiplier (0.1–10)";for(const e of [set,index,volume,kiai,omit])e.disabled=!osu;}
 type.onchange=()=>{value.value=type.value==="red"?120:1;sync();};sync();line.append(type,time,value,meter,set,index,volume,kiai,omit,actions);for(const [e,label] of [[type,"Point type"],[time,"Offset in milliseconds"],[value,"BPM or slider velocity"],[meter,"Beats per bar"],[set,"Sample set"],[index,"Sample index"],[volume,"Timing point volume"],[kiai,"Kiai time"],[omit,"Omit first barline"]])e.setAttribute("aria-label",label);table.append(line);return entry;
 }
 function read(e){for(const input of [e.time,e.value,e.meter,e.index,e.volume])if(!String(input.value).trim())throw Error("Fill in every timing value.");return {...e.point,timeMs:Number(e.time.value),red:e.type.value==="red",value:Number(e.value.value),meter:Number(e.meter.value),set:Number(e.set.value),index:Number(e.index.value),volume:Number(e.volume.value),effects:(e.point.effects&~9)|(e.kiai.checked?1:0)|(e.omit.checked?8:0)};}
 H.timingPoints(p()).forEach(add);filter.oninput=()=>{const query=filter.value.toLowerCase();for(const e of entries)e.line.hidden=!(e.time.value+" "+e.type.value).toLowerCase().includes(query);};
 const buttons=text("div","","timingButtons");for(const red of [true,false]){if(!red&&!osu)continue;const b=text("button",red?"Add red point at playhead":"Add green point at playhead");b.type="button";b.onclick=()=>{const points=H.timingPoints(p()),base=points.filter(t=>t.timeMs<=cursor).at(-1)||points[0];add({...base,timeMs:cursor,red,value:red?(points.filter(t=>t.red&&t.timeMs<=cursor).at(-1)?.value||120):1,rawBeat:undefined});};buttons.append(b);}d.append(buttons);
 return {element:d,value:()=>H.validateTimingPoints(entries.map(read))};
}
$("timingPoints").onclick=run(async()=>{const editor=timingEditor();if(await modal("Timing points",editor.element,"Apply timing")){const q=await desktop.editTiming(p(),editor.value());selected.clear();commit("Edit timing points",q);}});
function signatureEditor(){
 const d=text("div","","signatureEditor"),rows=text("div","","signatureRows"),entries=[];
 d.append(text("p","Set time-signature changes in milliseconds. Bar lines and beat labels follow these changes; event timing and osu! snap divisions stay unchanged."),rows);
 const headings=text("div","","signatureRow");for(const label of ["Time · ms","Beats","Beat unit",""])headings.append(text("span",label));rows.append(headings);
 function add(point){const line=text("div","","signatureRow"),time=document.createElement("input"),numerator=document.createElement("input"),denominator=document.createElement("select"),remove=text("button","Remove");time.type=numerator.type="number";time.step="any";time.value=point.timeMs;numerator.min=1;numerator.max=255;numerator.step=1;numerator.value=point.numerator;
  time.setAttribute("aria-label","Time-signature position in milliseconds");numerator.setAttribute("aria-label","Beats per bar");denominator.setAttribute("aria-label","Beat unit");
  for(const n of [1,2,4,8,16,32,64,128]){const option=text("option",String(n));option.value=String(n);denominator.append(option);}denominator.value=point.denominator;remove.type="button";
  const entry={line,time,numerator,denominator,point};entries.push(entry);remove.onclick=()=>{entries.splice(entries.indexOf(entry),1);line.remove();};line.append(time,numerator,denominator,remove);rows.append(line);
 }
 H.signatureMap(p()).forEach(add);const addButton=text("button","Add change at playhead");addButton.type="button";addButton.onclick=()=>{const current=H.beatPosition(p(),H.timeToBeat(cursor,p().tempos));add({timeMs:cursor,numerator:current.numerator,denominator:current.denominator});};d.append(addButton);
 return {element:d,value:()=>{if(!entries.length)throw Error("Keep at least one time signature.");return entries.map(e=>{if(!String(e.time.value).trim()||!String(e.numerator.value).trim())throw Error("Fill in each time and beat count.");return {...e.point,timeMs:Number(e.time.value),numerator:Number(e.numerator.value),denominator:Number(e.denominator.value)};});}};
}
$("timeSignature").onclick=run(async()=>{const editor=signatureEditor();if(await modal("Time signatures",editor.element))commit("Edit time signatures",H.setSignatures(p(),editor.value()));});

$("help").onclick=run(async()=>{const d=typeof HSEShortcuts!=="undefined"?HSEShortcuts.overview(document):document.createElement("div"),guide=document.createElement("details");guide.className="shortcutGuide";guide.append(text("summary","Editor guide"));guide.append(text("p","Open MIDI, .osu, .osz or a saved editor project. MIDI note mappings appear as sample rows; choose audio files in the inspector."));const list=document.createElement("ul");for(const s of ["1 Select · 2 Range · 3 Draw · 4 Erase · 5 Mute · 6 Ramp · 7 Audition","Draw: start in empty space to insert immediately and drag to repeat on the grid. Start on an existing diamond to delete immediately and drag to erase more sounds in that row. The stroke stays in its initial insert or erase mode until release. Snap off inserts one freely placed sound per stroke. Select: double-click empty space to insert a sound. Right-click a diamond to delete only that sound.","Ruler, waveform and empty-grid clicks place the playhead on the nearest grid line. Return on stop applies to Space, Play and Stop using the exact position at which playback began.","Hold Alt for temporary Draw with a pen cursor; release Alt to restore your chosen tool.","Ctrl+C copy · Ctrl+X cut · Ctrl+V paste at cursor. Select events first, then click the ruler to choose the paste position.","Ctrl+A select all · Ctrl+Shift+A deselect · Ctrl+D duplicate · Ctrl+R repeat · Delete remove","Ctrl+Up/Down moves selected sounds one sample row; Ctrl+Left/Right moves the group to the previous/next grid line. The earliest selected sound anchors horizontal movement, preserving group timing offsets.","Ctrl+Z undo · Ctrl+Y or Ctrl+Shift+Z redo · Ctrl+O open · Ctrl+S save · Ctrl+N new project","Ctrl+F filter samples · Q snap selected · J snapping · L loop · Space play / pause · F1 help","Ctrl+wheel horizontal zoom · Ctrl+Shift+wheel vertical zoom · Shift+wheel move timeline · wheel scroll rows. Alt+wheel changes beat snap; hover the beat-snap selector to change it with the wheel alone. Row height in the inspector offers the same vertical zoom. Diamonds scale with zoom; chart timing and volume do not change.","With Select, double-click a lane or an existing diamond to hear its assigned sample at its volume; double-clicking a lane also opens its display-name field. Double-clicking a gameplay object previews its assigned layers. Double-click a lane to rename its display label; sample filenames and export mappings stay unchanged. Use + Lane to add an empty mapped lane and − Lane to delete the active lane and its hitsounds. Undo restores deleted lanes. Drag a row grip or use Row up/down to reorder lanes.","Cycle: drag L/R flags, drag the upper ruler to draw a range, or drag its band to move it. Click the band or press Num / to toggle. Ctrl-click ruler sets L; Alt-click sets R. P sets locators to selection. Num 1/2 jumps; Ctrl+Num 1/2 sets from cursor. Reverse L/R for skip playback.","Use the arrow beside Controller to collapse or reopen the lower panel without changing chart data. Click a row to make it active. Its full volume lane stays visible for normal selections. Ctrl+A additionally displays every selected sound across all rows; choosing another row restores the single-row view. Click another bar to edit that sound; dragging a selected bar edits the selected sounds in this row. Alt-drag paints values across the row. Shift-drag creates a ramp on selected sounds in the row, or the full row when none are selected.","Drag an event edge with Show event lengths enabled to resize it. Each drag is one undo step.","Output volume and MIDI velocity stay independent. MIDI export preserves controllers and metadata from imported files.","Generate objects beside Assign to object explicitly creates gameplay circles for unassigned sounds, including MIDI-only projects. Simultaneous sounds share one circle. Load song audio before exporting a new beatmap; for imported maps set timing before generation. This is undoable. Global preview volume starts at 20% and does not affect export volumes.","Export beatmap asks whether to keep the existing gameplay objects or create a separate HS diff rhythm guide from all hitsound events. It mixes circle layers into generated audio when their volumes or sample combinations cannot share one object setting. Duplicate mixes reuse a file. Slider-edge rendering and unassigned sounds are still blocked with an explanation.","Playback speed in the transport offers 100%, 75%, 50% and 25%. Song audio slows with a lower pitch; hitsound samples retain their normal pitch. Changing speed preserves the playhead position and return-on-stop start. This does not change chart timing or exports.","Song audio has its own volume and offset controls. Positive offset plays the backing later; negative plays it earlier. These playback settings save with the project without moving sounds or changing exported audio.","Timing points in the header opens red/green point editing. Circle hitsound mixing is available during beatmap export. VST instruments and slider-edge rendering are not implemented."])list.append(text("li",s));guide.append(list);d.append(guide);await message("Shortcuts and controls",d);});
let songGain=null,lastSongVolume=null,lastSongAsset=null,lastSongOffset=null;
function stopSongVoice(){for(const old of songVoices){old.onended=null;try{old.stop();}catch{}old.disconnect();}songVoices.clear();songVoice=null;}
function applySongVolume(value){value=H.songPreviewVolume(p(),value);if(!songGain||lastSongVolume===value)return;const now=context().currentTime;songGain.gain.cancelScheduledValues(now);songGain.gain.setTargetAtTime(value/100,now,.01);lastSongVolume=value;}
function scheduleSongAhead(now,horizon){
 const buffer=buffers.get(p().audio?.assetKey);if(!buffer||!transport)return;
 let at=Math.max(now,songScheduledThrough);
 while(at<horizon){
  const leg=H.transportLeg(transport.start,at,transport.cycle,H.duration(p()));if(leg.elapsedEnd<=at)break;
  const timeline=leg.startMs+at-leg.elapsedStart,settings=H.audioSettings(p().audio),plan=H.songPlayback(p().audio,timeline,buffer.duration*1000),ctx=context();
  if(plan){const length=Math.min((leg.elapsedEnd-at)/1000-plan.delaySeconds,buffer.duration-plan.sourceSeconds);
   if(length>0){if(!songGain){songGain=ctx.createGain();songGain.connect(masterGain);songGain.gain.value=H.songPreviewVolume(p(),settings.volume)/100;lastSongVolume=H.songPreviewVolume(p(),settings.volume);}else applySongVolume(settings.volume);
    const voice=ctx.createBufferSource();voice.buffer=buffer;voice.playbackRate.value=transport.rate;voice.connect(songGain);songVoice=voice;songVoices.add(voice);voice.onended=()=>{songVoices.delete(voice);voice.disconnect();if(songVoice===voice)songVoice=null;};
    voice.start(Math.max(ctx.currentTime,transport.clock+(at/1000+plan.delaySeconds)/transport.rate),plan.sourceSeconds,length);
   }
  }at=leg.elapsedEnd;songScheduledThrough=at;
 }
}
function scheduleSong(buffer,timelineMs){stopSongVoice();lastSongAsset=p().audio?.assetKey||null;lastSongOffset=H.audioSettings(p().audio).offsetMs;const now=Math.max(0,(context().currentTime-transport.clock)*1000*transport.rate);songScheduledThrough=now;scheduleSongAhead(now,now+140*transport.rate);}
function syncSongControls(){
 const settings=H.audioSettings(p().audio),loaded=!!p().audio;
 $("songVolume").disabled=!loaded;$("songOffset").disabled=!loaded;$("songMute").disabled=!loaded;$("songMute").classList.toggle("muted",!!p().audio?.muted);$("songMute").setAttribute("aria-pressed",String(!!p().audio?.muted));
 $("songVolume").value=settings.volume;$("songVolumeValue").textContent=settings.volume+"%";$("songOffset").value=settings.offsetMs;
 applySongVolume(settings.volume);
 if(playing&&(lastSongAsset!==(p().audio?.assetKey||null)||lastSongOffset!==settings.offsetMs))scheduleSong(buffers.get(p().audio?.assetKey),Math.max(0,(context().currentTime-playingOrigin)*1000));
}
$("songMute").onclick=()=>{if(!p().audio)return;commit("Mute song audio",H.setAudioSettings(p(),{muted:!p().audio.muted}));};
$("songVolume").oninput=()=>{
 const value=H.clamp(Number($("songVolume").value),0,100);$("songVolumeValue").textContent=value+"%";applySongVolume(value);
};
$("songVolume").onchange=run(()=>{
 if(!p().audio)return;commit("Change song volume",H.setAudioSettings(p(),{volume:Number($("songVolume").value)}));
});
$("songVolume").onpointercancel=()=>syncSongControls();
$("songOffset").onchange=run(()=>{
 if(!p().audio)return;const raw=$("songOffset").value.trim(),value=Number(raw);
 if(!raw||!Number.isFinite(value)||Math.abs(value)>86400000){syncSongControls();return status("Enter a song offset in milliseconds, within 24 hours.");}
 commit("Change song offset",H.setAudioSettings(p(),{offsetMs:Math.round(value)}));
});
$("songOffset").onkeydown=ev=>{if(ev.key==="Enter"){ev.preventDefault();$("songOffset").blur();focusEditor();}};

function focusEditor(){ $("grid").focus({preventScroll:true}); }
function textEditing(){
 const el=document.activeElement;
 return !!el&&(el.isContentEditable||["TEXTAREA","SELECT"].includes(el.tagName)||(el.tagName==="INPUT"&&!["checkbox","radio","range","button","submit"].includes(el.type)));
}
function copyEvents(cut=false){
 const events=p().events.filter(e=>selected.has(e.id));if(!events.length){status("Select events first.");return false;}
 clipboard={projectId:p().id,events:H.clone(events),start:H.minValue(events.map(e=>e.timeMs))};
 if(cut){const ids=events.map(e=>e.id);selected.clear();commit("Cut "+ids.length+" events",H.remove(p(),ids));}
 else {status("Copied "+events.length+" events");updateEditCommands();}
 return true;
}
function pasteEvents(){
 if(!clipboard||clipboard.projectId!==p().id){status("Copy events from this project first.");return;}
 const q=H.clone(p()),ids=[];for(const old of clipboard.events){const e={...H.clone(old),id:H.id("paste"),timeMs:old.timeMs+Math.max(0,cursor)-clipboard.start};Object.assign(e,H.resolveOwner(q,e.timeMs));q.events.push(e);ids.push(e.id);}
 selected=new Set(ids);commit("Paste "+ids.length+" events",q);
}
function duplicateEvents(){
 if(!selected.size)return status("Select events first.");const before=p(),ids=new Set(before.events.map(e=>e.id)),q=H.duplicate(before,[...selected]);
 selected=new Set(q.events.filter(e=>!ids.has(e.id)).map(e=>e.id));commit("Duplicate events",q);
}
const editCommands={
 undo:()=> $("undo").click(),redo:()=> $("redo").click(),copy:()=>copyEvents(),cut:()=>copyEvents(true),paste:pasteEvents,
 duplicate:duplicateEvents,repeat:()=> $("repeat").click(),
 delete:()=>{if(!selected.size)return status("Select events first.");const next=H.remove(p(),[...selected]);selected.clear();commit("Delete events",next);},
 selectAll:()=>{selected=new Set(p().events.map(e=>e.id));update();},
 deselect:()=>{selected.clear();range=null;update();}
};
function updateEditCommands(){
 for(const key of Object.keys(editCommands)){const el=$("edit-"+key);if(!el)continue;
  el.disabled=["copy","cut","duplicate","repeat","delete"].includes(key)?!selected.size:key==="paste"?!clipboard||clipboard.projectId!==p().id:key==="undo"?!history.undoStack.length:key==="redo"?!history.redoStack.length:false;
 }
}
function nudgeSelection(key){
 if(gesture||rowDrag||locatorDrag)return;
 const events=p().events.filter(e=>selected.has(e.id));if(!events.length)return status("Select events first.");
 const primary=events.find(e=>e.rowId===active)||events[0],earliest=H.minValue(events.map(e=>e.timeMs));let delta=0,rowDelta=0;
 if(key==="ArrowUp"||key==="ArrowDown"){rowDelta=key==="ArrowUp"?-1:1;if(SG){const rows=visibleRows().filter(r=>!r._group),i=rows.findIndex(r=>r.id===primary.rowId),target=rows[i+rowDelta];rowDelta=target?p().rows.findIndex(r=>r.id===target.id)-p().rows.findIndex(r=>r.id===primary.rowId):0;}}
 else{
  const divisor=Number($("divisor").value)||4;
  delta=Math.max(0,H.gridNeighbor(earliest,p(),divisor,key==="ArrowRight"?1:-1))-earliest;
 }
 const next=H.move(p(),events.map(e=>e.id),delta,rowDelta),moved=next.events.find(e=>e.id===primary.id);
 if(moved){active=moved.rowId;const rows=SG?SG.entries(next,groupState,$("filter").value):next.rows.filter(r=>(H.laneLabel(r)+" "+r.name).toLowerCase().includes($("filter").value.toLowerCase())),i=rows.findIndex(r=>r.id===active);
  if(i>=0){if(rowTop(i,rows)<scrollY)scrollY=rowTop(i,rows);else if(rowTop(i+1,rows)>scrollY+$("grid").clientHeight)scrollY=rowTop(i+1,rows)-$("grid").clientHeight;}
  if(xAt(moved.timeMs)<0||xAt(moved.timeMs)>$("grid").clientWidth)view=Math.max(0,moved.timeMs-$("grid").clientWidth/scale*.15);
 }
 commit(rowDelta?"Move selected events between rows":"Move selected events to grid",next);
}

function editCommand(key){if(gesture)return status("Finish the current drag or press Escape first.");$("editMenu").open=false;const fn=editCommands[key];if(fn){focusEditor();fn();}}
for(const key of Object.keys(editCommands))$("edit-"+key).onclick=()=>editCommand(key);
document.addEventListener("pointerdown",ev=>{if(!$("editMenu").contains(ev.target))$("editMenu").open=false;});

window.addEventListener("keydown",ev=>{
 if(ev.key==="Alt"||ev.altKey){setAltDraw(!$("dialog").open&&modifierDraw(ev));if(ev.key==="Alt"){if(altDraw)ev.preventDefault();return;}}
 if($("dialog").open)return;
 if(ev.key==="Escape"&&gesture){ev.preventDefault();history.project=gesture.original||gesture.before;gesture=null;status("Gesture cancelled");update();return;}
 if(ev.key==="Escape"&&$("editMenu").open){ev.preventDefault();$("editMenu").open=false;focusEditor();return;}
 if(ev.key==="F1"){ev.preventDefault();$("help").click();return;}
 if(textEditing())return;
 if((["toggleController","toggleInspector","toggleLanes","toggleInfo"].some(id=>document.activeElement===$(id))||document.activeElement?.dataset?.inspectorControl==="true")&&[" ","Enter"].includes(ev.key))return;
 if(document.activeElement?.tagName==="INPUT"&&document.activeElement.type==="range"&&["ArrowLeft","ArrowRight","ArrowUp","ArrowDown","Home","End","PageUp","PageDown"].includes(ev.key))return;
 if(ev.shiftKey&&["l","r"].includes(ev.key.toLowerCase())){ev.preventDefault();$(ev.key.toLowerCase()==="l"?"loopLeft":"loopRight").focus();return;}
 if(ev.code==="Numpad0"){ev.preventDefault();$("stop").click();return;}
 if(ev.code==="NumpadDivide"){ev.preventDefault();$("loop").click();return;}
 if(ev.code==="Numpad1"||ev.code==="Numpad2"){ev.preventDefault();const side=ev.code==="Numpad1"?"leftMs":"rightMs";if(ev.ctrlKey||ev.metaKey)commit("Set cycle locator",H.setCycle(p(),{[side]:cursor}));else{stop();cursor=H.cycleSettings(p())[side];update();}return;}
 if(ev.key.toLowerCase()==="p"&&!ev.ctrlKey&&!ev.metaKey){ev.preventDefault();if(ev.altKey&&playing)stop();$("loopSelection").click();if(ev.altKey){commit("Enable cycle",H.setCycle(p(),{enabled:true}));cursor=H.cycleSettings(p()).leftMs;if(playing)stop();play().catch(e=>status(e.message));}return;}
 if(ev.altKey&&!["ArrowLeft","ArrowRight","Escape"].includes(ev.key))return;
 if(gesture)return;
 const key=ev.key.toLowerCase();
 if(ev.ctrlKey||ev.metaKey){
  if(!ev.altKey&&!ev.shiftKey&&["ArrowLeft","ArrowRight","ArrowUp","ArrowDown"].includes(ev.key)){ev.preventDefault();nudgeSelection(ev.key);return;}
  const command=key==="z"?(ev.shiftKey?"redo":"undo"):({y:"redo",c:"copy",x:"cut",v:"paste",d:"duplicate",a:ev.shiftKey?"deselect":"selectAll",r:"repeat"})[key];
  if(command){ev.preventDefault();editCommand(command);if(command==="selectAll"){controllerAllSelection={projectId:p().id,active,selection:selected,count:selected.size};if(controllerCollapsed)setControllerCollapsed(false);else update();}return;}
  const button=({s:"save",o:"open",n:"newProject"})[key];if(button){ev.preventDefault();$(button).click();return;}
  if(key==="b"){ev.preventDefault();if(ev.shiftKey)removeBookmarkAtCursor();else addBookmark();return;}
  if(key==="f"){ev.preventDefault();setLanesCollapsed(false);$("filter").focus({preventScroll:true});$("filter").select();return;}
  return;
 }
 if(ev.key==="["||ev.key==="]"){ev.preventDefault();jumpBookmark(BM?.neighbor(p(),cursor,ev.key==="["?-1:1));return;}
 if(ev.key==="Delete"||ev.key==="Backspace"){ev.preventDefault();editCommand("delete");return;}
 if(ev.key===" "){ev.preventDefault();$("play").click();return;}
 if(ev.key==="Home"){ev.preventDefault();$("toStart").click();return;}
 if(key==="j"){$("snap").click();return;}if(key==="q"){$("quantize").click();return;}if(key==="l"){$("loop").click();return;}
 const tools=["select","range","draw","erase","mute","ramp","audition"];if(Number(ev.key)>=1&&Number(ev.key)<=7)setTool(tools[Number(ev.key)-1]);
 if(ev.key==="Escape"){selected.clear();range=null;update();}
 if(["ArrowLeft","ArrowRight"].includes(ev.key)&&selected.size){ev.preventDefault();const e=p().events.find(e=>selected.has(e.id)),step=H.beatToTime(H.timeToBeat(e.timeMs,p().tempos)+1/Number($("divisor").value),p().tempos)-e.timeMs;commit("Nudge events",H.move(p(),[...selected],(ev.key==="ArrowLeft"?-1:1)*(ev.altKey?1:step)));}
});
window.addEventListener("keyup",ev=>setAltDraw(!$("dialog").open&&modifierDraw(ev)));
window.addEventListener("blur",()=>{altDraw=false;if(gesture){history.project=gesture.original||gesture.before;gesture=null;update();}else syncTools();});
function drawCycleShade(ctx,h){const c=H.cycleSettings(p());if(c.leftMs===c.rightMs)return;ctx.fillStyle=c.enabled?(c.leftMs<c.rightMs?"#007aff0d":"#d98a2614"):"#62748a08";ctx.fillRect(xAt(Math.min(c.leftMs,c.rightMs)),0,Math.abs(c.rightMs-c.leftMs)*scale,h);}
function drawLocators(ctx,w,h){const c=H.cycleSettings(p()),l=xAt(c.leftMs),r=xAt(c.rightMs);ctx.fillStyle=c.enabled?(l<r?"#2a5c97aa":"#946b3d99"):"#64647566";ctx.fillRect(Math.min(l,r),0,Math.abs(r-l),19);for(const [x,label,dir] of [[l,"L",1],[r,"R",-1]]){ctx.fillStyle=c.enabled?"#007aff":"#8c95a5";ctx.beginPath();ctx.moveTo(x,0);ctx.lineTo(x+dir*13,0);ctx.lineTo(x+dir*13,17);ctx.lineTo(x,24);ctx.closePath();ctx.fill();ctx.fillStyle="#ffffff";ctx.font="bold 10px Segoe UI";ctx.fillText(label,x+(dir===1?3:-11),12);}}
function reorderSampleRows(id,target,after){try{commit("Reorder sample rows",SG?SG.reorder(p(),id,target,after):H.reorderRow(p(),id,target,after));}catch(error){status(error.message);update();}}
function finishRowDrag(cancel=false){if(!rowDrag)return;const d=rowDrag;rowDrag=null;$("rows").style.cursor="";if(!cancel)reorderSampleRows(d.id,d.target,d.after);else renderRows();}
$("rows").onpointermove=ev=>{if(!rowDrag)return;const y=local(ev,$("rows")).y,rows=visibleRows(),height=$("grid").clientHeight;if(y<22)scrollY=Math.max(0,scrollY-rowH);else if(y>height-22)scrollY=Math.min(Math.max(0,rowsHeight(rows)-height),scrollY+rowH);const n=H.clamp(rowIndexAt(y+scrollY,rows),0,rows.length-1),target=rows[n];if(!target||target._group)return;rowDrag.target=target.id;rowDrag.after=y+scrollY-rowTop(n,rows)>rowH/2;renderRows();for(const holder of $("rows").children)for(const el of holder.children||[]){el.classList.toggle("dropBefore",el.dataset.rowId===rowDrag.target&&!rowDrag.after);el.classList.toggle("dropAfter",el.dataset.rowId===rowDrag.target&&rowDrag.after);}$("rows").style.cursor="grabbing";};
$("rows").onpointerup=()=>finishRowDrag();$("rows").onpointercancel=()=>finishRowDrag(true);
for(const [id,delta] of [["rowUp",-1],["rowDown",1]])$(id).onclick=()=>{const rows=SG?visibleRows().filter(r=>!r._group):p().rows,i=rows.findIndex(r=>r.id===active),target=rows[i+delta];if(target)reorderSampleRows(active,target.id,delta>0);};
for(const [id,key] of [["loopLeft","leftMs"],["loopRight","rightMs"]]){$(id).onchange=run(()=>{const value=Number($(id).value);if(!$(id).value.trim()||!Number.isFinite(value)||value<0||value>86400000){update();return status("Enter a locator position between 0 and 86400000 ms.");}commit("Set cycle locator",H.setCycle(p(),{[key]:Math.round(value)}));});}
$("loopSelection").onclick=()=>{const b=selectionBounds();if(!b)return status("Select events or drag an editing range first.");commit("Cycle locators to selection",H.setCycle(p(),{leftMs:b[0],rightMs:b[1]}));};
$("swapLoop").onclick=()=>{const c=H.cycleSettings(p());commit("Exchange cycle locators",H.setCycle(p(),{leftMs:c.rightMs,rightMs:c.leftMs}));};
for(const [id,key] of [["goLeft","leftMs"],["goRight","rightMs"]])$(id).onclick=()=>{stop();cursor=H.cycleSettings(p())[key];update();};
const ruler=$("ruler");
ruler.onpointerdown=ev=>{if(ev.button!==0)return;focusEditor();const pt=local(ev,ruler),t=Math.max(0,H.snapTime(timeAt(pt.x),p(),settings())),c=H.cycleSettings(p());ruler.setPointerCapture(ev.pointerId);
 const bookmark=BM&&pt.y>=ruler.clientHeight-12?BM.list(p()).find(b=>Math.abs(xAt(b.timeMs)-pt.x)<=7):null;if(bookmark&&!ev.ctrlKey&&!ev.altKey&&!ev.metaKey){jumpBookmark(bookmark);return;}
 if(ev.ctrlKey||ev.metaKey||ev.altKey){commit("Set cycle locator",H.setCycle(p(),{[ev.altKey?"rightMs":"leftMs"]:t}));return;}
 const dl=Math.abs(xAt(c.leftMs)-pt.x),dr=Math.abs(xAt(c.rightMs)-pt.x);
 if(pt.y<24&&(dl<9||dr<9))locatorDrag={type:dl<=dr?"left":"right",x:pt.x,t,before:p(),cycle:c};
 else if(pt.y<19)locatorDrag={type:t>Math.min(c.leftMs,c.rightMs)&&t<Math.max(c.leftMs,c.rightMs)?"move":"draw",x:pt.x,t,before:p(),cycle:c};
 else{stop();cursor=gridCursorTime(timeAt(pt.x));update();}
};
ruler.onpointermove=ev=>{if(!locatorDrag)return;const d=locatorDrag,pt=local(ev,ruler);if(Math.abs(pt.x-d.x)<3&&!d.moved)return;d.moved=true;const t=Math.max(0,H.snapTime(timeAt(pt.x),p(),settings()));let change={};
 if(d.type==="left")change.leftMs=t;else if(d.type==="right")change.rightMs=t;else if(d.type==="draw")change={leftMs:Math.min(d.t,t),rightMs:Math.max(d.t,t)};else{const delta=Math.max(t-d.t,-Math.min(d.cycle.leftMs,d.cycle.rightMs));change={leftMs:d.cycle.leftMs+delta,rightMs:d.cycle.rightMs+delta};}
 history.project=H.setCycle(d.before,change);refreshTransport();render();};
ruler.onpointerup=()=>{const d=locatorDrag;if(!d)return;locatorDrag=null;if(d.moved)commit("Move cycle locators",p(),d.before);else if(d.type==="move")$("loop").click();else update();};
ruler.onpointercancel=()=>{if(locatorDrag){history.project=locatorDrag.before;locatorDrag=null;update();}};
window.addEventListener("keydown",ev=>{if(ev.key==="Escape"){finishRowDrag(true);if(locatorDrag){history.project=locatorDrag.before;locatorDrag=null;update();}}});
window.addEventListener("blur",()=>{finishRowDrag(true);if(locatorDrag){history.project=locatorDrag.before;locatorDrag=null;update();}});

try{infoCollapsed=JSON.parse(localStorage.getItem("hse-info-collapsed")||"false")===true;}catch{}
try{const layout=JSON.parse(localStorage.getItem("hse-panel-layout")||"{}");inspectorCollapsed=layout.inspectorCollapsed===true;lanesCollapsed=layout.lanesCollapsed===true;}catch{}
new ResizeObserver(update).observe($("gridBody"));window.addEventListener("beforeunload",ev=>{if(renamingLane)finishLaneRename(true,false);stop();if(dirty||gesture){ev.preventDefault();ev.returnValue="";}});update();


desktop.defaults().then(samples=>{if(!p().demo)return;for(const row of p().rows){const s=samples.find(s=>s.name===row.name);if(s)row.assetKey=s.assetKey;}const backing=samples.find(s=>s.name==="demo-backing.wav");if(backing){p().audio={...backing,durationMs:8000};getBuffer(backing.assetKey).then(b=>makeWave(backing.assetKey,b));}update();}).catch(e=>status(e.message));













HSEHelp.install({
 isBusy:()=>!!gesture||!!rowDrag||!!locatorDrag||$("dialog").open,
 describe(el,ev){
  if(!ev||!Number.isFinite(ev.clientX))return "";
  if(el.id==="objects"){const t=timeAt(local(ev,el).x),edges=p().objects.filter(o=>Math.abs(o.timeMs-t)*scale<7);if(edges.length)return edges.map(o=>o.role+" · "+Math.round(o.timeMs)+" ms").join(" / ");const span=H.sliderSpans(p()).find(s=>t>=s.startMs&&t<=s.endMs);return span?"Slider body · "+Math.round(span.startMs)+"–"+Math.round(span.endMs)+" ms · "+Math.round(span.endMs-span.startMs)+" ms long":"";}
  if(el.id!=="grid"&&el.id!=="bars")return "";
  const pt=local(ev,el);let sound;
  if(el.id==="grid")sound=hit(pt.x,pt.y);
  else if(allControllerSelected())sound=controllerBarAt(pt.x);
  else{const scope=new Set(targets());sound=p().events.filter(e=>scope.has(e.id)&&Math.abs(xAt(e.timeMs)-pt.x)<8).sort((a,b)=>Math.abs(xAt(a.timeMs)-pt.x)-Math.abs(xAt(b.timeMs)-pt.x))[0];}
  if(!sound)return "";
  const row=p().rows.find(r=>r.id===sound.rowId);
  return (row?.name||"Sound")+"\nStart: "+Math.round(sound.timeMs*1000)/1000+" ms · Length: "+Math.round(sound.durationMs*1000)/1000+" ms\nVolume: "+sound.volume+"%"+(sound.performance?" · MIDI velocity: "+sound.performance.velocity:"")+"\n"+(sound.muted?"Muted · ":"")+(sound.ownerId?"Assigned to a gameplay object":"Not assigned to a gameplay object")+"\nCtrl+C Copy · Ctrl+X Cut · Ctrl+V Paste at cursor";
 }
});





if(desktop.version)desktop.version().then(version=>{appVersion=version;update();}).catch(()=>{});
