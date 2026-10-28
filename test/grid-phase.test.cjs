const test=require('node:test'),assert=require('node:assert/strict'),H=require('../src/model'),F=require('../src/formats.cjs');
const text=`osu file format v14
[General]
Mode:0
[Difficulty]
SliderMultiplier:1.4
[TimingPoints]
137.5,500,4,1,1,80,1,0
900,-50,4,1,1,80,0,0
1432.75,400,3,1,1,80,1,0
[HitObjects]
64,192,637.5,1,0,0:0:0:0:
128,192,1432.75,1,0,0:0:0:0:
192,192,1532.75,1,0,0:0:0:0:
256,192,1832.75,1,0,0:0:0:0:
`;
const project=()=>F.osuRead(text).project,approx=(a,b)=>assert.ok(Math.abs(a-b)<1e-7,`${a} != ${b}`),grid=(p,from,to,div=4)=>H.gridBeats(p,H.timeToBeat(from,p.tempos),H.timeToBeat(to,p.tempos),1/div).map(b=>H.beatToTime(b,p.tempos));
test('every red point restarts beat phase at its exact fractional offset',()=>{const p=project();for(const ms of [137.5,637.5,1432.75,1532.75,1832.75])approx(H.snapTime(ms,p,{divisor:4}),ms);assert.ok(grid(p,1400,1900).some(t=>Math.abs(t-1532.75)<1e-7));});
test('drawn grid has no continuation of the old section after a red point',()=>{const times=grid(project(),1400,1900);assert.equal(times.some(t=>Math.abs(t-1512.5)<1e-7),false);assert.deepEqual(times.map(t=>Math.round(t*100)/100),[1432.75,1532.75,1632.75,1732.75,1832.75]);});
test('green points do not restart beat phase',()=>{const p=project();approx(H.snapTime(1012.5,p,{divisor:4}),1012.5);assert.ok(!grid(p,850,1000).some(t=>Math.abs(t-900)<1e-7));});
test('nearest snap at a boundary considers actual grid points on either side',()=>{const p=project();approx(H.snapTime(1420,p,{divisor:4}),1432.75);approx(H.snapTime(1390,p,{divisor:4}),1387.5);});
test('strict adjacent-grid movement crosses red boundaries correctly',()=>{const p=project();approx(H.gridNeighbor(1387.5,p,4,1),1432.75);approx(H.gridNeighbor(1432.75,p,4,-1),1387.5);approx(H.gridNeighbor(1432.75,p,4,1),1532.75);});
test('relative snapping keeps the original within-cell offset in each section',()=>{const p=project();approx(H.snapTime(1544,p,{divisor:4,target:'relative'},647.5),1540.75);});
test('negative and fractional first offsets anchor the grid',()=>{const p=project();p.tempos=[{timeMs:-237.25,bpm:120,meter:4},{timeMs:876.5,bpm:150,meter:4}];approx(H.snapTime(1,p,{divisor:4}),12.75);approx(H.snapTime(976.5,p,{divisor:4}),976.5);});
test('paint follows the same restarted grid across the section boundary',()=>{const p=project();p.events=[];const q=H.paint(p,p.rows[0].id,1387.5,1632.75,{divisor:4},'draw');assert.deepEqual(q.events.map(e=>Math.round(e.timeMs*100)/100),[1387.5,1432.75,1532.75,1632.75]);});
test('grid and quantize use exact beat lengths for triplet divisions',()=>{const p=project(),ms=1432.75+400/3;approx(H.snapTime(ms,p,{divisor:3}),ms);assert.ok(grid(p,1432.75,1832.75,3).some(t=>Math.abs(t-ms)<1e-7));});
test('continuous MIDI beats retain their phase across non-beat-aligned tempo changes',()=>{const p=project();p.beatmaps=[];const ms=H.beatToTime(3,p.tempos);approx(H.snapTime(ms,p,{divisor:4}),ms);assert.ok(Math.abs(H.snapTime(1432.75,p,{divisor:4})-1432.75)>1);});
test('grid fixes do not move objects events or modify unchanged exports',()=>{const p=project(),before=JSON.stringify(p);grid(p,0,3000);for(const e of p.events)H.snapTime(e.timeMs,p,{divisor:4});assert.equal(JSON.stringify(p),before);assert.equal(F.osuWrite(p).text,text);});
test('signature bar anchors and grid markers agree at the new red point',()=>{const p=project(),beat=H.timeToBeat(1432.75,p.tempos);assert.equal(H.beatPosition(p,beat).isBar,true);assert.ok(H.gridBeats(p,beat,beat+.1,.25).some(b=>Math.abs(b-beat)<1e-8));});

test('same-meter red points restart emphasized beats and bars in older saved projects',()=>{const p=project();p.timeSignatures=[{timeMs:137.5,numerator:4,denominator:4}];const before=JSON.stringify(p),list=H.signatureSegments(p),beat=H.timeToBeat(1432.75,p.tempos);assert.equal(list.length,2);assert.equal(H.beatPosition(p,beat,list).isBar,true);assert.equal(H.beatPosition(p,beat+1,list).isBeat,true);assert.equal(JSON.stringify(p),before);});
test('tempo and signature display anchors retain explicit signature overrides',()=>{const p=project();p.timeSignatures=[{timeMs:137.5,numerator:4,denominator:4},{timeMs:1000,numerator:6,denominator:8}];assert.deepEqual(H.signatureSegments(p).map(s=>[s.timeMs,s.numerator,s.denominator]),[[137.5,4,4],[1000,6,8],[1432.75,6,8]]);});
test('MIDI bar numbering remains continuous across ordinary tempo changes',()=>{const p=project();p.beatmaps=[];p.timeSignatures=[{timeMs:137.5,numerator:4,denominator:4}];assert.equal(H.signatureSegments(p).length,1);});
