(function(root){
"use strict";
function build(buffer){
 const channels=Array.from({length:buffer.numberOfChannels},(_,i)=>buffer.getChannelData(i)),length=channels[0]?.length||0,rate=buffer.sampleRate,step=64,count=Math.ceil(length/step),min=new Float32Array(count),max=new Float32Array(count);let peak=0;
 for(let k=0;k<count;k++){let lo=0,hi=0;for(const channel of channels)for(let i=k*step;i<Math.min(length,(k+1)*step);i++){lo=Math.min(lo,channel[i]);hi=Math.max(hi,channel[i]);}min[k]=lo;max[k]=hi;peak=Math.max(peak,-lo,hi);}
 const levels=[{min,max,step}];while(levels.at(-1).min.length>1){const a=levels.at(-1),n=Math.ceil(a.min.length/2),lo=new Float32Array(n),hi=new Float32Array(n);for(let i=0;i<n;i++){lo[i]=Math.min(a.min[2*i],a.min[2*i+1]??0);hi[i]=Math.max(a.max[2*i],a.max[2*i+1]??0);}levels.push({min:lo,max:hi,step:a.step*2});}

 return {channels,length,sampleRate:rate,durationMs:length/rate*1000,levels,peak:peak||1};
}
function range(wave,startMs,endMs){
 const a=Math.max(0,Math.floor(startMs*wave.sampleRate/1000)),b=Math.min(wave.length,Math.ceil(endMs*wave.sampleRate/1000));if(b<=a)return {min:0,max:0};let lo=0,hi=0;
 if(b-a<64){for(const c of wave.channels)for(let i=a;i<b;i++){lo=Math.min(lo,c[i]);hi=Math.max(hi,c[i]);}}
 else{let level=wave.levels[0];for(const candidate of wave.levels){if(candidate.step>b-a)break;level=candidate;}for(let i=Math.floor(a/level.step);i<Math.ceil(b/level.step);i++){lo=Math.min(lo,level.min[i]||0);hi=Math.max(hi,level.max[i]||0);}}
 return {min:lo/wave.peak,max:hi/wave.peak};
}
const api={build,range};if(typeof module!=="undefined")module.exports=api;else root.HSEWaveform=api;
})(typeof globalThis!=="undefined"?globalThis:this);
