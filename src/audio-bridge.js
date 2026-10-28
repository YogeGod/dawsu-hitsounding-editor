(function(root){
"use strict";
function interleave(input){const left=input.getChannelData(0),right=input.numberOfChannels>1?input.getChannelData(1):left,out=new Float32Array(left.length*2);for(let i=0;i<left.length;i++){out[i*2]=Number.isFinite(left[i])?Math.max(-1,Math.min(1,left[i])):0;out[i*2+1]=Number.isFinite(right[i])?Math.max(-1,Math.min(1,right[i])):0;}return out;}
const loadedContexts=new WeakSet();
class AudioBridge {
 static async create(context,master,frames,send,onError){if(!context.audioWorklet||typeof AudioWorkletNode==="undefined")throw Error("AudioWorklet is unavailable in this runtime.");if(!loadedContexts.has(context)){await context.audioWorklet.addModule("src/audio-output-worklet.js");loadedContexts.add(context);}return new AudioBridge(context,master,frames,send,onError);}
 constructor(context,master,frames,send,onError){this.context=context;this.master=master;this.closed=false;this.suspended=false;this.processor=new AudioWorkletNode(context,"hse-native-output",{numberOfInputs:1,numberOfOutputs:1,outputChannelCount:[2],processorOptions:{frames}});this.processor.port.onmessage=event=>{if(this.closed||this.suspended)return;try{send(event.data);}catch(e){this.closed=true;onError(e);}};this.processor.onprocessorerror=()=>onError(Error("Native output worklet stopped. Reapply Audio preferences."));master.disconnect();master.connect(this.processor);this.processor.connect(context.destination);}
 dispose(){this.closed=true;this.processor.port.onmessage=null;this.processor.port.close();this.master.disconnect();this.processor.disconnect();this.master.connect(this.context.destination);}
}
const api={AudioBridge,interleave};if(typeof module!=='undefined')module.exports=api;else root.HSEAudioBridge=api;
})(globalThis);
