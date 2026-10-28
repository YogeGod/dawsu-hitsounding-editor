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



function setup(){reset();read('rowH=31;scale=.12;view=0;scrollY=0;snap=true;tool="draw";');el('divisor').value='4';el('snapMode').value='grid';}
const count=()=>read('p().events.length');
function down(x,y=15,extra={}){el('grid').onpointerdown(pointer({clientX:x,clientY:y,...extra}));}
function move(x,y=15,extra={}){el('grid').onpointermove(pointer({clientX:x,clientY:y,...extra}));}
const up=()=>el('grid').onpointerup();
setup();
check('Draw inserts immediately on mouse-down in empty space',()=>{const n=count();down(30);assert.equal(count(),n+1);assert.equal(read('gesture.mode'),'draw');up();assert.equal(read('history.undoStack.length'),1);});
setup();
check('Draw deletes the pointed sound immediately on mouse-down',()=>{const n=count();down(60);assert.equal(count(),n-1);assert.equal(read('gesture.mode'),'erase');up();assert.equal(read('history.undoStack.length'),1);});
setup();
check('an occupied start erases the row across a sparse pointer update and creates nothing',()=>{const n=count();down(60);move(180);up();assert.equal(count(),n-3);assert.equal(read('p().events.some(e=>e.rowId===active&&e.timeMs>=500&&e.timeMs<=1500)'),false);});
setup();
check('erasing through empty cells never inserts sounds',()=>{const n=count();down(60);move(105);up();assert.equal(count(),n-1);});
setup();
check('an empty start repeats grid-aligned insertions without deleting occupied cells',()=>{const n=count(),old=read('p().events.find(e=>e.rowId===active&&e.timeMs===500).id');down(15);move(90);up();assert.equal(count(),n+5);assert.equal(read('p().events.some(e=>e.id==='+JSON.stringify(old)+')'),true);});
check('a complete insert stroke is one undo and redo step',()=>{const after=read('JSON.stringify(p())');read('history.undo();history.redo();');assert.equal(read('JSON.stringify(p())'),after);assert.equal(read('history.undoStack.length'),1);});
setup();
check('retracing an insert stroke neither duplicates nor removes sounds',()=>{const n=count();down(15);move(90);const after=count();move(15);move(90);up();assert.equal(count(),after);assert.equal(count(),n+5);});
setup();
check('retracing an erase stroke never recreates deleted sounds',()=>{const n=count();down(60);move(180);move(60);move(180);up();assert.equal(count(),n-3);});
setup();
check('erase stroke remains on the starting row when the pointer crosses another row',()=>{const before=read('JSON.stringify(p().events.filter(e=>e.rowId!==active))');down(60);move(180,77);up();assert.equal(read('JSON.stringify(p().events.filter(e=>e.rowId!==active))'),before);});
setup();
check('erase sweep removes imported off-grid sounds rather than only exact snap cells',()=>{read('const e=p().events.find(e=>e.rowId===active&&e.timeMs===1000);e.timeMs=1007;');const n=count();down(60);move(180);up();assert.equal(count(),n-3);assert.equal(read('p().events.some(e=>e.rowId===active&&e.timeMs===1007)'),false);});
setup();
check('free placement with Snap off creates one sound per stroke, not a mouse-move spray',()=>{read('snap=false;');const n=count();down(31);move(70);move(110);up();assert.equal(count(),n+1);assert.ok(Math.abs(read('p().events.at(-1).timeMs')-31/.12)<1e-7);});
setup();
check('snap settings are frozen for the duration of an insert stroke',()=>{down(15);read('snap=false;');move(90);up();assert.equal(read('p().events.filter(e=>e.rowId===active&&[125,250,375,625,750].includes(e.timeMs)).length'),5);});
setup();
check('a second click after release can delete a sound placed by the first stroke',()=>{const n=count();down(30);up();assert.equal(count(),n+1);down(30);up();assert.equal(count(),n);assert.equal(read('history.undoStack.length'),2);});
setup();
check('cancel restores an immediately deleted sound and the rest of the stroke',()=>{const original=read('JSON.stringify(p())');down(60);move(180);el('grid').onpointercancel();assert.equal(read('JSON.stringify(p())'),original);assert.equal(read('history.undoStack.length'),0);});
setup();
check('Alt release during an erase gesture does not switch it back to insertion',()=>{read('tool="select"');down(60,15,{altKey:true});key('keyup','Alt');move(180);up();assert.equal(read('tool'),'select');assert.equal(read('p().events.some(e=>e.rowId===active&&e.timeMs>=500&&e.timeMs<=1500)'),false);});
setup();
check('draw strokes preserve gameplay geometry, unrelated row values and MIDI mapping',()=>{const objects=read('JSON.stringify(p().objects)'),other=read('JSON.stringify(p().events.filter(e=>e.rowId!==active))'),rows=read('JSON.stringify(p().rows)');down(15);move(90);up();assert.equal(read('JSON.stringify(p().objects)'),objects);assert.equal(read('JSON.stringify(p().events.filter(e=>e.rowId!==active))'),other);assert.equal(read('JSON.stringify(p().rows)'),rows);});
console.log(passed+' Draw insertion/deletion checks passed.');
