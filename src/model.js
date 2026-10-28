(function(root){
"use strict";
const clone=x=>JSON.parse(JSON.stringify(x)),clamp=(n,a,b)=>Math.max(a,Math.min(b,n));
const minValue=values=>values.reduce((a,b)=>Math.min(a,b),Infinity),maxValue=values=>values.reduce((a,b)=>Math.max(a,b),-Infinity);
const id=p=>p+"-"+(globalThis.crypto?.randomUUID?.()||Date.now().toString(36)+"-"+Math.random().toString(36).slice(2));
const finite=(v,name)=>{if(!Number.isFinite(v))throw Error("Invalid "+name);return v;};
function project(name="Untitled"){
 return {schemaVersion:1,id:id("project"),name,rows:[],events:[],objects:[],tempos:[{timeMs:0,bpm:120,meter:4}],midiSources:[],beatmaps:[],bookmarks:[],audio:null};
}
function segments(tempos){
 const sorted=(tempos?.length?tempos:[{timeMs:0,bpm:120,meter:4}]).map(t=>({...t})).sort((a,b)=>a.timeMs-b.timeMs);
 const clean=[];for(const t of sorted){if(!Number.isFinite(t.timeMs)||!(t.bpm>0&&t.bpm<=6000))throw Error("Invalid tempo point");if(clean.at(-1)?.timeMs===t.timeMs)clean.pop();clean.push(t);}
 let beat=0;for(let i=0;i<clean.length;i++){if(i)beat+=(clean[i].timeMs-clean[i-1].timeMs)*clean[i-1].bpm/60000;clean[i].beat=beat;}return clean;
}
function timeToBeat(ms,tempos){const list=segments(tempos);let t=list[0];for(const x of list){if(x.timeMs>ms)break;t=x;}return t.beat+(ms-t.timeMs)*t.bpm/60000;}
function beatToTime(beat,tempos){const list=segments(tempos);let t=list[0];for(const x of list){if(x.beat>beat)break;t=x;}return t.timeMs+(beat-t.beat)*60000/t.bpm;}
function signatureMap(p){
 if(p.timeSignatures!==undefined&&!Array.isArray(p.timeSignatures))throw Error("Invalid time-signature map");
 const tempos=p.tempos?.length?p.tempos:[{timeMs:0,meter:4}],source=p.timeSignatures?.length?p.timeSignatures:tempos.filter((t,i)=>i===0||p.beatmaps?.length||(t.meter||4)!==(tempos[i-1].meter||4)).map(t=>({timeMs:t.timeMs,numerator:t.meter||4,denominator:4}));
 const out=[];for(const point of source.map(t=>({...t})).sort((a,b)=>a.timeMs-b.timeMs)){
  finite(point.timeMs,"time-signature position");const n=point.numerator,d=point.denominator;
  if(!Number.isInteger(n)||n<1||n>255||!Number.isInteger(d)||d<1||d>128||(d&(d-1)))throw Error("Time signature needs 1-255 beats and a beat unit of 1, 2, 4, 8, 16, 32, 64 or 128.");
  if(out.at(-1)?.timeMs===point.timeMs)out.pop();out.push(point);
 }return out;
}
function setSignatures(p,points){const out=clone(p);out.timeSignatures=signatureMap({...out,timeSignatures:points});return out;}
function signatureSegments(p){
 const signatures=signatureMap(p);let anchors=signatures;
 // Signature data may omit unchanged meters, but every dawsu! red point is a new bar origin.
 // Derive these display anchors without rewriting saved signatures or source timing data.
 if(p.beatmaps?.length){const points=new Map();for(const t of segments(p.tempos)){const current=signatures.filter(s=>s.timeMs<=t.timeMs+1e-8).at(-1)||signatures[0];points.set(t.timeMs,{...current,timeMs:t.timeMs});}for(const s of signatures)points.set(s.timeMs,s);anchors=[...points.values()].sort((a,b)=>a.timeMs-b.timeMs);}
 const list=anchors.map(s=>({...s,beat:timeToBeat(s.timeMs,p.tempos),bar:1}));
 for(let i=1;i<list.length;i++){const prev=list[i-1];list[i].bar=prev.bar+Math.max(1,Math.ceil((list[i].beat-prev.beat)/(prev.numerator*4/prev.denominator)-1e-8));}
 return list;
}
function beatPosition(p,beat,list=signatureSegments(p)){
 let s=list[0];for(const candidate of list){if(candidate.beat>beat+1e-8)break;s=candidate;}
 const units=(beat-s.beat)*s.denominator/4,barOffset=Math.floor((units+1e-8)/s.numerator),inBar=units-barOffset*s.numerator;
 return {...s,barNumber:s.bar+barOffset,beatNumber:Math.floor(inBar+1e-8)+1,isBar:Math.abs(inBar)<1e-7,isBeat:Math.abs(inBar-Math.round(inBar))<1e-7};
}
function sliderSpans(p){
 const out=(p.sliderSpans||[]).map(s=>({...s})),known=new Set(out.map(s=>s.beatmapId+":"+s.lineIndex)),groups=new Map();
 for(const o of p.objects){if(!o.role?.startsWith("slider-")||o.lineIndex==null)continue;const key=o.beatmapId+":"+o.lineIndex;if(known.has(key))continue;if(!groups.has(key))groups.set(key,[]);groups.get(key).push(o);}
 for(const [key,objects] of groups){const head=objects.find(o=>o.role==="slider-head"),tail=objects.find(o=>o.role==="slider-tail");if(!head||!tail)continue;out.push({id:key+":body",beatmapId:head.beatmapId,lineIndex:head.lineIndex,startMs:head.timeMs,endMs:tail.timeMs,headId:head.id,tailId:tail.id,repeatIds:objects.filter(o=>o.role==="slider-repeat").map(o=>o.id)});}
 return out;
}

// dawsu! red points restart beat phase; MIDI tempo events retain continuous beats.
function gridSegments(p){const list=segments(p.tempos);return list.map((t,i)=>({...t,phase:p.beatmaps?.length?t.beat:0,startBeat:i?t.beat:-Infinity,endBeat:list[i+1]?.beat??Infinity}));}
function gridBeats(p,from,to,step=1/4){
 if(!(step>0))throw Error("Invalid grid division");const out=[];
 for(const s of gridSegments(p)){const lo=Math.max(from,s.startBeat),hi=Math.min(to,s.endBeat);if(hi<lo)continue;
 for(let n=Math.ceil((lo-s.phase)/step-1e-8),beat=s.phase+n*step;beat<=hi+1e-8&&beat<s.endBeat-1e-8;n++,beat=s.phase+n*step){if(out.length>=10000)throw Error("Grid range too long");out.push(beat);}}
 return out;
}
function gridCandidates(ms,p,divisor=4,relative=0){
 const beat=timeToBeat(ms,p.tempos),step=1/divisor,out=[];
 for(const s of gridSegments(p)){const phase=s.phase+relative,lo=Number.isFinite(s.startBeat)?Math.ceil((s.startBeat-phase)/step-1e-8):-Infinity,hi=Number.isFinite(s.endBeat)?Math.ceil((s.endBeat-phase)/step-1e-8)-1:Infinity,center=clamp(Math.round((beat-phase)/step),lo,hi);if(lo>hi)continue;
 for(let n=center-2;n<=center+2;n++)if(n>=lo&&n<=hi){const b=phase+n*step;out.push(s.timeMs+(b-s.beat)*60000/s.bpm);}}
 return out;
}
function gridNeighbor(ms,p,divisor=4,direction=1){const times=gridCandidates(ms,p,divisor).filter(t=>direction>0?t>ms+1e-5:t<ms-1e-5);return times.length?(direction>0?minValue(times):maxValue(times)):ms;}
function snapTime(ms,p,settings={},anchorMs){
 if(settings.enabled===false)return ms;
 const divisor=Number(settings.divisor)||4;
 let relative=0;if(settings.target==="relative"&&Number.isFinite(anchorMs)){const beat=timeToBeat(anchorMs,p.tempos),s=gridSegments(p).filter(s=>beat>=s.startBeat&&beat<s.endBeat)[0];relative=(beat-(s?.phase||0))%(1/divisor);}
 const times=gridCandidates(ms,p,divisor,relative);let snapped=times.reduce((best,t)=>Math.abs(t-ms)<Math.abs(best-ms)-1e-8||Math.abs(Math.abs(t-ms)-Math.abs(best-ms))<1e-8&&t>best?t:best,times[0]??ms);
 if(settings.target==="objects"||settings.target==="cursor"){
 const candidates=settings.target==="objects"?p.objects.map(o=>o.timeMs):[settings.cursorMs||0];if(candidates.length)snapped=candidates.reduce((a,b)=>Math.abs(b-ms)<Math.abs(a-ms)?b:a);}
 return snapped;
}
function ownerCandidates(p,time,tolerance=0.5){return p.objects.filter(o=>Math.abs(o.timeMs-time)<=tolerance);}
function resolveOwner(p,time,tolerance=.5){const found=ownerCandidates(p,time,tolerance);return {ownerId:found.length===1?found[0].id:null,assignment:found.length===1?"bound":found.length?"ambiguous":"unassigned"};}
function ownerResolver(p){const objects=[...p.objects].sort((a,b)=>a.timeMs-b.timeMs);return (time,tolerance=.5)=>{let lo=0,hi=objects.length;while(lo<hi){const mid=(lo+hi)>>>1;if(objects[mid].timeMs<time-tolerance)lo=mid+1;else hi=mid;}let match=null,count=0;for(let i=lo;i<objects.length&&objects[i].timeMs<=time+tolerance;i++){match=objects[i];count++;if(count>1)break;}return {ownerId:count===1?match.id:null,assignment:count===1?"bound":count?"ambiguous":"unassigned"};};}
function validate(p){
 if(!p||p.schemaVersion!==1)throw Error("Unsupported project format");
 for(const k of ["rows","events","objects","tempos","midiSources","beatmaps"])if(!Array.isArray(p[k]))throw Error("Missing project "+k);
 if(p.cycle)setCycle(p,{});signatureMap(p);
 if(p.sliderSpans!==undefined){if(!Array.isArray(p.sliderSpans))throw Error("Invalid slider spans");for(const span of p.sliderSpans){finite(span.startMs,"slider start");finite(span.endMs,"slider end");if(span.endMs<span.startMs)throw Error("Invalid slider duration");}}
 const rowIDs=new Set(),eventIDs=new Set(),objectIDs=new Set();
 for(const r of p.rows){if(typeof r.id!=="string"||rowIDs.has(r.id))throw Error("Duplicate or invalid sample ID");rowIDs.add(r.id);if(r.label!==undefined&&(typeof r.label!=="string"||r.label.length>128))throw Error("Invalid lane label");if(r.name!==undefined&&typeof r.name!=="string")throw Error("Invalid sample name");if(r.note!==undefined&&(!Number.isInteger(r.note)||r.note<0||r.note>127)||r.channel!==undefined&&(!Number.isInteger(r.channel)||r.channel<1||r.channel>16))throw Error("Invalid sample MIDI mapping");}
 for(const o of p.objects){if(typeof o.id!=="string"||objectIDs.has(o.id))throw Error("Duplicate object ID");objectIDs.add(o.id);finite(o.timeMs,"object time");}
 for(const e of p.events){
  if(typeof e.id!=="string"||eventIDs.has(e.id)||!rowIDs.has(e.rowId))throw Error("Invalid event identity or sample");eventIDs.add(e.id);
  finite(e.timeMs,"event time");finite(e.durationMs,"duration");finite(e.volume,"volume");
  if(e.performance&&(!Number.isInteger(e.performance.note)||e.performance.note<0||e.performance.note>127||!Number.isInteger(e.performance.channel)||e.performance.channel<1||e.performance.channel>16||!Number.isInteger(e.performance.velocity)||e.performance.velocity<1||e.performance.velocity>127||e.performance.releaseVelocity!==undefined&&(!Number.isInteger(e.performance.releaseVelocity)||e.performance.releaseVelocity<0||e.performance.releaseVelocity>127)))throw Error("Invalid MIDI note mapping");
  if(e.durationMs<0||(e.volume<1&&!(e.volume===0&&e.source?.originalVolume===0))||e.volume>100)throw Error("Event values outside valid range");
  if(e.ownerId&&!objectIDs.has(e.ownerId))throw Error("Missing object owner");
 }if(p.audio){const a=audioSettings(p.audio);finite(a.volume,"song volume");finite(a.offsetMs,"song offset");if(a.volume<0||a.volume>100||Math.abs(a.offsetMs)>86400000)throw Error("Invalid song playback settings");}segments(p.tempos);return p;
}
function setVolumes(p,ids,value,mode="absolute"){
 value=clamp(Math.round(finite(Number(value),"volume")),1,100);
 const out=clone(p),keys=new Set(ids),first=out.events.find(e=>e.id===ids[0]);if(!first)return out;
 const delta=value-first.volume;
 for(const e of out.events)if(keys.has(e.id))e.volume=mode==="relative"?clamp(e.volume+delta,1,100):value;return out;
}
function ramp(p,ids,a,b,va,vb,shape="line"){
 const out=clone(p),keys=new Set(ids),min=Math.min(a,b),max=Math.max(a,b);
 for(const e of out.events){if(!keys.has(e.id)||e.timeMs<min||e.timeMs>max)continue;
  let u=a===b?1:(e.timeMs-a)/(b-a);
  if(shape==="sine")u=(1-Math.cos(u*Math.PI))/2;else if(shape==="parabola")u*=u;else if(shape==="triangle")u=1-Math.abs(2*u-1);else if(shape==="square")u=u<.5?0:1;
  e.volume=clamp(Math.round(va+(vb-va)*u),1,100);
 }return out;
}
function move(p,ids,delta,rowDelta=0,tolerance=.5){
 const out=clone(p),keys=new Set(ids),targets=out.events.filter(e=>keys.has(e.id));if(!targets.length)return out;
 const owner=ownerResolver(out),indices=targets.map(e=>out.rows.findIndex(r=>r.id===e.rowId));
 rowDelta=clamp(rowDelta,-minValue(indices),out.rows.length-1-maxValue(indices));
 delta=Math.max(delta,-minValue(targets.map(e=>e.timeMs)));
 for(const e of targets){e.rowId=out.rows[out.rows.findIndex(r=>r.id===e.rowId)+rowDelta].id;
  if(delta){e.timeMs=Math.round((e.timeMs+delta)*1000)/1000;Object.assign(e,owner(e.timeMs,tolerance));}
 }return out;
}
function paint(p,rowId,a,b,settings,mode="draw",volume=80,durationMs=125,visited=new Set()){
 const out=clone(p),owner=ownerResolver(out),div=Number(settings.divisor)||4,low=Math.min(a,b),high=Math.max(a,b);
 let times=settings.enabled===false?[b]:[];
 if(settings.enabled!==false){const start=snapTime(low,p,{...settings,target:"grid"}),end=snapTime(high,p,{...settings,target:"grid"});times=gridBeats(p,timeToBeat(start,p.tempos),timeToBeat(end,p.tempos),1/div).map(beat=>beatToTime(beat,p.tempos));}
 for(const time of times){if(time<0)continue;
  const key=rowId+":"+time.toFixed(5);if(visited.has(key))continue;visited.add(key);
  const existing=out.events.filter(e=>e.rowId===rowId&&Math.abs(e.timeMs-time)<.01);
  if(mode==="erase"){const keys=new Set(existing.map(e=>e.id));out.events=out.events.filter(e=>!keys.has(e.id));}
  else if(!existing.length)out.events.push({id:id("hit"),rowId,timeMs:Math.max(0,time),durationMs,volume:clamp(Math.round(volume),1,100),muted:false,...owner(time),source:null,performance:{note:out.rows.find(r=>r.id===rowId)?.note??35,channel:out.rows.find(r=>r.id===rowId)?.channel??10,velocity:100,releaseVelocity:0}});
 }return out;
}
function duplicate(p,ids,count=1,offsetMs=null){
 const out=clone(p),keys=new Set(ids),owner=ownerResolver(out),targets=p.events.filter(e=>keys.has(e.id));if(!targets.length)return out;
 const start=minValue(targets.map(e=>e.timeMs)),end=maxValue(targets.map(e=>e.timeMs+Math.max(e.durationMs,1)));
 const offset=offsetMs??Math.max(end-start,1);
 for(let n=1;n<=count;n++)for(const e of targets){const next={...clone(e),id:id("copy"),timeMs:e.timeMs+n*offset};Object.assign(next,owner(next.timeMs));out.events.push(next);}return out;
}
function remove(p,ids){const out=clone(p),keys=new Set(ids);out.events=out.events.filter(e=>!keys.has(e.id));return out;}
function mute(p,ids,value){const out=clone(p),keys=new Set(ids);for(const e of out.events)if(keys.has(e.id))e.muted=value??!e.muted;return out;}
function snapSelected(p,ids,settings,strength=1){const out=clone(p),keys=new Set(ids);for(const e of out.events)if(keys.has(e.id)){const target=snapTime(e.timeMs,p,settings);e.timeMs+=clamp(strength,0,1)*(target-e.timeMs);Object.assign(e,resolveOwner(out,e.timeMs));}return out;}
function assign(p,ids,tolerance=2,ambiguity="keep"){const out=clone(p),keys=new Set(ids);for(const e of out.events)if(keys.has(e.id)){let found=resolveOwner(out,e.timeMs,tolerance);if(found.assignment==="ambiguous"&&ambiguity==="first"){const candidates=ownerCandidates(out,e.timeMs,tolerance).sort((a,b)=>Math.abs(a.timeMs-e.timeMs)-Math.abs(b.timeMs-e.timeMs));found={ownerId:candidates[0].id,assignment:"bound"};}Object.assign(e,found);if(found.ownerId)e.timeMs=out.objects.find(o=>o.id===found.ownerId).timeMs;}return out;}
function rangeEdit(p,range,action,offset=0,split=false){
 let out=clone(p);const a=Math.min(range.start,range.end),b=Math.max(range.start,range.end),rowSet=new Set(range.rowIds||p.rows.map(r=>r.id));
 if(split){const extras=[];for(const e of out.events){if(!rowSet.has(e.rowId)||!e.performance)continue;
  const ends=[e.timeMs,...[a,b].filter(t=>t>e.timeMs&&t<e.timeMs+e.durationMs),e.timeMs+e.durationMs];
  if(ends.length>2){e.durationMs=ends[1]-ends[0];for(let i=1;i<ends.length-1;i++)extras.push({...clone(e),id:id("split"),timeMs:ends[i],durationMs:ends[i+1]-ends[i],...resolveOwner(out,ends[i])});}
 }out.events=out.events.concat(extras);}
 const ids=out.events.filter(e=>rowSet.has(e.rowId)&&e.timeMs>=a&&e.timeMs<b).map(e=>e.id);
 if(action==="delete")return remove(out,ids);
 if(action==="move")return move(out,ids,offset);
 if(action==="copy")return duplicate(out,ids,1,offset);
 return out;
}
function bulk(p,scope,rule){
 const allowed=new Set(scope),ids=p.events.filter(e=>allowed.has(e.id)&&(!rule.rowId||e.rowId===rule.rowId)&&e.volume>=(rule.min??1)&&e.volume<=(rule.max??100)&&(!rule.unassigned||!e.ownerId)).map(e=>e.id);
 let out=clone(p);
 if(rule.action==="scale"){const keys=new Set(ids);for(const e of out.events)if(keys.has(e.id))e.volume=clamp(Math.round(e.volume*(Number(rule.value)||0)/100),1,100);}
 if(rule.action==="fixed")out=setVolumes(out,ids,Number(rule.value));
 if(rule.action==="mute")out=mute(out,ids,true);
 return {project:out,ids};
}
// Every appended document receives fresh local IDs, including its source snapshots.
function appendProject(p,incoming,offset=0){
 finite(offset,"import offset");validate(p);validate(incoming);const out=clone(p),next=clone(incoming),maps=new Map();
 function mapped(kind,old){if(old==null)return old;if(!maps.has(kind))maps.set(kind,new Map());const ids=maps.get(kind);if(!ids.has(old))ids.set(old,id(kind));return ids.get(old);}
 function row(r){r.id=mapped("row",r.id);return r;}
 function event(e){e.id=mapped("event",e.id);e.rowId=mapped("row",e.rowId);e.timeMs+=offset;if(e.timeMs<0)throw Error("This offset places events before zero.");if(e.ownerId)e.ownerId=mapped("object",e.ownerId);else Object.assign(e,resolveOwner(out,e.timeMs));if(e.source?.sourceId)e.source.sourceId=mapped("midi",e.source.sourceId);if(e.source?.beatmapId)e.source.beatmapId=mapped("beatmap",e.source.beatmapId);return e;}
 next.rows.forEach(row);next.events.forEach(event);
 for(const o of next.objects){o.id=mapped("object",o.id);if(o.beatmapId)o.beatmapId=mapped("beatmap",o.beatmapId);o.timeMs+=offset;}
 for(const source of next.midiSources){source.id=mapped("midi",source.id);source.offsetMs=(source.offsetMs||0)+offset;}
 for(const map of next.beatmaps){map.id=mapped("beatmap",map.id);map.originalRows?.forEach(row);map.originalEvents?.forEach(event);map.importOffsetMs=(map.importOffsetMs||0)+offset;}
 for(const span of next.sliderSpans||[]){span.id=mapped("span",span.id);span.beatmapId=mapped("beatmap",span.beatmapId);span.startMs+=offset;span.endMs+=offset;span.headId=mapped("object",span.headId);span.tailId=mapped("object",span.tailId);span.repeatIds=(span.repeatIds||[]).map(value=>mapped("object",value));}
 out.rows=out.rows.concat(next.rows);out.events=out.events.concat(next.events);out.objects=out.objects.concat(next.objects);out.midiSources=out.midiSources.concat(next.midiSources);out.beatmaps=out.beatmaps.concat(next.beatmaps);if(next.sliderSpans?.length)out.sliderSpans=[...out.sliderSpans||[],...next.sliderSpans];validate(out);return out;
}
class History{
 constructor(p){this.project=clone(validate(p));this.undoStack=[];this.redoStack=[];this.revision=0;}
 commit(name,before,after){validate(after);this.project=after;if(JSON.stringify(before)===JSON.stringify(after))return false;
  this.undoStack.push({name,before,after});if(this.undoStack.length>35)this.undoStack.shift();this.redoStack=[];this.revision++;return true;}
 undo(){const e=this.undoStack.pop();if(e){this.redoStack.push(e);this.project=e.before;this.revision++;}return !!e;}
 redo(){const e=this.redoStack.pop();if(e){this.undoStack.push(e);this.project=e.after;this.revision++;}return !!e;}
}
function demo(){
 const p=project("Sample editing • demo");p.demo=true;
 p.rows=["normal-hitnormal.wav","soft-hitclap.wav","drum-hitwhistle.wav","normal-hitfinish.wav"].map((name,i)=>({id:id("sample"),name,assetKey:null,note:[35,38,42,49][i],channel:10,muted:false,solo:false}));
 p.objects=Array.from({length:65},(_,i)=>({id:"demo-object-"+i,timeMs:i*125,role:"circle",beatmapId:"demo"}));
 [[0,500,1000,1500,2000,2500,3000,3500,4000,4500,5000,5500,6000,6500,7000,7500],[500,1500,2500,3500,4500,5500,6500,7500],Array.from({length:32},(_,i)=>i*250),[0,2000,4000,6000]].forEach((times,r)=>times.forEach((t,i)=>p.events.push({id:id("event"),rowId:p.rows[r].id,timeMs:t,durationMs:125,volume:r===2?(i%2?42:65):r===1?(i%2?70:90):85,muted:false,...resolveOwner(p,t),source:null,performance:{note:p.rows[r].note,channel:10,velocity:r===2?65:108,releaseVelocity:0}})));
 return p;
}
function songPreviewVolume(p,volume=audioSettings(p.audio).volume){return p.audio?.muted||p.rows.some(r=>r.solo)?0:volume;}
function audioSettings(audio){return {volume:audio?.volume??100,offsetMs:audio?.offsetMs??0};}
function setAudioSettings(p,changes){
 const out=clone(p);if(!out.audio)return out;
 const next={...audioSettings(out.audio),...changes};
 finite(next.volume,"song volume");finite(next.offsetMs,"song offset");
 if(next.volume<0||next.volume>100)throw Error("Song volume must be between 0 and 100%.");
 if(Math.abs(next.offsetMs)>86400000)throw Error("Song offset must be within 24 hours.");
 Object.assign(out.audio,next);return out;
}
function songPlayback(audio,timelineMs,durationMs){
 const sourceMs=timelineMs-audioSettings(audio).offsetMs;
 if(sourceMs>=durationMs)return null;
 return {delaySeconds:Math.max(0,-sourceMs)/1000,sourceSeconds:Math.max(0,sourceMs)/1000};
}

// Row order is presentation state; events retain stable row IDs and mappings.
function laneLabel(row){return row?.label||row?.name||"Unnamed lane";}
function renameLane(p,rowId,label){if(typeof label!=="string"||label.length>128)throw Error("Lane names must be at most 128 characters.");const out=clone(p),row=out.rows.find(r=>r.id===rowId);if(!row)return out;const value=label.trim();if(value&&value!==row.name)row.label=value;else delete row.label;return out;}
function addLane(p,template){const out=clone(p),row={id:id("sample"),name:template?.name||"normal-hitnormal.wav",assetKey:template?.assetKey||null,note:template?.note??35,channel:template?.channel??10,muted:false,solo:false,label:"New lane"};out.rows.push(row);return out;}
function deleteLane(p,rowId){const out=clone(p);out.rows=out.rows.filter(r=>r.id!==rowId);out.events=out.events.filter(e=>e.rowId!==rowId);return out;}
function reorderRow(p,rowId,targetId,after=false){
 const out=clone(p),from=out.rows.findIndex(r=>r.id===rowId);
 if(from<0||rowId===targetId||!out.rows.some(r=>r.id===targetId))return out;
 const [row]=out.rows.splice(from,1),to=out.rows.findIndex(r=>r.id===targetId);
 out.rows.splice(to+(after?1:0),0,row);return out;
}
function cycleSettings(p){return {leftMs:0,rightMs:4000,enabled:false,...p.cycle};}
function setCycle(p,changes){
 const out=clone(p),c={...cycleSettings(p),...changes};
 for(const k of ["leftMs","rightMs"]){finite(c[k],k);if(c[k]<0||c[k]>86400000)throw Error("Loop positions must be within 0 and 86400000 ms.");}
 c.enabled=!!c.enabled;out.cycle=c;return out;
}
// A transport leg is a continuous piece of timeline, expressed against an
// unwrapped audio clock. End points are exclusive so boundary hits play once.
function transportLeg(start,elapsed,c,end){
 elapsed=Math.max(0,elapsed);let origin=0,pos=start,boundary=end;
 const l=c.leftMs,r=c.rightMs;
 if(c.enabled&&l<r&&start<r){
  const first=r-start;
  if(elapsed<first){boundary=r;}
  else{const lap=Math.floor((elapsed-first)/(r-l));origin=first+lap*(r-l);pos=l;boundary=r;}
 }else if(c.enabled&&r<l&&start<l){
  if(start>=r){pos=l;}
  else if(elapsed<r-start){boundary=r;}
  else{origin=r-start;pos=l;}
 }
 return {elapsedStart:origin,elapsedEnd:origin+Math.max(0,boundary-pos),startMs:pos,endMs:boundary};
}
function transportPosition(start,elapsed,c,end){const leg=transportLeg(start,elapsed,c,end);return Math.min(leg.endMs,leg.startMs+Math.max(0,elapsed)-leg.elapsedStart);}
function transportSlices(start,from,to,c,end){
 const out=[];let at=Math.max(0,from);
 for(let n=0;at<to&&n<10000;n++){
  const leg=transportLeg(start,at,c,end),until=Math.min(to,leg.elapsedEnd);
  if(until<=at)break;
  out.push({...leg,fromMs:leg.startMs+at-leg.elapsedStart,toMs:leg.startMs+until-leg.elapsedStart,fromElapsed:at,toElapsed:until});at=until;
 }return out;
}

function duration(p){let end=Math.max(8000,(p.audio?.durationMs||0)+audioSettings(p.audio).offsetMs);for(const e of p.events)end=Math.max(end,e.timeMs+e.durationMs);for(const o of p.objects)end=Math.max(end,o.timeMs);return end;}
function timingPoints(p){
 if(!p.beatmaps.length)return p.tempos.map(t=>({timeMs:t.timeMs,red:true,value:t.bpm,meter:signatureMap(p).filter(s=>s.timeMs<=t.timeMs).at(-1)?.numerator||t.meter||4,set:0,index:0,volume:100,effects:0}));
 const out=[];let inside=false;for(const line of p.beatmaps[0].lines){if(/^\[.+\]$/.test(line.trim()))inside=line.trim()==="[TimingPoints]";else if(inside&&line.trim()&&!line.trim().startsWith("//")){const a=line.split(",").map(Number);if(a.length<2)continue;const red=a[6]!==0,signature=signatureMap(p).find(t=>t.timeMs===a[0]);out.push({timeMs:a[0],red,value:red?60000/a[1]:-100/a[1],meter:red&&signature?.denominator===4?signature.numerator:a[2]||4,set:a[3]??0,index:a[4]??0,volume:a[5]??100,effects:a[7]||0,rawBeat:a[1]});}}
 return out;
}
function validateTimingPoints(points){
 if(!Array.isArray(points)||!points.length||points.length>100000)throw Error("Keep at least one timing point.");
 const sorted=clone(points).sort((a,b)=>a.timeMs-b.timeMs),reds=new Set();
 for(const t of sorted){finite(t.timeMs,"timing offset");finite(t.value,"BPM or slider velocity");if(Math.abs(t.timeMs)>86400000||typeof t.red!=="boolean"||t.value<=0||t.value>(t.red?6000:10)||(!t.red&&t.value<.1))throw Error("Timing values need a valid offset, BPM above 0 through 6000, or slider velocity 0.1-10.");
 for(const [key,min,max] of [["meter",1,255],["set",0,3],["index",0,100000],["volume",0,100],["effects",0,2147483647]])if(!Number.isInteger(t[key])||t[key]<min||t[key]>max)throw Error("Invalid timing point "+key+".");
 if(t.red){if(reds.has(t.timeMs))throw Error("Two tempo points cannot share the same offset.");reds.add(t.timeMs);}}
 if(!reds.size)throw Error("Keep at least one red tempo point.");if(!sorted[0].red)throw Error("The first timing point must be a red tempo point.");return sorted;
}
function setTimingPoints(p,points){
 const sorted=validateTimingPoints(points);if(p.beatmaps.length)throw Error("Beatmap timing must be applied through the beatmap adapter.");if(sorted.some(t=>!t.red))throw Error("Inherited points require an imported dawsu! beatmap.");
 const out=clone(p);out.tempos=sorted.map(t=>({timeMs:t.timeMs,bpm:t.value,meter:t.meter}));out.timeSignatures=clone(signatureMap(p));for(const t of sorted){const current=signatureMap(out).filter(s=>s.timeMs<=t.timeMs).at(-1)||signatureMap(out)[0];if(current.numerator!==t.meter)out.timeSignatures=signatureMap({...out,timeSignatures:[...out.timeSignatures,{timeMs:t.timeMs,numerator:t.meter,denominator:current.denominator}]});}return out;
}
// Default assets are preview-only: never replace the row's custom asset or export name.
function defaultSampleName(row,event){
 if(!row||event?.source?.explicitFilename)return null;
 const m=/^(normal|soft|drum)-hit(normal|whistle|finish|clap)(?:\d*)\.(?:wav|ogg|mp3)$/i.exec(row.name||"");
 return m?m[1].toLowerCase()+"-hit"+m[2].toLowerCase()+".wav":null;
}
function sampleAsset(row,event){return row?.assetKey||(defaultSampleName(row,event)?"osu-default:"+defaultSampleName(row,event):null);}
const api={laneLabel,renameLane,addLane,deleteLane,appendProject,minValue,maxValue,gridSegments,gridBeats,gridNeighbor,timingPoints,validateTimingPoints,setTimingPoints,defaultSampleName,sampleAsset,signatureMap,setSignatures,signatureSegments,beatPosition,sliderSpans,reorderRow,cycleSettings,setCycle,transportLeg,transportPosition,transportSlices,clone,clamp,id,project,segments,timeToBeat,beatToTime,snapTime,resolveOwner,ownerCandidates,validate,setVolumes,ramp,move,paint,duplicate,remove,mute,snapSelected,assign,rangeEdit,bulk,History,demo,audioSettings,setAudioSettings,songPreviewVolume,songPlayback,duration};
if(typeof module!=="undefined")module.exports=api;else root.HSE=api;
})(globalThis);
