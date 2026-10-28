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
vm.createContext(sandbox);vm.runInContext(fs.readFileSync(root+"src/help.js","utf8"),sandbox);vm.runInContext(fs.readFileSync(root+"src/sample-groups.js","utf8"),sandbox);vm.runInContext(fs.readFileSync(root+"src/app.js","utf8"),sandbox);
const read=s=>vm.runInContext(s,sandbox);
function key(type,k,extra={}){const ev={key:k,altKey:k==="Alt"&&type==="keydown",ctrlKey:false,metaKey:false,shiftKey:false,defaultPrevented:false,preventDefault(){this.defaultPrevented=true;},getModifierState:()=>false,...extra};for(const fn of handlers[type]||[])fn(ev);return ev;}
function pointer(extra={}){return {button:0,clientX:15,clientY:15,pointerId:1,altKey:false,ctrlKey:false,metaKey:false,shiftKey:false,preventDefault(){},getModifierState:()=>false,...extra};}
function dispatch(type,event){for(const f of docHandlers[type]||[])f(event);}
function flush(){const queue=[...timers.values()];timers.clear();for(const f of queue)f();}
function reset(){read('history=new H.History(H.demo());selected=new Set();clipboard=null;tool="select";cursor=0;gesture=null;altDraw=false;active=p().rows[0].id;update();');doc.activeElement={tagName:"BODY"};}
let passed=0;const check=(name,fn)=>{fn();passed++;console.log("PASS "+name);};



read('history=new H.History(SG.defaultProject());resetSampleGroups(p());active=p().rows[0].id;selected.clear();update();');
check('folder headings and real lane rows use a shared grid coordinate system',()=>{const rows=JSON.parse(read('JSON.stringify(visibleRows())'));assert.equal(rows[0]._group,true);assert.equal(rows[1].name,'normal-hitnormal.wav');assert.equal(rows[2].name,'normal-hitwhistle.wav');assert.equal(rows.some(r=>r.name==='Additions'),false);assert.equal(read('hit(0,rowH/4)'),null);const before=read('p().events.length');el('grid').onpointerdown(pointer({clientY:15}));el('grid').onpointerup();assert.equal(read('p().events.length'),before);});
check('folder headers stay exactly half-height at every vertical zoom',()=>{for(const h of [18,31,96]){read('rowH='+h+';update();');const rows=JSON.parse(read('JSON.stringify(visibleRows())'));for(let i=0;i<rows.length;i++){assert.equal(read('rowTop('+(i+1)+')-rowTop('+i+')'),rows[i]._group?h/2:h);assert.equal(read('rowIndexAt(rowTop('+i+')+'+(rows[i]._group?h/4:h/2)+')'),i);}for(const holder of el('rows').children)for(const node of holder.children)if(node.dataset.groupKey)assert.equal(node.style.height,(h/2)+'px');}read('rowH=31;update();');});
check('drawing on a real grouped lane preserves its row ID and mapping',()=>{read('tool="draw";');el('grid').onpointerdown(pointer({clientY:30,clientX:30}));el('grid').onpointerup();assert.equal(read('p().events.length'),1);assert.equal(read('p().events[0].rowId'),read('p().rows[0].id'));read('tool="select";');});
check('plus adds successive layers, reveals them, and supports undo',()=>{const base=read('p().rows.find(r=>r.name==="normal-hitclap.wav").id');read('addSampleLayer('+JSON.stringify(base)+')');assert.equal(read('activeRow().name'),'normal-hitclap2.wav');assert.ok(read('visibleRows().some(r=>r.id===active)'));read('addSampleLayer('+JSON.stringify(base)+')');assert.equal(read('activeRow().name'),'normal-hitclap3.wav');read('history.undo();update();');assert.equal(read('p().rows.some(r=>r.name==="normal-hitclap3.wav")'),false);});
check('collapsing a family hides only its numbered rows without removing sounds',()=>{const before=read('JSON.stringify(p().events)');read('toggleSampleFamily("normal:clap");');assert.equal(read('visibleRows().some(r=>r.name==="normal-hitclap2.wav")'),false);assert.equal(read('JSON.stringify(p().events)'),before);read('toggleSampleFamily("normal:clap");');assert.equal(read('visibleRows().some(r=>r.name==="normal-hitclap2.wav")'),true);});
check('sample-set folders stay aligned under repeated upward and downward scrolling',()=>{read('groupState.closed.clear();groupState.expanded.clear();rowH=31;update();');for(let i=0;i<8;i++){read('scrollY=-240;update();');assert.equal(read('scrollY'),0);assert.equal(read('JSON.parse(laneRenderKey)[1]'),0);}read('scrollY=100000;update();');assert.equal(read('scrollY'),read('Math.max(0,rowsHeight()-$("grid").clientHeight)'));});
check('all samplesets use the same type tints and addition icons before lane controls',()=>{read('scrollY=0;groupState.closed.clear();update();');for(const kind of ['normal','whistle','finish','clap']){assert.equal(read('laneBackground({name:"normal-hit'+kind+'.wav"})'),read('laneBackground({name:"drum-hit'+kind+'20.wav"})'));assert.equal(read('laneBackground({name:"normal-hit'+kind+'.wav"})'),read('laneBackground({name:"soft-hit'+kind+'.wav"})'));}for(const holder of el('rows').children)for(const node of holder.children){if(!node.dataset.rowId)continue;const kind=node.dataset.sampleKind,icon=node.children.find(c=>c.className==='laneKindIcon');assert.ok(icon);assert.equal(icon.src,'src/icons/hit'+kind+'.svg');assert.ok(node.children.indexOf(icon)<node.children.findIndex(c=>c.className==='addLayer'));}read('update();');const row=read('p().rows.find(r=>r.name==="normal-hitwhistle.wav").id');read('active='+JSON.stringify(row)+';update();');assert.equal(read('laneElements.get(active).style.backgroundColor'),read('laneBackground(activeRow(),true)'));});
console.log(passed+' grouped renderer checks passed.');

(async()=>{read('audition=async()=>{};');sandbox.desktop.samples=async()=>[{name:'my-snare.ogg',assetKey:'assigned-snare'}];read('active=p().rows.find(r=>r.name==="normal-hitclap2.wav").id;update();');await el('replaceSample').onclick();check('choosing a source file keeps numbered family identity',()=>{assert.equal(read('activeRow().name'),'normal-hitclap2.ogg');assert.equal(read('activeRow().assetKey'),'assigned-snare');assert.equal(read('activeRow().sampleMapping.index'),2);});})().catch(e=>{console.error(e);process.exitCode=1;});
