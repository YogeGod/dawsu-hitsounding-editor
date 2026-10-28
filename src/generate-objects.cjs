"use strict";
const H=require('./model'),F=require('./formats.cjs');
function audioFilename(p){return String(p.audio?.name||'song.wav').replace(/[\\/:*?"<>|\r\n]/g,'_');}
function candidates(p,ids=null){const allowed=ids==null?null:new Set(ids),map=p.beatmaps[0],objects=new Map(p.objects.filter(o=>o.beatmapId===map?.id).map(o=>[o.id,o]));return p.events.filter(e=>(!allowed||allowed.has(e.id))&&(!objects.has(e.ownerId)||Math.abs(objects.get(e.ownerId).timeMs-e.timeMs)>.001));}
function generate(p,options={}){
 H.validate(p);if(p.beatmaps.length>1)throw Error('Generate objects requires at most one beatmap.');
 const x=options.x??256,y=options.y??192;if(!Number.isInteger(x)||x<0||x>512||!Number.isInteger(y)||y<0||y>384)throw Error('Object position must be within 512 by 384.');
 const events=candidates(p,options.ids);if(!events.length)return {project:H.clone(p),created:0,assigned:0,rounded:0};
 if(events.some(e=>e.timeMs<0||e.timeMs>86400000))throw Error('Generate objects requires sound times between 0 and 86400000 ms.');
 const q=H.clone(p);let map=q.beatmaps[0];
 if(!map){
  const signatures=H.signatureMap(p);if(signatures.some(s=>s.denominator!==4))throw Error('osu! beatmaps require time signatures with a beat unit of 4.');
  const offsets=[...new Set([...p.tempos.map(t=>t.timeMs),...signatures.map(s=>s.timeMs)])].sort((a,b)=>a-b);
  const points=offsets.map(time=>{const tempo=H.segments(p.tempos).filter(t=>t.timeMs<=time).at(-1)||H.segments(p.tempos)[0],sig=signatures.filter(s=>s.timeMs<=time).at(-1)||signatures[0];return [time,60000/tempo.bpm,sig.numerator,1,1,100,1,0].join(',');});
  const title=String(p.name||'Hitsounds').replace(/[\r\n]/g,' '),text=['osu file format v14','','[General]','AudioFilename: '+audioFilename(p),'AudioLeadIn: 0','PreviewTime: -1','Countdown: 0','SampleSet: Normal','Mode: 0','StackLeniency: 0.7','','[Editor]','DistanceSpacing: 1','BeatDivisor: 4','GridSize: 4','TimelineZoom: 1','','[Metadata]','Title:'+title,'TitleUnicode:'+title,'Artist:Unknown','ArtistUnicode:Unknown','Creator:Hitsound Editor','Version:Generated hitsounds','Source:','Tags:hitsounds','BeatmapID:0','BeatmapSetID:-1','','[Difficulty]','HPDrainRate:5','CircleSize:4','OverallDifficulty:5','ApproachRate:5','SliderMultiplier:1.4','SliderTickRate:1','','[Events]','','[TimingPoints]',...points,'','[Colours]','Combo1 : 237,118,170','','[HitObjects]',''].join('\r\n');
  map=F.osuRead(text,title).project.beatmaps[0];map.generated=true;map.originalRows=H.clone(q.rows);map.originalEvents=[];q.beatmaps=[map];q.objects=q.objects.filter(o=>o.beatmapId!=='demo');delete q.demo;
  map.originalTimeSignatures=H.clone(H.signatureMap(q));
 }
 if(map.importOffsetMs)throw Error('Cannot generate into a beatmap appended with a time offset.');
 const lines=map.lines,start=lines.findIndex(l=>l.trim()==='[HitObjects]');if(start<0)throw Error('Beatmap has no HitObjects section.');
 let at=start+1;while(at<lines.length&&!/^\[.+\]$/.test(lines[at].trim()))at++;
 const keys=new Set(events.map(e=>e.id)),groups=new Map();let rounded=0;
 for(const e of q.events)if(keys.has(e.id)){const time=Math.round(e.timeMs);if(time!==e.timeMs)rounded++;e.timeMs=time;if(!groups.has(time))groups.set(time,[]);groups.get(time).push(e);}
 let reused=0;const added=[];for(const [time,sounds] of [...groups].sort((a,b)=>a[0]-b[0])){const matches=q.objects.filter(o=>o.beatmapId===map.id&&o.role==='circle'&&o.timeMs===time);if(matches.length===1){reused++;for(const e of sounds){e.ownerId=matches[0].id;e.assignment='bound';}continue;}const object={id:H.id('generated-object'),timeMs:time,role:'circle',lineIndex:at+added.length,edgeIndex:0,beatmapId:map.id,generated:true};q.objects.push(object);added.push([x,y,time,1,0,'0:0:1:100:'].join(','));for(const e of sounds){e.ownerId=object.id;e.assignment='bound';}}
 lines.splice(at,0,...added);map.generatedObjectLines=[...(map.generatedObjectLines||[]),...q.objects.filter(o=>o.generated&&o.beatmapId===map.id).map(o=>o.lineIndex)].filter((v,i,a)=>a.indexOf(v)===i);map.text=lines.join(map.text.includes('\r\n')?'\r\n':'\n');
 H.validate(q);return {project:q,created:added.length,assigned:events.length,rounded,reused};
}
function editTiming(p,points){
 const sorted=H.validateTimingPoints(points),q=H.clone(p),map=q.beatmaps[0];if(!map?.generated)throw Error('Not a generated beatmap.');
 const start=map.lines.findIndex(l=>l.trim()==='[TimingPoints]');let end=start+1;while(end<map.lines.length&&!/^\[.+\]$/.test(map.lines[end].trim()))end++;
 const records=sorted.map(t=>[t.timeMs,t.red?60000/t.value:-100/t.value,t.meter,t.set,t.index,t.volume,t.red?1:0,t.effects].join(','));
 const delta=records.length+1-(end-start-1);map.lines.splice(start+1,end-start-1,...records,'');
 for(const object of q.objects)if(object.beatmapId===map.id&&object.lineIndex>=end)object.lineIndex+=delta;
 for(const e of [...q.events,...map.originalEvents])if(e.source?.beatmapId===map.id&&e.source.lineIndex>=end)e.source.lineIndex+=delta;
 map.generatedObjectLines=(map.generatedObjectLines||[]).map(i=>i>=end?i+delta:i);map.text=map.lines.join(map.text.includes('\r\n')?'\r\n':'\n');
 q.tempos=sorted.filter(t=>t.red).map(t=>({timeMs:t.timeMs,bpm:t.value,meter:t.meter}));q.timeSignatures=sorted.filter(t=>t.red).map(t=>({timeMs:t.timeMs,numerator:t.meter,denominator:4}));map.originalTimeSignatures=H.clone(q.timeSignatures);H.validate(q);return q;
}
// Export-only preparation: the caller retains the original editable project.
function rhythmGuide(p,options={}){
 H.validate(p);
 if(p.beatmaps.length>1)throw Error('Rhythm guide export requires at most one beatmap.');
 const original=p.beatmaps[0];
 if(!p.events.length)throw Error('Add hitsound events before exporting a rhythm guide.');
 if(!p.audio?.assetKey)throw Error('Load song audio before exporting a rhythm guide.');
 const name=String(options.name??'Rhythm guide').trim();
 if(!name||name.length>100||/[\r\n]/.test(name))throw Error('Difficulty name must contain 1-100 characters on one line.');
 if(options.newCombo!==undefined&&typeof options.newCombo!=='boolean')throw Error('Invalid new-combo option.');
 const source=H.clone(p);
 function field(lines,section,key,value){let at=lines.findIndex(l=>l.trim()===section);if(at<0){lines.push('',section);at=lines.length-1;}let end=at+1;while(end<lines.length&&!/^\[.+\]$/.test(lines[end].trim()))end++;const found=lines.findIndex((l,i)=>i>at&&i<end&&l.trimStart().startsWith(key+':'));if(found>=0)lines[found]=key+': '+value;else lines.splice(end,0,key+': '+value);}
 // Build a fresh object section in the export copy, retaining timing and map metadata.
 source.objects=[];source.sliderSpans=[];
 for(const event of source.events){event.ownerId=null;event.assignment='unassigned';}
 if(source.beatmaps[0]){const map=source.beatmaps[0],at=map.lines.findIndex(line=>line.trim()==='[HitObjects]');if(at<0)throw Error('Beatmap has no HitObjects section.');let end=at+1;while(end<map.lines.length&&!/^\[.+\]$/.test(map.lines[end].trim()))end++;map.lines.splice(at+1,end-at-1);
  map.originalEvents=[];map.originalRows=H.clone(source.rows);map.generatedObjectLines=[];
  field(map.lines,'[General]','Mode',0);field(map.lines,'[General]','AudioFilename',audioFilename(source));field(map.lines,'[Metadata]','Version',name);field(map.lines,'[Metadata]','BeatmapID',0);map.audioFilename=audioFilename(source);map.text=map.lines.join(map.text.includes('\r\n')?'\r\n':'\n');map.mode=0;
  if(original.archiveEntry){const entry=original.archiveEntry.replace(/\\/g,'/'),slash=entry.lastIndexOf('/'),base=entry.slice(slash+1).replace(/\.osu$/i,''),safeName=name.replace(/[\\/:*?"<>|]/g,'_');map.archiveEntry=entry.slice(0,slash+1)+base+' ['+safeName+'].osu';}else map.archiveEntry='hitsounds-hs.osu';
 }

 const result=generate(source,{x:options.x??256,y:options.y??192}),map=result.project.beatmaps[0];
 field(map.lines,'[Metadata]','Version',name);
 for(const object of result.project.objects.filter(o=>o.beatmapId===map.id&&o.generated)){const fields=map.lines[object.lineIndex].split(',');fields[3]=String(options.newCombo===false?1:5);map.lines[object.lineIndex]=fields.join(',');}
 map.generated=true;map.rhythmGuide=true;map.text=map.lines.join(map.text.includes('\r\n')?'\r\n':'\n');
 return result;
}
module.exports={generate,candidates,audioFilename,editTiming,rhythmGuide};
