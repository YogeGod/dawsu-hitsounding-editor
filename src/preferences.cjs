"use strict";
const fs=require("fs"),path=require("path");
const audioDefaults={backend:"browser",deviceId:"",sampleRate:48000,latencyMs:30,bridgeFrames:512,channelOffset:0,latencyHint:"interactive"};
function audioSettings(value={}){const a={...audioDefaults,...value};if(!["browser","wasapi-shared","wasapi-exclusive","asio","directsound","mme"].includes(a.backend)||typeof a.deviceId!=="string"||a.deviceId.length>2048||![44100,48000,88200,96000].includes(a.sampleRate)||!Number.isInteger(a.latencyMs)||a.latencyMs<10||a.latencyMs>500||![256,512,1024,2048,4096].includes(a.bridgeFrames)||!Number.isInteger(a.channelOffset)||a.channelOffset<0||a.channelOffset>126||!["interactive","balanced","playback"].includes(a.latencyHint))throw Error("Invalid audio output settings.");return Object.fromEntries(Object.keys(audioDefaults).map(key=>[key,a[key]]));}
function load(file){try{const value=JSON.parse(fs.readFileSync(file,"utf8"));const result={songsFolder:typeof value.songsFolder==="string"?value.songsFolder:""};if(value.audio)try{result.audio=audioSettings(value.audio);}catch{}return result;}catch{return {songsFolder:""};}}
function save(file,value){
 if(!value||typeof value.songsFolder!=="string"||value.songsFolder.length>32767)throw Error("Invalid Songs folder.");
 let songsFolder=value.songsFolder.trim();if(songsFolder){if(!path.isAbsolute(songsFolder)||!fs.statSync(songsFolder).isDirectory())throw Error("Choose an existing dawsu! Songs folder.");songsFolder=fs.realpathSync(songsFolder);}
 const result={songsFolder};if(value.audio!==undefined)result.audio=audioSettings(value.audio);fs.mkdirSync(path.dirname(file),{recursive:true});const temporary=file+".tmp";fs.writeFileSync(temporary,JSON.stringify(result,null,2),"utf8");fs.renameSync(temporary,file);return result;
}
module.exports={load,save,audioSettings,audioDefaults};
