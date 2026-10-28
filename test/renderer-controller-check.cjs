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


function setup(){reset();read('active=p().rows[2].id;view=0;scale=.12;tool="select";update();');el("controller").value="volume";el("valueMode").value="relative";}
const values=()=>read('JSON.stringify(p().events.map(e=>[e.id,e.volume,e.performance?.velocity,e.timeMs,e.rowId,e.ownerId]))');
const value=t=>read('p().events.find(e=>e.rowId===active&&e.timeMs==='+t+').volume');
const barCount=()=>{barRects=[];read('render()');return barRects.filter(r=>r[2]===6).length;};
function drag(ms,y){el('bars').onpointerdown(pointer({clientX:ms*.12,clientY:y}));el('bars').onpointerup();}
setup();
check("full active lane stays visible after selecting a timeline event",()=>{assert.equal(barCount(),32);read('selected=new Set([p().events.find(e=>e.rowId===active&&e.timeMs===250).id]);update();');assert.equal(barCount(),32);assert.ok(el('controllerScope').textContent.includes('drum-hitwhistle.wav'));assert.ok(el('controllerScope').textContent.includes('1 selected'));});
check("editing a bar keeps all 32 bars visible",()=>{drag(250,60);assert.equal(value(250),80);assert.equal(barCount(),32);assert.equal(read('selected.size'),1);});
check("clicking a different bar changes that sound instead of the old selection",()=>{drag(500,120);assert.equal(value(500),60);assert.equal(value(250),80);assert.equal(read('p().events.find(e=>selected.has(e.id)).timeMs'),500);assert.equal(barCount(),32);});
check("each controller drag is one undo step and redo preserves full display",()=>{assert.equal(read('history.undoStack.length'),2);read('history.undo();update();');assert.equal(value(500),65);read('history.redo();update();');assert.equal(value(500),60);assert.equal(barCount(),32);});
setup();
check("selected group edits stay within the active sample row",()=>{read('selected=new Set([...p().events.filter(e=>e.rowId===active&&[0,250].includes(e.timeMs)).map(e=>e.id),p().events[0].id]);');const other=read('p().events[0].volume');drag(0,60);assert.equal(value(0),80);assert.equal(value(250),57);assert.equal(read('p().events[0].volume'),other);assert.equal(barCount(),32);});
setup();
check("a selection in another lane cannot redirect an active-lane bar edit",()=>{read('selected=new Set([p().events[0].id]);update();');const before=read('p().events[0].volume');drag(500,90);assert.equal(value(500),70);assert.equal(read('p().events[0].volume'),before);});
setup();
check("empty controller space does not change a distant selected bar",()=>{read('selected=new Set([p().events.find(e=>e.rowId===active).id]);');const before=values();drag(8125,40);assert.equal(values(),before);assert.equal(read('history.undoStack.length'),0);});
setup();
check("Alt draw edits other bars even with an existing single selection",()=>{read('selected=new Set([p().events.find(e=>e.rowId===active&&e.timeMs===0).id]);');const before=value(0);el('bars').onpointerdown(pointer({clientX:60,clientY:90,altKey:true}));el('bars').onpointermove(pointer({clientX:90,clientY:120,altKey:true}));el('bars').onpointerup();key('keyup','Alt');assert.equal(value(500),70);assert.equal(value(750),60);assert.equal(value(0),before);assert.equal(barCount(),32);assert.equal(read('history.undoStack.length'),1);});
setup();
check("switching sample rows displays that complete lane regardless of old selection",()=>{read('selected=new Set([p().events.find(e=>e.rowId===active).id]);active=p().rows[1].id;update();');assert.equal(barCount(),8);assert.ok(el('controllerScope').textContent.includes('soft-hitclap.wav'));});
setup();
check("velocity editing uses the same visible-lane scope without touching volume or timing",()=>{el('controller').value='velocity';read('selected=new Set([p().events.find(e=>e.rowId===active&&e.timeMs===0).id]);');const original=read('JSON.stringify(p().events.map(e=>[e.id,e.volume,e.timeMs,e.rowId,e.ownerId]))');drag(500,60);assert.equal(read('p().events.find(e=>e.rowId===active&&e.timeMs===500).performance.velocity'),102);assert.equal(read('JSON.stringify(p().events.map(e=>[e.id,e.volume,e.timeMs,e.rowId,e.ownerId]))'),original);assert.equal(barCount(),32);});
setup();
check("ramp targets only selected sounds in the active row",()=>{read('selected=new Set([...p().events.filter(e=>e.rowId===active&&[0,500].includes(e.timeMs)).map(e=>e.id),p().events[0].id]);');const other=read('p().events[0].volume'),middle=value(250);el('bars').onpointerdown(pointer({clientX:0,clientY:60,shiftKey:true}));el('bars').onpointermove(pointer({clientX:60,clientY:240,shiftKey:true}));el('bars').onpointerup();assert.equal(value(0),80);assert.equal(value(500),20);assert.equal(value(250),middle);assert.equal(read('p().events[0].volume'),other);assert.equal(barCount(),32);});
setup();
check("cancelled controller edit restores all chart values",()=>{const original=values();el('bars').onpointerdown(pointer({clientX:30,clientY:20}));el('bars').onpointercancel();assert.equal(values(),original);assert.equal(read('history.undoStack.length'),0);assert.equal(barCount(),32);});
console.log(passed+' controller regression checks passed.');

setup();
check("Ctrl+A alone enables the all-row controller view",()=>{const before=values();key('keydown','a',{ctrlKey:true});assert.equal(read('allControllerSelected()'),true);assert.equal(read('controllerTargets().length'),read('p().events.length'));assert.ok(el('controllerScope').textContent.includes('All selected'));assert.equal(values(),before);read('selected=new Set([p().events[0].id]);update();');assert.equal(read('!!allControllerSelected()'),false);assert.equal(read('controllerTargets().length'),32);});
setup();
check("normal multi-row selections retain single-row controller scope",()=>{read('selected=new Set(p().events.map(e=>e.id));update();');assert.equal(read('!!allControllerSelected()'),false);assert.equal(barCount(),32);});
setup();
check("simultaneous Ctrl+A volume bars get separate horizontal slots",()=>{key('keydown','a',{ctrlKey:true});const bars=JSON.parse(read('JSON.stringify(controllerBars().filter(b=>b.event.timeMs===0).map(b=>({x:b.x,width:b.width})))'));assert.ok(bars.length>1);assert.equal(new Set(bars.map(b=>b.x)).size,bars.length);for(let i=1;i<bars.length;i++)assert.ok(bars[i].x>=bars[i-1].x+bars[i-1].width-.001);});

setup();
check("repeated upward overscroll is clamped before reusing cached row elements",()=>{const snapshot=values();for(let i=0;i<10;i++){read('scrollY=-240;update();');assert.equal(read('scrollY'),0);assert.equal(read('JSON.parse(laneRenderKey)[1]'),0);assert.equal(read('Number($("vertical").value)'),0);}assert.equal(values(),snapshot);});
setup();
check("repeated bottom overscroll and height changes cannot poison row cache positions",()=>{read('rowH=96;update();');for(let i=0;i<10;i++){read('scrollY=10000;update();');const max=read('Math.max(0,visibleRows().length*rowH-$("grid").clientHeight)');assert.equal(read('scrollY'),max);assert.equal(read('JSON.parse(laneRenderKey)[1]'),max);}el('grid').clientHeight=500;read('update();');assert.equal(read('scrollY'),0);assert.equal(read('JSON.parse(laneRenderKey)[1]'),0);});
