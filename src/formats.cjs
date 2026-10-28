"use strict";
const fs=require("fs"),path=require("path"),{parseMidi,writeMidi}=require("midi-file"),H=require("./model");
function midiRead(bytes,name="MIDI"){
 const b=Buffer.from(bytes);if(b.length<14||b.toString("ascii",0,4)!=="MThd")throw Error("This is not a Standard MIDI File.");
 const hl=b.readUInt32BE(4);if(hl<6||8+hl>b.length)throw Error("Truncated MIDI header.");
 const division=b.readUInt16BE(12);if(division&32768)throw Error("SMPTE time division is not supported yet. Export with PPQ timing.");
 if(b.readUInt16BE(8)>1)throw Error("Type 2 MIDI needs independent sequences and cannot be imported as one timeline.");
 let pos=8+hl;while(pos<b.length){if(pos+8>b.length)throw Error("Truncated MIDI chunk.");let n=b.readUInt32BE(pos+4);if(pos+8+n>b.length)throw Error("Truncated MIDI track.");pos+=8+n;}
 const raw=parseMidi(b);for(const tr of raw.tracks)for(const e of tr)if(e.data)e.data=Array.from(e.data);const ppq=raw.header.ticksPerBeat;if(!ppq)throw Error("Invalid MIDI timing.");
 const all=[];raw.tracks.forEach((tr,ti)=>{let tick=0;tr.forEach((e,ei)=>{tick+=e.deltaTime;all.push({...e,tick,ti,ei});});});
 const tempo=[{tick:0,us:500000}];for(const e of all.filter(e=>e.type==="setTempo").sort((a,b)=>a.tick-b.tick)){if(tempo.at(-1).tick===e.tick)tempo.pop();tempo.push({tick:e.tick,us:e.microsecondsPerBeat});}
 let ms=0;tempo.forEach((t,i)=>{if(i)ms+=(t.tick-tempo[i-1].tick)*tempo[i-1].us/ppq/1000;t.ms=ms;});
 const toMs=t=>{let s=tempo[0];for(const v of tempo){if(v.tick>t)break;s=v;}return s.ms+(t-s.tick)*s.us/ppq/1000;};
 const p=H.project(name),sid=H.id("midi");p.tempos=tempo.map(t=>({timeMs:t.ms,bpm:60000000/t.us,meter:4}));p.midiSources=[{id:sid,name,bytes:b.toString("base64"),raw,ppq}];
 p.timeSignatures=H.signatureMap({...p,timeSignatures:[{timeMs:0,numerator:4,denominator:4},...all.filter(e=>e.type==="timeSignature").sort((a,b)=>a.tick-b.tick).map(e=>({timeMs:toMs(e.tick),numerator:e.numerator,denominator:e.denominator,metronome:e.metronome,thirtyseconds:e.thirtyseconds}))]});
 const rows=new Map(),pending=new Map(),warnings=[];let serial=0;
 raw.tracks.forEach((tr,ti)=>{let tick=0;const trackName=tr.find(e=>e.type==="trackName")?.text||"Track "+(ti+1);
  for(const e of tr){tick+=e.deltaTime;const key=ti+":"+e.channel+":"+e.noteNumber;
   if(e.type==="noteOn"&&e.velocity>0){const rk=key;if(!rows.has(rk)){const r={id:H.id("row"),name:trackName+" · "+e.noteNumber,note:e.noteNumber,channel:e.channel+1,assetKey:null,muted:false,solo:false};rows.set(rk,r);p.rows.push(r);}
    const event={id:H.id("note"),rowId:rows.get(rk).id,timeMs:toMs(tick),durationMs:0,volume:Math.max(1,Math.round(e.velocity*100/127)),muted:false,ownerId:null,assignment:"unassigned",source:{sourceId:sid,track:ti,startTick:tick,ordinal:serial++},performance:{note:e.noteNumber,channel:e.channel+1,velocity:e.velocity,releaseVelocity:0}};
    if(!pending.has(key))pending.set(key,[]);pending.get(key).push(event);p.events.push(event);
   }else if(e.type==="noteOff"||(e.type==="noteOn"&&e.velocity===0)){const ev=pending.get(key)?.shift();if(ev){ev.durationMs=Math.max(0,toMs(tick)-ev.timeMs);ev.performance.releaseVelocity=e.velocity;ev.source.endTick=tick;}else warnings.push("Unmatched release in track "+(ti+1)+" at tick "+tick);}
  }
 });
 for(const q of pending.values())for(const e of q)warnings.push("Missing release: note "+e.performance.note+" at "+Math.round(e.timeMs)+" ms. Duration is zero.");
 p.rows.sort((a,b)=>a.note-b.note||a.channel-b.channel);return {project:p,warnings};
}
function midiWrite(p){
 H.validate(p);const ppq=960,warnings=[],tempos=H.segments(p.tempos); // origin is absolute audio time, with a valid MIDI tempo at zero
 const tickAt=ms=>Math.max(0,Math.round((H.timeToBeat(ms,p.tempos)-H.timeToBeat(0,p.tempos))*ppq));
 const tempoAt0=tempos.filter(t=>t.timeMs<=0).at(-1)||tempos[0];
 const metadata=[{tick:0,type:"setTempo",microsecondsPerBeat:Math.round(60000000/tempoAt0.bpm)}];
 for(const t of tempos.filter(t=>t.timeMs>0))metadata.push({tick:tickAt(t.timeMs),type:"setTempo",microsecondsPerBeat:Math.round(60000000/t.bpm)});
 const signatures=H.signatureMap(p),at0=signatures.filter(s=>s.timeMs<=0).at(-1)||signatures[0];
 for(const point of [{...at0,timeMs:0},...signatures.filter(s=>s.timeMs>0)])metadata.push({tick:tickAt(point.timeMs),type:"timeSignature",numerator:point.numerator,denominator:point.denominator,metronome:point.metronome??24,thirtyseconds:point.thirtyseconds??8});
 const tracks=[metadata],keys=new Map();
 function tr(key){if(!keys.has(key)){keys.set(key,tracks.length);tracks.push([]);}return tracks[keys.get(key)];}
 // Keep non-note MIDI events and their original absolute times when source timing remains unchanged.
 for(const s of p.midiSources||[]){let sourceTempo=[{timeMs:0,bpm:120}],all=[];s.raw.tracks.forEach((es,ti)=>{let tick=0;for(const e of es){tick+=e.deltaTime;all.push({...e,ti,tick});}});const tempos=all.filter(e=>e.type==="setTempo").sort((a,b)=>a.tick-b.tick);let at=0,last=0,bpm=120;
  for(const e of tempos){at+=(e.tick-last)*60000/bpm/s.ppq;sourceTempo.push({timeMs:at,bpm:60000000/e.microsecondsPerBeat});last=e.tick;bpm=60000000/e.microsecondsPerBeat;}
  for(const e of all){if(["noteOn","noteOff","setTempo","timeSignature","endOfTrack"].includes(e.type))continue;const ev={...e,tick:tickAt(H.beatToTime(e.tick/s.ppq,sourceTempo)+(s.offsetMs||0))};delete ev.ti;delete ev.deltaTime;tr(s.id+":"+e.ti).push(ev);}
 }
 for(const e of p.events){if(!e.performance){warnings.push("An event without MIDI note mapping was skipped.");continue;}if(e.muted){warnings.push("Muted events were omitted from MIDI.");continue;}
  const startTick=tickAt(e.timeMs),endTick=tickAt(e.timeMs+e.durationMs),perf=e.performance,events=tr(e.source?e.source.sourceId+":"+e.source.track:"new:"+e.rowId);
  events.push({tick:startTick,type:"noteOn",channel:perf.channel-1,noteNumber:perf.note,velocity:H.clamp(Math.round(perf.velocity),1,127),order:endTick===startTick?.5:1});
  events.push({tick:endTick,type:"noteOff",channel:perf.channel-1,noteNumber:perf.note,velocity:perf.releaseVelocity||0,order:endTick===startTick?.5:-1});
 }
 const encoded=tracks.map(es=>{es.sort((a,b)=>a.tick-b.tick||(a.order||0)-(b.order||0));let prev=0;const out=es.map(e=>{const q={...e,deltaTime:e.tick-prev};prev=e.tick;delete q.tick;delete q.order;return q;});out.push({deltaTime:0,type:"endOfTrack"});return out;});
 return {bytes:Buffer.from(writeMidi({header:{format:1,ticksPerBeat:ppq},tracks:encoded})),warnings:[...new Set(warnings)]};
}
function sampleFieldIndex(type){return type&2?10:type&8?6:5;}
function osuRead(text,name="Beatmap"){
 text=text.replace(/^\uFEFF/,"");if(!/^osu file format v\d+/m.test(text))throw Error("Not an osu beatmap.");
 const lines=text.split(/\r?\n/),p=H.project(name),bid=H.id("beatmap"),timings=[],hitLines=[];let section="",audioFilename="",sliderMultiplier=1.4,mode=0,defaultSet=1;
 lines.forEach((line,i)=>{if(/^\[.+\]$/.test(line.trim()))section=line.trim();else if(section==="[TimingPoints]"&&line&&!line.startsWith("//")){const a=line.split(",").map(Number);if(a.length>=2)timings.push({time:a[0],beat:a[1],meter:a[2]||4,set:a[3]??0,index:a[4]??0,volume:a[5]??100,red:a[6]!==0});}else if(section==="[HitObjects]"&&line&&!line.startsWith("//"))hitLines.push({line,i});else if(section==="[Editor]"&&/^Bookmarks\s*:/i.test(line)){p.bookmarks=line.slice(line.indexOf(":")+1).split(",").map(Number).filter(n=>Number.isFinite(n)&&n>=0&&n<=86400000&&line.slice(line.indexOf(":")+1).trim()).map((timeMs,i)=>({id:H.id("bookmark"),timeMs,label:"Bookmark "+(i+1)}));}else if(section==="[General]"&&line.startsWith("AudioFilename:"))audioFilename=line.slice(14).trim();else if(section==="[General]"&&line.startsWith("SampleSet:"))defaultSet=({Normal:1,Soft:2,Drum:3}[line.slice(10).trim()]||1);else if(section==="[General]"&&line.startsWith("Mode:"))mode=Number(line.slice(5));else if(section==="[Difficulty]"&&line.startsWith("SliderMultiplier:"))sliderMultiplier=Number(line.slice(17));});
 timings.sort((a,b)=>a.time-b.time);p.tempos=timings.filter(t=>t.red&&t.beat>0).map(t=>({timeMs:t.time,bpm:60000/t.beat,meter:t.meter}));if(!p.tempos.length)p.tempos=[{timeMs:0,bpm:120,meter:4}];
 p.timeSignatures=H.signatureMap(p);p.sliderSpans=[];
 const rows=new Map(),warnings=[];
 function stateAt(t){let s={set:defaultSet,index:0,volume:100,beat:500,sv:1};for(const tp of timings){if(tp.time>t)break;if(tp.red){s.beat=tp.beat;s.sv=1;}else s.sv=H.clamp(-100/tp.beat,.1,10);s.set=tp.set||defaultSet;s.index=tp.index;s.volume=tp.volume;}return s;}
 function layer(obj,kind,set,index,volume,filename,field){const bank=["","normal","soft","drum"][set]||"normal";const sample=filename||bank+"-hit"+kind+(index>1?index:"")+".wav";const key=sample.toLowerCase();
  if(!rows.has(key)){const r={id:H.id("sample"),name:sample,assetKey:null,note:{normal:35,whistle:42,finish:49,clap:38}[kind]||35,channel:10,muted:false,solo:false};rows.set(key,r);p.rows.push(r);}
  p.events.push({id:H.id("sound"),rowId:rows.get(key).id,timeMs:obj.timeMs,durationMs:0,volume:H.clamp(volume,0,100),muted:false,ownerId:obj.id,assignment:"bound",source:{beatmapId:bid,lineIndex:obj.lineIndex,field,kind,explicitFilename:!!filename,originalSample:sample,originalVolume:volume,sampleIndex:index},performance:{note:rows.get(key).note,channel:10,velocity:Math.max(1,Math.round(volume*127/100)),releaseVelocity:0}});
 }
 for(const {line,i} of hitLines){const a=line.split(","),t=Number(a[2]),type=Number(a[3]),sound=Number(a[4]);if(!Number.isFinite(t))continue;const st=stateAt(t);
  let hs=a[sampleFieldIndex(type)]||"0:0:0:0:",tail=null;if(type&128){const j=hs.indexOf(":");tail=Number(hs.slice(0,j));hs=hs.slice(j+1);}else if(type&8)tail=Number(a[5]);const h=hs.split(":");const ns=Number(h[0])||0,as=Number(h[1])||0,file=h.slice(4).join(":");
  let edges=[{time:t,edge:0,bits:sound,sets:[ns,as],role:type&2?"slider-head":type&128?"hold":"circle"}];
  if(type&2){const repeats=Number(a[6]),length=Number(a[7]),span=length/(sliderMultiplier*100*st.sv)*st.beat;const bits=(a[8]||"").split("|").map(Number),sets=(a[9]||"").split("|").map(v=>v.split(":").map(Number));edges=Array.from({length:Math.min(10000,repeats+1)},(_,k)=>({time:t+k*span,edge:k,bits:bits[k]??sound,sets:[sets[k]?.[0]||ns,sets[k]?.[1]||as],role:k===0?"slider-head":k===repeats?"slider-tail":"slider-repeat"}));p.sliderSpans.push({id:bid+":"+i+":body",beatmapId:bid,lineIndex:i,startMs:t,endMs:t+repeats*span,spanMs:span,repeats,headId:bid+":"+i+":0",tailId:bid+":"+i+":"+repeats,repeatIds:Array.from({length:Math.max(0,Math.min(9999,repeats-1))},(_,k)=>bid+":"+i+":"+(k+1))});warnings.push("Slider body sliding/whistle loops are preserved in the original beatmap but are not editable as events.");}
  if(type&8){edges=[{time:tail,edge:0,bits:sound,sets:[ns,as],role:"spinner-end"}];}
  for(const edge of edges){const es=stateAt(edge.time),v=Number(h[3])||es.volume,o={id:bid+":"+i+":"+edge.edge,timeMs:edge.time,role:edge.role,lineIndex:i,edgeIndex:edge.edge,beatmapId:bid};p.objects.push(o);
   const normalBank=edge.sets[0]||es.set,additionBank=edge.sets[1]||normalBank;
   layer(o,"normal",normalBank,Number(h[2])||es.index,v,file,"sample");for(const [bit,kind] of [[2,"whistle"],[4,"finish"],[8,"clap"]])if(!file&&(edge.bits&bit))layer(o,kind,additionBank,Number(h[2])||es.index,v,"","sample");
  }
 }
 p.beatmaps=[{id:bid,name,text,lines,audioFilename,mode,originalTimeSignatures:H.clone(H.signatureMap(p)),originalBookmarks:H.clone(p.bookmarks),originalEvents:H.clone(p.events),originalRows:H.clone(p.rows)}];return {project:p,warnings:[...new Set(warnings)]};
}
function osuWrite(p,options={}){
 if(p.beatmaps.length!==1)return {conflicts:["Choose a project with exactly one imported beatmap."]};
 if(p.beatmaps[0].importOffsetMs)return {conflicts:["This beatmap was appended with a time offset. Save the editor project or export MIDI; exporting it as a beatmap would discard that offset."]};
 const renderRequests=[],map=p.beatmaps[0],conflicts=[],lines=[...map.lines],orig=new Map(map.originalEvents.map(e=>[e.id,e])),originalRows=new Map(map.originalRows.map(r=>[r.id,r])),rowMap=new Map(p.rows.map(r=>[r.id,r])),objectMap=new Map(p.objects.filter(o=>o.beatmapId===map.id).map(o=>[o.id,o])),groups=new Map(),oldGroups=new Map(),changes=new Map();
 if(map.generated){if(!p.audio?.assetKey)return {conflicts:["Load song audio before exporting the generated beatmap."]};const audioLine=lines.findIndex(l=>l.startsWith("AudioFilename:"));lines[audioLine]="AudioFilename: "+require("./generate-objects.cjs").audioFilename(p);}
 const generatedLines=new Set(map.generatedObjectLines||[]);
 const sourceSignatures=map.originalTimeSignatures||osuRead(map.text).project.timeSignatures,currentSignatures=H.signatureMap(p);
 const shape=xs=>xs.map(s=>[s.timeMs,s.numerator,s.denominator]);
 if(JSON.stringify(shape(sourceSignatures))!==JSON.stringify(shape(currentSignatures))){
  const red=[];let inTiming=false;for(let i=0;i<lines.length;i++){if(/^\[.+\]$/.test(lines[i].trim()))inTiming=lines[i].trim()==="[TimingPoints]";else if(inTiming&&lines[i]&&!lines[i].startsWith("//")){const a=lines[i].split(",");if(Number(a[1])>0&&a[6]!=="0")red.push({i,timeMs:Number(a[0]),a});}}
  if(currentSignatures.length!==red.length||currentSignatures.some((s,i)=>s.denominator!==4||Math.abs(s.timeMs-red[i].timeMs)>.000001))conflicts.push("Beatmap export supports time signatures with beat unit 4 at the existing red timing points. Save the project or export MIDI for other time-signature changes.");
  else for(let i=0;i<red.length;i++){const a=red[i].a;a[2]=String(currentSignatures[i].numerator);lines[red[i].i]=a.join(",");}
 }
 for(const e of map.originalEvents){if(!oldGroups.has(e.ownerId))oldGroups.set(e.ownerId,[]);oldGroups.get(e.ownerId).push(e);}
 for(const e of p.events){if(!e.ownerId||!objectMap.has(e.ownerId)){conflicts.push("Sound at "+Math.round(e.timeMs)+" ms is unassigned to a gameplay object.");continue;}const obj=objectMap.get(e.ownerId);if(Math.abs(e.timeMs-obj.timeMs)>.001){conflicts.push("Sound at "+Math.round(e.timeMs)+" ms must match its assigned object time.");continue;}if(e.muted&&!options.renderCircles){conflicts.push("Muted events need an explicit sample change; volume zero means inherited volume.");continue;}if(!groups.has(e.ownerId))groups.set(e.ownerId,[]);groups.get(e.ownerId).push(e);}
 // Recover effective indices from the source for older saved projects as well.
 // Auto is shared by a slider but resolves independently at each edge.
 const sourceIndices=[];let inSourceTiming=false;
 for(const line of map.lines){if(/^\[.+\]$/.test(line.trim()))inSourceTiming=line.trim()==="[TimingPoints]";else if(inSourceTiming&&line.trim()&&!line.trim().startsWith("//")){const a=line.split(",");if(Number.isFinite(Number(a[0])))sourceIndices.push({time:Number(a[0]),index:Number(a[4])||0});}}
 sourceIndices.sort((a,b)=>a.time-b.time);
 function inheritedIndex(e){let index=0;for(const point of sourceIndices){if(point.time>e.timeMs)break;index=point.index;}return index;}
 function sourceIndex(e){const o=objectMap.get(e.ownerId);if(!o)return 1;const a=map.lines[o.lineIndex].split(","),type=Number(a[3]);let sample=a[sampleFieldIndex(type)]||"";if(type&128)sample=sample.slice(sample.indexOf(":")+1);return Number(sample.split(":")[2])||inheritedIndex(e);}
 function description(e){const row=rowMap.get(e.rowId);if(!row){conflicts.push("An event has no sample row.");return null;}const m=/^(normal|soft|drum)-hit(normal|whistle|finish|clap)(\d*)\.(wav|ogg|mp3)$/i.exec(row.name);if(e.source?.kind==="normal"&&e.source.explicitFilename)return {kind:"normal",bank:1,index:null,filename:row.name};if(m)return {kind:m[2].toLowerCase(),bank:{normal:1,soft:2,drum:3}[m[1].toLowerCase()],index:row.sampleMapping?(m[3]?Number(m[3]):1):m[3]?Number(m[3]):sourceIndex(e),filename:""};if(!/\.(wav|ogg|mp3)$/i.test(row.name)){conflicts.push("Choose an audio sample for row: "+row.name);return null;}return {kind:"normal",bank:1,index:null,filename:row.name};}
 for(const [oid,obj] of objectMap){const es=groups.get(oid)||[],old=oldGroups.get(oid)||[];const unchanged=es.length===old.length&&es.every(e=>{const o=orig.get(e.id);return o&&o.ownerId===oid&&e.volume===o.volume&&e.muted===o.muted&&rowMap.get(e.rowId)?.name===originalRows.get(o.rowId)?.name&&rowMap.get(e.rowId)?.assetKey===originalRows.get(o.rowId)?.assetKey;});if(unchanged&&!generatedLines.has(obj.lineIndex))continue;
  if(options.renderCircles&&(Number(lines[obj.lineIndex].split(",")[3])&1)){
   const mark=conflicts.length,descriptors=es.map(description).filter(Boolean),normals=descriptors.filter(d=>d.kind==="normal"),adds=descriptors.filter(d=>d.kind!=="normal"),needsRender=generatedLines.has(obj.lineIndex)||es.some(e=>e.muted)||new Set(es.map(e=>e.volume)).size>1||descriptors.length!==es.length||normals.length!==1||new Set(adds.map(d=>d.kind)).size!==adds.length||new Set(adds.map(d=>d.bank)).size>1||new Set(descriptors.filter(d=>d.index!==null).map(d=>d.index)).size>1;conflicts.length=mark;
   if(needsRender){const layers=es.filter(e=>!e.muted&&e.volume>0).map(e=>({eventId:e.id,rowId:e.rowId,volume:e.volume}));renderRequests.push({ownerId:oid,lineIndex:obj.lineIndex,timeMs:obj.timeMs,layers});const name=options.rendered?.[oid];if(name){if(!/^hse-mix-[a-f0-9]{64}\.wav$/.test(name))throw Error("Invalid generated sample name.");const fields=lines[obj.lineIndex].split(",");while(fields.length<5)fields.push("");fields[5]="0:0:1:100:"+name;lines[obj.lineIndex]=fields.join(",");}continue;}
  }
  if(es.some(e=>e.muted)){conflicts.push("Muted slider, spinner or hold events need rendered edge support.");continue;}

  const volumeOnly=es.length===old.length&&es.every(e=>{const o=orig.get(e.id);return o&&o.ownerId===oid&&rowMap.get(e.rowId)?.name===originalRows.get(o.rowId)?.name&&rowMap.get(e.rowId)?.assetKey===originalRows.get(o.rowId)?.assetKey;});if(volumeOnly){if(!changes.has(obj.lineIndex))changes.set(obj.lineIndex,[]);changes.get(obj.lineIndex).push({events:es,obj,volumeOnly:true});continue;}
  const ds=es.map(description).filter(Boolean),normals=ds.filter(d=>d.kind==="normal"),adds=ds.filter(d=>d.kind!=="normal");
  if(normals.length!==1){conflicts.push("Object at "+Math.round(obj.timeMs)+" ms needs exactly one normal sample. Multiple custom layers need audio rendering.");continue;}
  if(new Set(adds.map(d=>d.kind)).size!==adds.length){conflicts.push("Object at "+Math.round(obj.timeMs)+" ms has duplicate additions that require rendered samples.");continue;}
  const banks=new Set(adds.map(d=>d.bank)),indices=new Set(ds.filter(d=>d.index!==null).map(d=>d.index));if(banks.size>1||indices.size>1){conflicts.push("Object at "+Math.round(obj.timeMs)+" ms uses incompatible sample banks or indices.");continue;}
  const normal=normals[0],bits=adds.reduce((n,d)=>n|{whistle:2,finish:4,clap:8}[d.kind],0),sample={normalBank:normal.bank,addBank:adds[0]?.bank||normal.bank,index:[...indices][0]||1,filename:normal.filename,bits,events:es,obj};
  if(!changes.has(obj.lineIndex))changes.set(obj.lineIndex,[]);changes.get(obj.lineIndex).push(sample);
 }
 for(const [li,edits] of changes){const a=lines[li].split(","),type=Number(a[3]),all=p.events.filter(e=>objectMap.get(e.ownerId)?.lineIndex===li&&!e.muted),old=map.originalEvents.filter(e=>e.source.lineIndex===li),volumes=new Set(all.map(e=>e.volume)),volumeChanged=all.some(e=>e.volume!==(orig.get(e.id)?.volume??oldGroups.get(e.ownerId)?.[0]?.volume));
  if(volumeChanged&&volumes.size!==1){conflicts.push("Independent layer/edge volumes at line "+(li+1)+" need rendered samples; this object has one shared volume.");continue;}
  const sampleIndex=sampleFieldIndex(type);let hs=a[sampleIndex]||"0:0:0:0:",prefix="";if(type&128){const ix=hs.indexOf(":");prefix=hs.slice(0,ix+1);hs=hs.slice(ix+1);}let parts=hs.includes(":")?hs.split(":"):["0","0","0","0",""];while(parts.length<5)parts.push("");
  if(volumeChanged)parts[3]=String(all[0]?.volume||100);
  const onlyVolume=edits.every(edit=>edit.events.length===(oldGroups.get(edit.obj.id)||[]).length&&edit.events.every(e=>orig.has(e.id)&&rowMap.get(e.rowId)?.name===originalRows.get(orig.get(e.id).rowId)?.name));
  if(!onlyVolume){
   const sampleEdits=edits.filter(e=>!e.volumeOnly),descriptions=all.map(description).filter(Boolean),indices=new Set(descriptions.filter(d=>d.index!==null).map(d=>d.index)),filenames=new Set(descriptions.filter(d=>d.kind==="normal").map(d=>d.filename));const keepAuto=Number(parts[2])===0&&descriptions.some(d=>d.index!==null)&&all.every(e=>{const d=description(e);return d&&(d.index===null||d.index===inheritedIndex(e));});
   if((indices.size>1&&!keepAuto)||filenames.size>1){conflicts.push("Different slider-edge sample indices or custom filenames require rendering.");continue;}
   if(!keepAuto)parts[2]=String([...indices][0]??1);parts[4]=[...filenames][0]||"";
   if(type&2){const repeat=Number(a[6]),edgeBits=(a[8]||"").split("|"),edgeSets=(a[9]||"").split("|");while(edgeBits.length<repeat+1)edgeBits.push(a[4]);while(edgeSets.length<repeat+1)edgeSets.push(parts[0]+":"+parts[1]);for(const e of sampleEdits){edgeBits[e.obj.edgeIndex]=String(e.bits);edgeSets[e.obj.edgeIndex]=e.normalBank+":"+e.addBank;}a[8]=edgeBits.join("|");a[9]=edgeSets.join("|");}
   else{a[4]=String(sampleEdits[0].bits);parts[0]=String(sampleEdits[0].normalBank);parts[1]=String(sampleEdits[0].addBank);}
  }
  while(a.length<sampleIndex)a.push("");a[sampleIndex]=prefix+parts.join(":");lines[li]=a.join(",");
 }
 const Bookmarks=require('./bookmarks'),originalBookmarks=map.originalBookmarks??osuRead(map.text).project.bookmarks,currentBookmarks=map.originalBookmarks!==undefined||p.bookmarks?.length?p.bookmarks:originalBookmarks;
 const bookmarkTimes=marks=>[...new Set(Bookmarks.list({bookmarks:marks}).map(b=>Math.round(b.timeMs)))].sort((a,b)=>a-b);
 if(JSON.stringify(bookmarkTimes(currentBookmarks))!==JSON.stringify(bookmarkTimes(originalBookmarks))){let start=lines.findIndex(l=>l.trim()==='[Editor]');if(start<0){lines.push('','[Editor]');start=lines.length-1;}let end=start+1;while(end<lines.length&&!/^\[.+\]$/.test(lines[end].trim()))end++;const at=lines.findIndex((l,i)=>i>start&&i<end&&/^Bookmarks\s*:/i.test(l));const value='Bookmarks: '+bookmarkTimes(currentBookmarks).join(',');if(at>=0)lines[at]=value;else lines.splice(end,0,value);}
 return {...(options.renderCircles?{renderRequests}:{}),conflicts:[...new Set(conflicts)],text:lines.every((v,i)=>v===map.lines[i])?map.text:lines.join(map.text.includes("\r\n")?"\r\n":"\n")};
}

