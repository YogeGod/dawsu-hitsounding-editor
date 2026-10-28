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



function setup(){reset();read('rowH=31;scale=.12;view=0;scrollY=0;cursor=0;snap=true;controllerCollapsed=false;');el('divisor').value='4';el('snapMode').value='grid';}
const count=()=>read('p().events.length');
function click(x,y=15,extra={}){el('grid').onpointerdown(pointer({clientX:x,clientY:y,...extra}));el('grid').onpointerup();}
setup();
check('Draw click deletes exactly the clicked event in one undoable edit',()=>{read('tool="draw"');const n=count(),objects=read('JSON.stringify(p().objects)');click(60);assert.equal(count(),n-1);assert.equal(read('history.undoStack.length'),1);assert.equal(read('JSON.stringify(p().objects)'),objects);read('history.undo();update()');assert.equal(count(),n);});
setup();
check('Draw drag from an existing sound stays in erase mode',()=>{read('tool="draw"');const id=read('p().events.find(e=>e.rowId===active&&e.timeMs===500).id'),n=count();el('grid').onpointerdown(pointer({clientX:60,clientY:15}));el('grid').onpointermove(pointer({clientX:105,clientY:15}));el('grid').onpointerup();assert.equal(count(),n-1);assert.equal(read('p().events.some(e=>e.id==='+JSON.stringify(id)+')'),false);assert.equal(read('history.undoStack.length'),1);});
setup();
check('Draw click with small mouse jitter still deletes instead of painting',()=>{read('tool="draw"');const n=count();el('grid').onpointerdown(pointer({clientX:60,clientY:15}));el('grid').onpointermove(pointer({clientX:61,clientY:16}));el('grid').onpointerup();assert.equal(count(),n-1);});
setup();
check('cancelled Draw click leaves the original sound intact',()=>{read('tool="draw"');const before=read('JSON.stringify(p())');el('grid').onpointerdown(pointer({clientX:60,clientY:15}));el('grid').onpointercancel();assert.equal(read('JSON.stringify(p())'),before);assert.equal(read('history.undoStack.length'),0);});
setup();
check('right click deletes only the pointed sound even with a larger selection',()=>{read('selected=new Set(p().events.slice(0,3).map(e=>e.id));');const n=count();click(60,15,{button:2});assert.equal(count(),n-1);assert.equal(read('selected.size'),2);assert.equal(read('history.undoStack.length'),1);});
setup();
check('right click empty space creates no change and suppresses the context menu',()=>{const before=read('JSON.stringify(p())');click(30,15,{button:2});assert.equal(read('JSON.stringify(p())'),before);let prevented=false;el('grid').oncontextmenu({preventDefault(){prevented=true;}});assert.equal(prevented,true);});
setup();
check('Select double click places one snapped sound and selects it',()=>{const n=count();click(31);click(31);el('grid').ondblclick(pointer({clientX:31,clientY:15}));assert.equal(count(),n+1);assert.equal(read('p().events.find(e=>selected.has(e.id)).timeMs'),250);assert.equal(read('history.undoStack.length'),1);});
check('double click an existing sound does not duplicate it',()=>{const n=count();el('grid').ondblclick(pointer({clientX:30,clientY:15}));assert.equal(count(),n);});
setup();
check('Alt draw click toggles deletion while preserving the chosen Select tool',()=>{const n=count();click(60,15,{altKey:true});key('keyup','Alt');assert.equal(count(),n-1);assert.equal(read('tool'),'select');assert.equal(el('grid').dataset.tool,'select');});
setup();
check('waveform and lower-ruler clicks snap the playhead to the active grid',()=>{el('wave').onpointerdown(pointer({clientX:31}));assert.equal(read('cursor'),250);el('ruler').onpointerdown(pointer({clientX:47,clientY:32}));assert.equal(read('cursor'),375);});
setup();
check('empty-grid click snaps the playhead while preserving chart data',()=>{const before=read('JSON.stringify(p())');click(31,200);assert.equal(read('cursor'),250);assert.equal(read('JSON.stringify(p())'),before);});
setup();
check('playhead always uses grid divisions even if event snapping targets Objects or is disabled',()=>{read('snap=false;');el('snapMode').value='cursor';el('wave').onpointerdown(pointer({clientX:31}));assert.equal(read('cursor'),250);el('divisor').value='8';el('wave').onpointerdown(pointer({clientX:39}));assert.equal(read('cursor'),312.5);});
setup();
check('grid placement follows tempo changes and clamps negative positions',()=>{read('p().tempos=[{timeMs:0,bpm:120,meter:4},{timeMs:1000,bpm:60,meter:4}];');el('wave').onpointerdown(pointer({clientX:158}));assert.equal(read('cursor'),1250);el('wave').onpointerdown(pointer({clientX:-10}));assert.equal(read('cursor'),0);});
console.log(passed+' editing gesture and playhead checks passed.');
