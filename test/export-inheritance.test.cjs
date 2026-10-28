const test=require('node:test'),assert=require('node:assert/strict'),F=require('../src/formats.cjs');
const source=`osu file format v14
[General]
SampleSet: Normal
[Difficulty]
SliderMultiplier:1
[TimingPoints]
0,500,4,1,2,80,1,0
500,-100,4,2,3,30,0,0
[HitObjects]
64,192,0,2,0,L|256:192,1,100,0|0,0:0|0:0,0:0:0:0:
256,192,0,8,0,750,0:0:0:0:
`;
function row(p,name){const r={...p.rows[0],id:'new-'+p.rows.length,name};p.rows.push(r);return r;}
function tail(p){return p.events.find(e=>p.objects.find(o=>o.id===e.ownerId)?.role==='slider-tail');}
function bankEdit(p){const e=tail(p);e.rowId=row(p,'drum-hitnormal3.wav').id;return e;}
function exported(p){const out=F.osuWrite(p);assert.deepEqual(out.conflicts,[]);assert.equal(out.text.split('[HitObjects]')[0],source.split('[HitObjects]')[0]);return out.text;}
test('Auto sample banks resolve at slider edges and spinner end',()=>{const p=F.osuRead(source).project;assert.deepEqual(p.events.map(e=>p.rows.find(r=>r.id===e.rowId).name),['normal-hitnormal2.wav','soft-hitnormal3.wav','soft-hitnormal3.wav']);assert.deepEqual(p.events.map(e=>e.volume),[80,30,30]);});
test('slider bank edit preserves Auto index across inherited index changes',()=>{const p=F.osuRead(source).project;bankEdit(p);const text=exported(p),line=text.split('[HitObjects]\n')[1].split('\n')[0];assert.equal(line,'64,192,0,2,0,L|256:192,1,100,0|0,0:0|3:3,0:0:0:0:');assert.deepEqual(F.osuRead(text).project.events.map(e=>e.volume),[80,30,30]);});
test('old saved projects without resolved index metadata export bank edits',()=>{const p=F.osuRead(source).project;for(const e of [...p.events,...p.beatmaps[0].originalEvents])delete e.source.sampleIndex;bankEdit(p);exported(p);});
test('adding a layer at inherited volume does not flatten differing slider-edge volumes',()=>{const p=F.osuRead(source).project,e=tail(p);p.events.push({...structuredClone(e),id:'added',source:undefined,rowId:row(p,'soft-hitclap3.wav').id});const text=exported(p);assert.match(text,/0\|8,0:0\|2:2,0:0:0:0:/);assert.deepEqual(F.osuRead(text).project.events.map(e=>e.volume),[80,30,30,30]);});
test('independent slider-edge volume change remains an explicit conflict',()=>{const p=F.osuRead(source).project;tail(p).volume=40;assert.match(F.osuWrite(p).conflicts.join(' '),/one shared volume/);});
test('explicit object bank and index override timing-point changes',()=>{const p=F.osuRead(source.replaceAll('0:0:0:0:','3:2:4:65:')).project;assert.ok(p.events.every(e=>p.rows.find(r=>r.id===e.rowId).name==='drum-hitnormal4.wav'));assert.ok(p.events.every(e=>e.volume===65));});
test('default sample index zero remains Auto after an unnumbered bank edit',()=>{const text=source.replace('1,2,80','1,0,80').replace('2,3,30','2,0,30'),p=F.osuRead(text).project,e=tail(p);e.rowId=row(p,'drum-hitnormal.wav').id;const out=F.osuWrite(p);assert.deepEqual(out.conflicts,[]);assert.match(out.text,/0\|0,0:0\|3:3,0:0:0:0:/);});
test('lane labels, order, solo and mute remain presentation-only on export',()=>{const p=F.osuRead(source).project;p.rows.reverse();for(const r of p.rows){r.label='My lane';r.muted=true;r.solo=true;}assert.equal(F.osuWrite(p).text,source);});
test('incompatible explicitly numbered slider edge indices are blocked',()=>{const p=F.osuRead(source).project;tail(p).rowId=row(p,'drum-hitnormal4.wav').id;assert.match(F.osuWrite(p).conflicts.join(' '),/indices/);});
