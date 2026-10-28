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



function setup(){reset();read('rowH=31;scale=.12;view=0;scrollY=0;snap=true;');el('divisor').value='4';el('snapMode').value='grid';}
const press=k=>key('keydown',k,{ctrlKey:true});
const approx=(a,b)=>assert.ok(Math.abs(a-b)<.0011,a+' != '+b);
function pick(row,time){read('selected=new Set([p().events.find(e=>e.rowId===p().rows['+row+'].id&&e.timeMs==='+time+').id]);active=p().rows['+row+'].id;update();');}
setup();
check('Ctrl+Right and Ctrl+Left move one sound to adjacent grid lines',()=>{pick(0,500);const id=read('[...selected][0]');press('ArrowRight');assert.equal(read('p().events.find(e=>selected.has(e.id)).timeMs'),625);press('ArrowLeft');assert.equal(read('p().events.find(e=>selected.has(e.id)).timeMs'),500);assert.equal(read('[...selected][0]'),id);});
setup();
check('off-grid movement goes to the next strict grid line in either direction',()=>{pick(0,500);read('p().events.find(e=>selected.has(e.id)).timeMs=540;');press('ArrowRight');assert.equal(read('p().events.find(e=>selected.has(e.id)).timeMs'),625);read('p().events.find(e=>selected.has(e.id)).timeMs=540;');press('ArrowLeft');assert.equal(read('p().events.find(e=>selected.has(e.id)).timeMs'),500);});
setup();
check('group horizontal movement preserves all time offsets and selection IDs',()=>{read('selected=new Set(p().events.filter(e=>e.rowId===p().rows[0].id&&[500,1000,1500].includes(e.timeMs)).map(e=>e.id));p().events.find(e=>selected.has(e.id)).timeMs=540;');const before=read('p().events.filter(e=>selected.has(e.id)).map(e=>[e.id,e.timeMs])');press('ArrowRight');const after=read('p().events.filter(e=>selected.has(e.id)).map(e=>[e.id,e.timeMs])');for(let i=0;i<before.length;i++){assert.equal(after[i][0],before[i][0]);approx(after[i][1]-before[i][1],85);}assert.equal(read('selected.size'),3);});
setup();
check('Ctrl+Down and Ctrl+Up move a selected group through sample row order',()=>{read('selected=new Set(p().events.filter(e=>e.rowId===p().rows[1].id&&[500,1500].includes(e.timeMs)).map(e=>e.id));active=p().rows[1].id;');press('ArrowDown');assert.equal(read('p().events.filter(e=>selected.has(e.id)).every(e=>e.rowId===p().rows[2].id)'),true);assert.equal(read('active'),read('p().rows[2].id'));press('ArrowUp');assert.equal(read('p().events.filter(e=>selected.has(e.id)).every(e=>e.rowId===p().rows[1].id)'),true);});
setup();
check('multi-row groups preserve relative lane positions',()=>{read('selected=new Set([p().events.find(e=>e.rowId===p().rows[1].id).id,p().events.find(e=>e.rowId===p().rows[2].id).id]);');const ids=read('[...selected]');press('ArrowDown');assert.equal(read('p().events.find(e=>e.id==='+JSON.stringify(ids[0])+').rowId'),read('p().rows[2].id'));assert.equal(read('p().events.find(e=>e.id==='+JSON.stringify(ids[1])+').rowId'),read('p().rows[3].id'));});
setup();
check('a group at the first or last row stays together at the boundary',()=>{pick(0,500);press('ArrowUp');assert.equal(read('history.undoStack.length'),0);pick(3,0);press('ArrowDown');assert.equal(read('history.undoStack.length'),0);});
setup();
check('left boundary never creates negative times or partially shifts a group',()=>{read('selected=new Set(p().events.filter(e=>e.rowId===p().rows[0].id&&[0,500].includes(e.timeMs)).map(e=>e.id));');const before=read('JSON.stringify(p())');press('ArrowLeft');assert.equal(read('JSON.stringify(p())'),before);assert.equal(read('history.undoStack.length'),0);});
setup();
check('Ctrl+arrows preserve chart geometry, volume, duration and MIDI performance',()=>{pick(1,500);const objects=read('JSON.stringify(p().objects)'),data=read('JSON.stringify(p().events.map(e=>[e.id,e.volume,e.durationMs,e.performance]))');press('ArrowDown');press('ArrowRight');assert.equal(read('JSON.stringify(p().objects)'),objects);assert.equal(read('JSON.stringify(p().events.map(e=>[e.id,e.volume,e.durationMs,e.performance]))'),data);});
setup();
check('each arrow press is one undo step and supports redo',()=>{pick(0,500);const original=read('JSON.stringify(p())');press('ArrowRight');const moved=read('JSON.stringify(p())');assert.equal(read('history.undoStack.length'),1);key('keydown','z',{ctrlKey:true});assert.equal(read('JSON.stringify(p())'),original);key('keydown','y',{ctrlKey:true});assert.equal(read('JSON.stringify(p())'),moved);});
setup();
check('grid movement follows the current divisor and tempo map',()=>{pick(0,1000);read('p().tempos=[{timeMs:0,bpm:120,meter:4},{timeMs:1000,bpm:60,meter:4}];');el('divisor').value='8';press('ArrowRight');approx(read('p().events.find(e=>selected.has(e.id)).timeMs'),1125);});
setup();
check('Ctrl+arrows use grid lines even when event snapping is off or targets Cursor',()=>{pick(0,500);read('snap=false;');el('snapMode').value='cursor';press('ArrowRight');assert.equal(read('p().events.find(e=>selected.has(e.id)).timeMs'),625);});
setup();
check('Ctrl+arrows retain native text and slider input behaviour',()=>{pick(0,500);const before=read('JSON.stringify(p())');el('rowName').focus();assert.equal(press('ArrowRight').defaultPrevented,false);el('rowHeight').focus();assert.equal(press('ArrowDown').defaultPrevented,false);assert.equal(read('JSON.stringify(p())'),before);});
setup();
check('Ctrl+arrows with no selection create no chart edit',()=>{const before=read('JSON.stringify(p())');press('ArrowRight');assert.equal(read('JSON.stringify(p())'),before);assert.equal(read('history.undoStack.length'),0);});
setup();
check('movement ignores keyboard nudges while a pointer gesture is active',()=>{pick(0,500);read('gesture={type:"move"};');const before=read('JSON.stringify(p())');press('ArrowRight');assert.equal(read('JSON.stringify(p())'),before);read('gesture=null;');});
console.log(passed+' Ctrl+arrow movement checks passed.');
