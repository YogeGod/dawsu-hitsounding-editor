const fs=require("fs"),vm=require("vm"),assert=require("assert/strict");
const root=require("path").resolve(__dirname,"..")+"/",H=require(root+"src/model.js"),handlers={},docHandlers={},elements=new Map(),all=[],timers=new Map();let nextTimer=0;
let barRects=[];const barCtx=new Proxy({},{get:(_,key)=>key==="fillRect"?((...args)=>barRects.push(args)):(()=>{})});
let gridPaths=[],pathPoints=[];const gridCtx=new Proxy({},{get:(_,key)=>key==="beginPath"?(()=>{pathPoints=[];}):key==="moveTo"||key==="lineTo"?((...point)=>pathPoints.push(point)):key==="closePath"?(()=>gridPaths.push(pathPoints)):(()=>{})});
const ctx=new Proxy({},{get:()=>()=>{}});
let doc;
function el(id="",tagName="DIV"){
 if(elements.has(id))return elements.get(id);
 const node={id,tagName:tagName.toUpperCase(),dataset:{},attributes:{},children:[],classList:{toggle(){},remove(){},add(){}},style:{},value:"",title:"",type:"",clientWidth:1000,clientHeight:300,open:false,checked:false,hidden:id==="hoverHelp",disabled:false,isConnected:true,textContent:"",
 setAttribute(k,v){this.attributes[k]=v;if(k==="title")this.title=v;},getAttribute(k){return k==="title"?this.title:this.attributes[k]??null;},hasAttribute(k){return k==="title"?!!this.title:k in this.attributes;},removeAttribute(k){delete this.attributes[k];if(k==="title")this.title="";},
 contains(other){for(let n=other;n;n=n.parentElement)if(n===this)return true;return false;},
 closest(){for(let n=this;n;n=n.parentElement)if(n.dataset.help||n.title)return n;return null;},
 focus(){doc.activeElement=this;},select(){this.selectedText=true;},
 setPointerCapture(){},getBoundingClientRect(){return {left:0,top:0,bottom:300,width:250,height:80};},getContext(){return this.id==="bars"?barCtx:this.id==="grid"?gridCtx:ctx;},
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



const approx=(a,b)=>assert.ok(Math.abs(a-b)<1e-7,a+' != '+b);
function setup(many=false){reset();read('rowH=31;scale=.12;view=0;scrollY=0;');if(many)read('for(let i=0;i<26;i++)p().rows.push({...p().rows[0],id:"extra"+i});');read('update();');}
function wheel(id,extra={}){const ev={...pointer({clientX:200,clientY:150}),deltaY:-120,deltaX:0,deltaMode:0,defaultPrevented:false,preventDefault(){this.defaultPrevented=true;},...extra};el(id).onwheel(ev);return ev;}
function geometry(scale){read('scale='+scale+';render();');gridPaths=[];read('render();');const p=gridPaths[0];return {w:Math.max(...p.map(p=>p[0]))-Math.min(...p.map(p=>p[0])),h:Math.max(...p.map(p=>p[1]))-Math.min(...p.map(p=>p[1]))};}
setup();
check('rendered diamonds get smaller and larger with horizontal zoom',()=>{const a=geometry(.03),b=geometry(.12),c=geometry(.48);assert.ok(a.w<b.w&&b.w<c.w);assert.ok(a.h<b.h&&b.h<c.h);approx(b.w,12);approx(b.h,12);});
check('diamonds stay readable and within their rows at both zoom limits',()=>{for(const height of [18,31,96])for(const scale of [.0001,5]){read('rowH='+height+';scale='+scale+';');const size=read('diamondSize()');approx(size.w,size.h);assert.ok(size.w>=2&&size.w<=14);assert.ok(size.h>=3&&size.h*2<height);}});
setup(true);
check('Ctrl+Shift+wheel zooms vertically around the pointer without horizontal zoom',()=>{read('scrollY=200;view=500;');const old=read('(scrollY+150)/rowH');const ev=wheel('grid',{ctrlKey:true,shiftKey:true});assert.equal(ev.defaultPrevented,true);assert.ok(read('rowH')>31);approx(read('(scrollY+150)/rowH'),old);assert.equal(read('view'),500);approx(read('scale'),.12);});
check('row labels, row virtualization and scrollbar use the zoomed row height',()=>{const height=read('rowH'),holder=el('rows').children[0];for(const row of holder.children)assert.equal(row.style.height,height+'px');approx(Number(el('vertical').max),30*height-el('grid').clientHeight);approx(Number(el('rowHeight').value),height);});
check('small trackpad deltas accumulate instead of being rounded away',()=>{const before=read('rowH');for(let i=0;i<10;i++)wheel('rows',{ctrlKey:true,shiftKey:true,deltaY:-1});assert.ok(read('rowH')>before);});
check('vertical zoom has hard upper and lower bounds',()=>{wheel('grid',{ctrlKey:true,shiftKey:true,deltaY:-1e8});assert.equal(read('rowH'),96);wheel('grid',{ctrlKey:true,shiftKey:true,deltaY:1e8});assert.equal(read('rowH'),18);});
setup(true);
check('row-height slider zooms vertically without creating an edit transaction',()=>{const data=read('JSON.stringify(p())');el('rowHeight').value='62';el('rowHeight').oninput();assert.equal(read('rowH'),62);assert.equal(el('rowHeightValue').textContent,'62 px');assert.equal(read('JSON.stringify(p())'),data);assert.equal(read('history.undoStack.length'),0);assert.equal(read('dirty'),false);});
setup(true);
check('wheel zoom also works over the ruler, waveform and controller',()=>{for(const id of ['ruler','wave','objects','bars']){const old=read('rowH');wheel(id,{ctrlKey:true,shiftKey:true,deltaY:-20});assert.ok(read('rowH')>old);}});
setup();
check('Ctrl+wheel retains horizontal zoom and the time under the pointer',()=>{read('view=2000;');const time=read('timeAt(200)');wheel('grid',{ctrlKey:true});assert.ok(read('scale')>.12);assert.equal(read('rowH'),31);approx(read('timeAt(200)'),time);});
setup(true);
check('unmodified wheel still scrolls rows and Shift+wheel scrolls time',()=>{wheel('grid',{deltaY:80});assert.equal(read('scrollY'),80);wheel('grid',{shiftKey:true,deltaY:120});assert.equal(read('view'),1000);assert.equal(read('rowH'),31);});
setup();
check('hit testing follows the enlarged diamond width and ignores empty row space',()=>{read('scale=.48;rowH=62;');const size=read('diamondSize()');assert.equal(read('hit(240+'+(size.w+1)+',31).timeMs'),500);assert.equal(read('hit(240,2)'),undefined);});
setup();
check('drawing after vertical zoom inserts into the visually pointed sample row',()=>{read('setRowHeight(60,0);');const count=read('p().events.length'),id=read('p().rows[2].id');el('grid').onpointerdown(pointer({clientX:255,clientY:150,altKey:true}));el('grid').onpointerup();key('keyup','Alt');assert.equal(read('p().events.length'),count+1);assert.equal(read('p().events.at(-1).rowId'),id);approx(read('p().events.at(-1).timeMs'),2125);});
setup();
check('moving an event to another expanded row preserves its time and owner',()=>{read('setRowHeight(60,0);');const owner=read('p().events.find(e=>e.rowId===p().rows[2].id&&e.timeMs===500).ownerId');el('grid').onpointerdown(pointer({clientX:60,clientY:150}));el('grid').onpointermove(pointer({clientX:60,clientY:210}));el('grid').onpointerup();assert.equal(read('p().events.find(e=>selected.has(e.id)).rowId'),read('p().rows[3].id'));approx(read('p().events.find(e=>selected.has(e.id)).timeMs'),500);assert.equal(read('p().events.find(e=>selected.has(e.id)).ownerId'),owner);});
setup();
check('row grip reordering follows expanded row boundaries',()=>{read('setRowHeight(60,0);');const first=read('p().rows[0].id');el('rows').children[0].children[0].children[0].onpointerdown({...pointer(),stopPropagation(){}});el('rows').onpointermove(pointer({clientY:230}));el('rows').onpointerup();assert.equal(read('p().rows[3].id'),first);});
setup();
check('wheel zoom is ignored during an event gesture',()=>{el('grid').onpointerdown(pointer({clientX:60,clientY:15}));wheel('grid',{ctrlKey:true,shiftKey:true});assert.equal(read('rowH'),31);el('grid').onpointercancel();});
setup();
check('page and line wheel deltas scale zoom consistently',()=>{wheel('grid',{ctrlKey:true,shiftKey:true,deltaMode:1,deltaY:-2});const lines=read('rowH');read('rowH=31;scrollY=0;');wheel('grid',{ctrlKey:true,shiftKey:true,deltaY:-32});approx(read('rowH'),lines);});
console.log(passed+' zoom and marker regression checks passed.');
