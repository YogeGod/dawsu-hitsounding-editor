(function(root){
'use strict';
const RATE=48000,MAX_SECONDS=120;
function mix(layers,rate=RATE){
 if(!Array.isArray(layers)||layers.length>256)throw Error('A mixed sound supports at most 256 layers.');
 let length=layers.length?1:Math.round(rate*.01);for(const layer of layers){if(!Number.isFinite(layer.volume)||layer.volume<0||layer.volume>100||!layer.channels?.length||layer.channels.length>2||!(layer.sampleRate>0))throw Error('Invalid rendered audio layer.');const n=layer.channels[0].length;if(layer.channels.some(c=>c.length!==n))throw Error('Mismatched sample channels.');length=Math.max(length,Math.ceil(n*rate/layer.sampleRate));}
 if(length>rate*MAX_SECONDS)throw Error('A rendered hitsound exceeds two minutes. Shorten the source sample.');
 const pcm=[new Float32Array(length),new Float32Array(length)];
 for(const layer of layers){const gain=layer.volume/100,step=layer.sampleRate/rate,n=layer.channels[0].length;for(let ch=0;ch<2;ch++){const source=layer.channels[Math.min(ch,layer.channels.length-1)],target=pcm[ch];for(let i=0;i<length;i++){const x=i*step,j=Math.floor(x);if(j>=n)break;const f=x-j,v=source[j]*(1-f)+(source[Math.min(j+1,n-1)]||0)*f;if(!Number.isFinite(v))throw Error('Source audio contains invalid samples.');target[i]+=v*gain;}}}
 let peak=0;for(const channel of pcm)for(const value of channel)peak=Math.max(peak,Math.abs(value));const gain=peak>1?1/peak:1;
 const bytes=new Uint8Array(44+length*4),view=new DataView(bytes.buffer);function ascii(at,text){for(let i=0;i<text.length;i++)bytes[at+i]=text.charCodeAt(i);}
 ascii(0,'RIFF');view.setUint32(4,bytes.length-8,true);ascii(8,'WAVE');ascii(12,'fmt ');view.setUint32(16,16,true);view.setUint16(20,1,true);view.setUint16(22,2,true);view.setUint32(24,rate,true);view.setUint32(28,rate*4,true);view.setUint16(32,4,true);view.setUint16(34,16,true);ascii(36,'data');view.setUint32(40,length*4,true);
 for(let i=0;i<length;i++)for(let ch=0;ch<2;ch++){const value=Math.max(-1,Math.min(1,pcm[ch][i]*gain));view.setInt16(44+(i*2+ch)*2,Math.round(value*(value<0?32768:32767)),true);}
 return {bytes,headroomGain:gain,peak,durationMs:length*1000/rate};
}
function validateWav(bytes){const array=bytes instanceof Uint8Array?bytes:new Uint8Array(bytes),v=new DataView(array.buffer,array.byteOffset,array.byteLength),text=(a,b)=>String.fromCharCode(...array.subarray(a,b));if(array.length<48||array.length>RATE*MAX_SECONDS*4+44||text(0,4)!=='RIFF'||text(8,12)!=='WAVE'||text(12,16)!=='fmt '||text(36,40)!=='data'||v.getUint32(4,true)!==array.length-8||v.getUint32(16,true)!==16||v.getUint16(20,true)!==1||v.getUint16(22,true)!==2||v.getUint32(24,true)!==RATE||v.getUint16(34,true)!==16||v.getUint32(40,true)!==array.length-44||(array.length-44)%4)throw Error('Invalid generated hitsound WAV.');return array;}
const api={mix,validateWav,RATE};if(typeof module!=='undefined')module.exports=api;else root.HSESampleRender=api;
})(globalThis);
