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
const approx=(a,b)=>assert.ok(Math.abs(a-b)<1e-7,a+' != '+b);
function setup(){read('stop();history=new H.History(H.demo());for(const row of p().rows)row.name="unloaded-custom.wav";cursor=1375;view=0;scale=.12;range=null;selected=new Set();update();');el('startMode').value='cursor';el('returnStart').checked=true;}
function advance(ms){audio.currentTime=read('transport.clock')+ms/1000;for(const tick of [...ticks.values()])tick();}
(async()=>{
 setup();await read('play()');advance(700);await el('play').onclick();check('Play pause returns to the original playhead position when enabled',()=>{approx(read('cursor'),1375);assert.equal(read('playing'),false);});
 setup();await read('play()');advance(700);key('keydown',' ');await Promise.resolve();check('Space honors Return on stop',()=>{approx(read('cursor'),1375);assert.equal(read('playing'),false);});
 setup();await read('play()');advance(700);el('stop').click();check('Stop button uses the same playback start anchor',()=>approx(read('cursor'),1375));
 setup();el('returnStart').checked=false;await read('play()');advance(700);await el('play').onclick();check('Return off pauses at the current playback position',()=>approx(read('cursor'),2075));
 setup();read('history.project=H.setCycle(p(),{leftMs:1000,rightMs:2000,enabled:true});');await read('play()');advance(900);read('commit("Cycle off",H.setCycle(p(),{enabled:false}));');audio.currentTime+=.2;el('stop').click();check('cycle wraps and live locator changes preserve the original return anchor',()=>approx(read('cursor'),1375));
 setup();await read('play()');advance(700);read('view=20000;');el('stop').click();check('returning to the start also makes the playhead visible after following playback',()=>{approx(read('cursor'),1375);assert.ok(read('xAt(cursor)')>=0&&read('xAt(cursor)')<=el('grid').clientWidth);});
 setup();await read('play()');advance(700);el('stop').click();el('wave').onpointerdown(pointer({clientX:45}));el('stop').click();check('Stop while already stopped does not jump to a stale playback anchor',()=>approx(read('cursor'),375));
 setup();read('cursor=7500;');await read('play()');advance(1000);check('automatic playback end also honors Return on stop',()=>{assert.equal(read('playing'),false);approx(read('cursor'),7500);});
 console.log(passed+' transport return checks passed.');
})().catch(e=>{console.error(e);process.exitCode=1;});
