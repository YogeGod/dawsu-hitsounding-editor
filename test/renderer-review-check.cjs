const fs=require("fs"),vm=require("vm"),assert=require("assert/strict");
const root=require("path").resolve(__dirname,"..")+"/",H=require(root+"src/model.js"),handlers={},docHandlers={},elements=new Map(),all=[],timers=new Map();let nextTimer=0;
let barRects=[];const barCtx=new Proxy({},{get:(_,key)=>key==="fillRect"?((...args)=>barRects.push(args)):(()=>{})});
const ctx=new Proxy({},{get:()=>()=>{}});
let doc;
function el(id="",tagName="DIV"){
 if(elements.has(id))return elements.get(id);
 const node={id,tagName:tagName.toUpperCase(),dataset:{},attributes:{},children:[],classList:{toggle(){},remove(){},add(){}},style:{},value:"",title:"",type:"",clientWidth:1000,clientHeight:300,open:false,checked:false,hidden:id==="hoverHelp",disabled:false,isConnected:true,textContent:"",
 setAttribute(k,v){this.attributes[k]=v;if(k==="title")this.title=v;},getAttribute(k){return k==="title"?this.title:this.attributes[k]??null;},hasAttribute(k){return k==="title"?!!this.title:k in this.attributes;},removeAttribute(k){delete this.attributes[k];if(k==="title")this.title="";},
 contains(other){for(let n=other;n;n=n.parentElement)if(n===this)return true;return false;},
 closest(){for(let n=this;n;n=n.parentElement)if(n.dataset.help||n.title)return n;return null;},
 focus(){doc.activeElement=this;},select(){this.selectedText=true;},
 setPointerCapture(){},getBoundingClientRect(){return {left:0,top:0,bottom:300,width:250,height:80};},getContext(){return this.id==="bars"?barCtx:ctx;},
 replaceChildren(...nodes){this.children=[];this.append(...nodes);},append(...nodes){for(const n of nodes){this.children.push(n);if(typeof n==="object")n.parentElement=this;}},prepend(...nodes){this.children.unshift(...nodes);},
 querySelector(){return {textContent:({undo:"Ctrl+Z",redo:"Ctrl+Y / Ctrl+Shift+Z",cut:"Ctrl+X",copy:"Ctrl+C",paste:"Ctrl+V",duplicate:"Ctrl+D",repeat:"Ctrl+R",delete:"Delete",selectAll:"Ctrl+A",deselect:"Ctrl+Shift+A"})[this.dataset.command]||""};},
 click(){if(!this.disabled)this.onclick?.();},showModal(){this.open=true;}
 };elements.set(id,node);all.push(node);return node;
}
const html=fs.readFileSync(root+"index.html","utf8");
for(const match of html.matchAll(/<([a-z][\w-]*)\b([^>]*)>/gi)){
 const attrs=Object.fromEntries([...match[2].matchAll(/([\w-]+)="([^"]*)"/g)].map(a=>[a[1],a[2]]));
 if(!attrs.id&&!attrs["data-tool"])continue;
 const node=el(attrs.id||"tool-"+attrs["data-tool"],match[1]);node.type=attrs.type||"";node.title=attrs.title||"";
 for(const [k,v] of Object.entries(attrs)){node.attributes[k]=v;if(k.startsWith("data-"))node.dataset[k.slice(5)]=v;}
}
for(const [id,value] of Object.entries({divisor:"4",insertVolume:"80",insertLength:"125",controller:"volume",valueMode:"relative",rampShape:"line",snapMode:"grid",colour:"volume"}))el(id).value=value;
doc={activeElement:{tagName:"BODY"},getElementById:el,createElement:tag=>el("element"+Math.random(),tag),querySelectorAll:selector=>all.filter(e=>selector==="[data-command]"?e.dataset.command:selector==="button[data-tool]"?e.tagName==="BUTTON"&&e.dataset.tool:e.dataset.tool),addEventListener:(k,f)=>(docHandlers[k]||=[]).push(f)};
const sandbox={HSE:H,document:doc,window:{devicePixelRatio:1,innerWidth:1200,innerHeight:900,addEventListener:(k,f)=>(handlers[k]||=[]).push(f)},ResizeObserver:class{observe(){}},desktop:{onLoaded(){},defaults:()=>new Promise(()=>{})},clearInterval:()=>{},setTimeout:f=>{const id=++nextTimer;timers.set(id,f);return id;},clearTimeout:id=>timers.delete(id),console};
vm.createContext(sandbox);vm.runInContext(fs.readFileSync(root+"src/help.js","utf8"),sandbox);vm.runInContext(fs.readFileSync(root+"src/app.js","utf8"),sandbox);
const read=s=>vm.runInContext(s,sandbox);
function key(type,k,extra={}){const ev={key:k,altKey:k==="Alt"&&type==="keydown",ctrlKey:false,metaKey:false,shiftKey:false,defaultPrevented:false,preventDefault(){this.defaultPrevented=true;},getModifierState:()=>false,...extra};for(const fn of handlers[type]||[])fn(ev);return ev;}
function pointer(extra={}){return {button:0,clientX:15,clientY:15,pointerId:1,altKey:false,ctrlKey:false,metaKey:false,shiftKey:false,preventDefault(){},getModifierState:()=>false,...extra};}
function dispatch(type,event){for(const f of docHandlers[type]||[])f(event);}
function flush(){const queue=[...timers.values()];timers.clear();for(const f of queue)f();}
function reset(){read('history=new H.History(H.demo());selected=new Set();clipboard=null;tool="select";cursor=0;gesture=null;altDraw=false;active=p().rows[0].id;update();');doc.activeElement={tagName:"BODY"};}
let passed=0;const check=(name,fn)=>{fn();passed++;console.log("PASS "+name);};



const ticks=new Map();let nextTick=0;const audio={currentTime:20,resume(){return Promise.resolve();},destination:{},createGain(){return {gain:{value:0},connect(){},disconnect(){}};}};
sandbox.AudioContext=function(){return audio;};sandbox.setInterval=fn=>{ticks.set(++nextTick,fn);return nextTick;};sandbox.clearInterval=id=>ticks.delete(id);
function setup(){read('stop();history=new H.History(H.demo());for(const row of p().rows)row.name="custom.wav";cursor=0;dirty=false;gesture=null;selected.clear();update();');el('startMode').value='cursor';}
(async()=>{
 setup();let resolve;const gate=new Promise(r=>resolve=r);sandbox.prepareGate=gate;read('ensureAudioOutput=()=>prepareGate;');const waiting=read('play()');read('stop()');resolve();await waiting;check('Stop cancels playback while audio initialization is pending',()=>{assert.equal(read('playing'),false);assert.equal(ticks.size,0);});
 setup();let resolve2;sandbox.prepareGate=new Promise(r=>resolve2=r);const first=read('play()');await read('play()');resolve2();await first;check('rapid Play toggles do not create multiple schedulers',()=>{assert.equal(read('playing'),false);assert.equal(ticks.size,0);});
 setup();sandbox.prepareGate=Promise.resolve();await read('play()');check('play still starts normally after cancelled preparation',()=>{assert.equal(read('playing'),true);assert.equal(ticks.size,1);});read('stop()');
 setup();const close={preventDefault(){this.blocked=true;}};read('dirty=true;');for(const f of handlers.beforeunload)f(close);check('unsaved edits block closing until the native confirmation',()=>{assert.equal(close.blocked,true);assert.equal(close.returnValue,'');});
 setup();const clean={preventDefault(){this.blocked=true;}};for(const f of handlers.beforeunload)f(clean);check('an unchanged project closes without a warning',()=>assert.equal(clean.blocked,undefined));
 setup();read('history=new H.History(H.project());history.project.rows.push({id:"row",name:"sample.wav",note:35,channel:10});dirty=true;');const creating=el('newProject').onclick();check('New project warns about unsaved rows even without notes',()=>assert.equal(el('dialog').open,true));el('dialog').returnValue='cancel';el('dialog').open=false;el('dialog').onclose();await creating;assert.equal(read('p().rows.length'),1);
 setup();el('eventVelocity').value='70.6';read('selected=new Set([p().events[0].id]);');await el('eventVelocity').onchange();check('velocity input rounds to a valid MIDI integer',()=>assert.equal(read('p().events[0].performance.velocity'),71));
 setup();el('rowNote').value='42.8';await el('rowNote').onchange();check('row mapping input rounds and validates MIDI note numbers',()=>assert.equal(read('activeRow().note'),43));
 setup();let finishSave;sandbox.desktop.save=()=>new Promise(r=>finishSave=r);const saving=el('save').onclick();read('commit("Later edit",H.setVolumes(p(),[p().events[0].id],22));');finishSave('saved.hseproj');await saving;check('a late save response cannot mark newer edits as saved',()=>assert.equal(read('dirty'),true));
 setup();read('ensureAudioOutput=()=>Promise.resolve();buffers.clear();bufferLoads.clear();');let reads=0,decodes=0;sandbox.desktop.asset=async()=>{reads++;return {data:'AAAA'};};sandbox.atob=s=>Buffer.from(s,'base64').toString('binary');audio.decodeAudioData=async()=>{decodes++;await Promise.resolve();return {duration:1};};
 await Promise.all([read('getBuffer("same")'),read('getBuffer("same")')]);check('simultaneous playback and preview share one sample read and decode',()=>{assert.equal(reads,1);assert.equal(decodes,1);});
 let failures=0;audio.decodeAudioData=async()=>{if(!failures++)throw Error('bad audio');return {duration:1};};await assert.rejects(read('getBuffer("retry")'),/bad audio/);await read('getBuffer("retry")');check('failed audio decoding can be retried without a stuck cache entry',()=>assert.equal(failures,2));
 let releaseDecode,beginDecode;const decoding=new Promise(r=>beginDecode=r);audio.decodeAudioData=()=>new Promise(r=>{releaseDecode=r;beginDecode();});const oldDecode=read('getBuffer("old")');await decoding;read('audioGeneration++;buffers.clear();bufferLoads.clear();');releaseDecode({duration:1});await oldDecode;check('old audio-context loads cannot repopulate the new device cache',()=>assert.equal(read('buffers.has("old")'),false));
 setup();const old=read('JSON.stringify(p())');sandbox.invalidImport={project:H.demo()};sandbox.invalidImport.project.events[0].volume=101;await assert.rejects(read('loadResult(invalidImport)'),/range/);check('an invalid imported project cannot replace the current work',()=>assert.equal(read('JSON.stringify(p())'),old));
 console.log(passed+' review renderer regression checks passed.');
})().catch(e=>{console.error(e);process.exitCode=1;});