function editTiming(p,points){
 const sorted=H.validateTimingPoints(points);if(p.beatmaps[0]?.generated)return require("./generate-objects.cjs").editTiming(p,sorted);if(p.beatmaps[0]?.generatedObjectLines?.length&&JSON.stringify(H.timingPoints(p))!==JSON.stringify(sorted))throw Error("Undo gameplay-object generation before changing timing points, then generate again. This keeps the original hitsound layers intact.");if(!p.beatmaps.length)return H.setTimingPoints(p,sorted);
 if(JSON.stringify(H.timingPoints(p))===JSON.stringify(sorted))return H.clone(p);
 const saved=osuWrite(p);if(saved.conflicts.length)throw Error("Resolve these beatmap edits before changing timing: "+saved.conflicts.join(" "));
 const lines=saved.text.split(/\r?\n/),start=lines.findIndex(l=>l.trim()==="[TimingPoints]");if(start<0)throw Error("Beatmap has no TimingPoints section.");let end=start+1;while(end<lines.length&&!/^\[.+\]$/.test(lines[end].trim()))end++;
 const comments=lines.slice(start+1,end).filter(l=>l.trim().startsWith("//"));
 const records=sorted.map(t=>{const beat=t.rawBeat!==undefined&&Math.abs((t.red?60000/t.rawBeat:-100/t.rawBeat)-t.value)<1e-10?t.rawBeat:t.red?60000/t.value:-100/t.value;return [t.timeMs,beat,t.meter,t.set,t.index,t.volume,t.red?1:0,t.effects].join(",");});
 lines.splice(start+1,end-start-1,...comments,...records,"");const fresh=osuRead(lines.join(saved.text.includes("\r\n")?"\r\n":"\n"),p.beatmaps[0].name).project;
 const oldRows=new Map(p.rows.map(r=>[r.name.toLowerCase(),r])),remap=new Map();for(const r of fresh.rows){const old=oldRows.get(r.name.toLowerCase());if(old){remap.set(r.id,old.id);Object.assign(r,H.clone(old));}}
 for(const e of fresh.events)e.rowId=remap.get(e.rowId)||e.rowId;
 // Preserve independent MIDI performance values across timing-derived sample regeneration.
 const keys=objects=>{const lineNumbers=new Map([...new Set(objects.map(o=>o.lineIndex))].sort((a,b)=>a-b).map((line,i)=>[line,i]));return new Map(objects.map(o=>[o.id,lineNumbers.get(o.lineIndex)+":"+o.edgeIndex]));};
 const oldObjects=keys(p.objects),newObjects=keys(fresh.objects);
 const performance=new Map(p.events.map(e=>[oldObjects.get(e.ownerId)+":"+e.source?.kind,e.performance]));for(const e of fresh.events){const perf=performance.get(newObjects.get(e.ownerId)+":"+e.source?.kind);if(perf)e.performance=H.clone(perf);}
 const rows=p.rows.map(r=>H.clone(r));for(const r of fresh.rows)if(!rows.some(old=>old.id===r.id))rows.push(r);
 fresh.beatmaps[0]={...H.clone(p.beatmaps[0]),...fresh.beatmaps[0],originalRows:H.clone(rows),originalEvents:H.clone(fresh.events)};
 const out={...H.clone(p),rows,events:fresh.events,objects:fresh.objects,sliderSpans:fresh.sliderSpans,tempos:fresh.tempos,timeSignatures:fresh.timeSignatures,beatmaps:fresh.beatmaps};H.validate(out);return out;
}

module.exports={editTiming,midiRead,midiWrite,osuRead,osuWrite};
