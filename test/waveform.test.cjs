const {test}=require('node:test'),assert=require('node:assert/strict'),W=require('../src/waveform');
function buffer(data,rate=48000){return {sampleRate:rate,numberOfChannels:data.length,getChannelData:i=>data[i]};}
test('signed waveform retains brief positive and negative peaks at all zooms',()=>{const c=new Float32Array(48000);c[1234]=.8;c[1250]=-.6;const w=W.build(buffer([c]));assert.ok(W.range(w,25,27).max>.99);assert.ok(W.range(w,25,27).min<-.74);assert.equal(W.range(w,-5,-1).max,0);assert.equal(W.range(w,1001,1005).min,0);});
test('stereo peaks include attacks present in only one channel',()=>{const c=new Float32Array(48000);c[10000]=1;const w=W.build(buffer([new Float32Array(48000),c]));assert.equal(W.range(w,208,209).max,1);});
