"use strict";
class NativeOutputProcessor extends AudioWorkletProcessor {
 constructor(options){super();this.frames=options.processorOptions.frames;this.block=new Float32Array(this.frames*2);this.at=0;}
 process(inputs,outputs){for(const output of outputs)for(const channel of output)channel.fill(0);const channels=inputs[0]||[],left=channels[0],right=channels[1]||left,n=outputs[0]?.[0]?.length||128;for(let i=0;i<n;i++){const l=left?.[i]||0,r=right?.[i]||0;this.block[this.at*2]=Number.isFinite(l)?Math.max(-1,Math.min(1,l)):0;this.block[this.at*2+1]=Number.isFinite(r)?Math.max(-1,Math.min(1,r)):0;if(++this.at===this.frames){this.port.postMessage(this.block.buffer,[this.block.buffer]);this.block=new Float32Array(this.frames*2);this.at=0;}}return true;}
}
registerProcessor("hse-native-output",NativeOutputProcessor);
