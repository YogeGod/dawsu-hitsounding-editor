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



sandbox.G=require(root+'src/generate-objects.cjs');sandbox.calls=[];sandbox.desktop.generateObjects=async(p,options)=>{sandbox.calls.push(options);return sandbox.G.generate(p,options);};
sandbox.AudioContext=function(){return {currentTime:0,destination:{},resume(){},createGain(){return {gain:{value:0},connect(){}};}};};
check('global gain starts at 20 percent before any user adjustment',()=>{read('context();');assert.equal(read('masterVolume'),20);assert.equal(read('masterGain.gain.value'),.2);assert.match(html,/id="masterVolume"[^>]*value="20"/);assert.match(html,/id="masterVolumeValue">20%/);});
function setup(){read('history=new H.History(H.project("Generated test"));p().rows=[{id:"r",name:"normal-hitnormal.wav",note:35,channel:10}];p().events=[{id:"e",rowId:"r",timeMs:1000,durationMs:0,volume:77,ownerId:null,assignment:"unassigned"}];active="r";selected.clear();dirty=false;update();');}
(async()=>{
 setup();read('modal=async()=>false;');await el('generateObjects').onclick();check('cancelled generation creates no objects or undo entry',()=>{assert.equal(read('p().objects.length'),0);assert.equal(read('history.undoStack.length'),0);assert.equal(sandbox.calls.length,0);});
 setup();read('modal=async()=>true;');const before=read('JSON.stringify(p())');await el('generateObjects').onclick();check('generation action binds sounds and creates exactly one undo step',()=>{assert.equal(read('p().objects.length'),1);assert.equal(read('p().events[0].ownerId'),read('p().objects[0].id'));assert.equal(read('p().events[0].volume'),77);assert.equal(read('history.undoStack.length'),1);assert.equal(read('dirty'),true);read('history.undo();');assert.equal(read('JSON.stringify(p())'),before);});
 setup();read('p().events.push({...p().events[0],id:"other",timeMs:2000});selected=new Set(["e"]);modal=async()=>{document.getElementById("generateScope").value="selected";document.getElementById("generateX").value=256;document.getElementById("generateY").value=192;return true;};');await el('generateObjects').onclick();check('selected scope leaves other sounds unassigned',()=>{assert.deepEqual(Array.from(sandbox.calls.at(-1).ids),['e']);assert.equal(read('p().objects.length'),1);assert.equal(read('p().events[1].ownerId'),null);});
 sandbox.desktop.rhythmGuide=async(p,options)=>{sandbox.guideCalls++;return sandbox.G.rhythmGuide(p,options);};
 sandbox.desktop.planOsu=async p=>{sandbox.exportedProject=p;sandbox.planCalls++;return {renderRequests:[],conflicts:[]};};
 sandbox.desktop.osu=async(p,mixes)=>{sandbox.saveCalls++;return {path:'guide.osz'};};
 function exportSetup(){setup();sandbox.guideCalls=sandbox.planCalls=sandbox.saveCalls=0;read('p().audio={name:"song.wav",assetKey:"song"};renderExportMixes=async()=>({mixes:[],attenuated:0});message=async(title,body)=>{lastMessage=body;};modal=async()=>true;');}
 exportSetup();read('modal=async()=>false;');await el('exportOsu').onclick();check('cancelling rhythm guide export never generates or saves anything',()=>{assert.equal(sandbox.guideCalls,0);assert.equal(sandbox.planCalls,0);assert.equal(sandbox.saveCalls,0);assert.equal(read('p().objects.length'),0);assert.equal(el('exportOsu').disabled,false);});
 exportSetup();read('p().events.push({...p().events[0],id:"second",timeMs:1757});selected=new Set(["e"]);modal=async(title,body)=>{if(title==="Export beatmap"){document.getElementById("beatmapExportMode").value="guide";return true;}guideDialogTitle=title;guidePreview=body.children[0].textContent;document.getElementById("guideName").value="My hitsounds";document.getElementById("guideCombo").value="yes";document.getElementById("guideX").value=256;document.getElementById("guideY").value=192;return true;};');const exportBefore=read('JSON.stringify(p())');await el('exportOsu').onclick();check('export button previews and exports all sounds as a guide without editing the project',()=>{assert.equal(read('guideDialogTitle'),'Export rhythm guide');assert.match(read('guidePreview'),/2 circles from 2 sounds/);assert.equal(sandbox.guideCalls,1);assert.equal(sandbox.saveCalls,1);assert.equal(sandbox.exportedProject.objects.length,2);assert.match(sandbox.exportedProject.beatmaps[0].text,/Version: My hitsounds/);assert.equal(read('JSON.stringify(p())'),exportBefore);assert.equal(read('history.undoStack.length'),0);assert.equal(read('dirty'),false);assert.equal(el('exportOsu').disabled,false);});
 exportSetup();read('p().audio=null;');await el('exportOsu').onclick();check('missing song audio gives an actionable message before generating objects',()=>{assert.match(read('lastMessage'),/Load song audio/);assert.equal(sandbox.guideCalls,0);assert.equal(sandbox.saveCalls,0);});
 exportSetup();read('history=new H.History(G.generate(p()).project);modal=async(title)=>{assertedExportChoice=title;if(title!=="Export beatmap")throw Error("Unexpected guide dialog");document.getElementById("beatmapExportMode").value="normal";return true;};');await el('exportOsu').onclick();check('existing gameplay maps keep their normal export path',()=>{assert.equal(read('assertedExportChoice'),'Export beatmap');assert.equal(sandbox.guideCalls,0);assert.equal(sandbox.planCalls,1);assert.equal(sandbox.saveCalls,1);});
 exportSetup();read('history=new H.History(G.generate(p()).project);p().events.push({...p().events[0],id:"extra",timeMs:1757,ownerId:null,assignment:"unassigned"});modal=async(title)=>{if(title==="Export beatmap")document.getElementById("beatmapExportMode").value="guide";else document.getElementById("guideName").value="HS";return true;};');const existingBefore=read('JSON.stringify(p())');await el('exportOsu').onclick();check('existing maps can export an HS guide including unassigned events without changing the project',()=>{assert.equal(sandbox.guideCalls,1);assert.equal(sandbox.saveCalls,1);assert.equal(sandbox.exportedProject.objects.length,2);assert.match(sandbox.exportedProject.beatmaps[0].text,/Version: HS/);assert.equal(read('JSON.stringify(p())'),existingBefore);});
 exportSetup();read('modal=async()=>{document.getElementById("beatmapExportMode").value="normal";return true;};');await el('exportOsu').onclick();check('normal export on an objectless project explains the HS option and never generates silently',()=>{assert.match(read('lastMessage'),/no gameplay objects/);assert.equal(sandbox.guideCalls,0);assert.equal(sandbox.saveCalls,0);});
 exportSetup();read('modal=async(title)=>title==="Export beatmap";');await el('exportOsu').onclick();check('cancelling guide options after the choice does not generate or export',()=>{assert.equal(sandbox.guideCalls,0);assert.equal(sandbox.planCalls,0);assert.equal(sandbox.saveCalls,0);});
 console.log(passed+' generation UI checks passed.');
})().catch(e=>{console.error(e);process.exitCode=1;});
