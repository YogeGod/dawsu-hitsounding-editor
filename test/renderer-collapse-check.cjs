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



function setup(){reset();read('controllerCollapsed=false;rowH=31;view=0;scale=.12;active=p().rows[2].id;update();');}
const barCount=()=>{barRects=[];read('render()');return barRects.filter(r=>r[2]===6).length;};
setup();
check('collapsing the controller hides its body and keeps an accessible reopen button',()=>{el('toggleController').click();assert.equal(el('controllerBody').hidden,true);assert.equal(el('toggleController').getAttribute('aria-expanded'),'false');assert.equal(el('toggleController').getAttribute('aria-label'),'Show controller lane');assert.equal(barCount(),0);});
check('reopening restores the full active sample lane',()=>{el('toggleController').click();assert.equal(el('controllerBody').hidden,false);assert.equal(el('toggleController').getAttribute('aria-expanded'),'true');assert.equal(barCount(),32);});
check('collapse and reopen preserve chart, selection, controller property, zoom and undo history',()=>{read('selected=new Set([p().events.find(e=>e.rowId===active).id]);rowH=60;scale=.4;view=500;update();');el('controller').value='velocity';const visibleBars=barCount(),project=read('JSON.stringify(p())'),selected=read('JSON.stringify([...selected])');el('toggleController').click();el('toggleController').click();assert.equal(read('JSON.stringify(p())'),project);assert.equal(read('JSON.stringify([...selected])'),selected);assert.equal(el('controller').value,'velocity');assert.equal(read('rowH'),60);assert.equal(read('scale'),.4);assert.equal(read('view'),500);assert.equal(read('history.undoStack.length'),0);assert.equal(barCount(),visibleBars);});
setup();
check('hiding an unfinished controller drag rolls it back without an undo entry',()=>{const project=read('JSON.stringify(p())');el('bars').onpointerdown(pointer({clientX:30,clientY:20}));assert.notEqual(read('JSON.stringify(p())'),project);read('setControllerCollapsed(true)');assert.equal(read('JSON.stringify(p())'),project);assert.equal(read('gesture'),null);assert.equal(read('history.undoStack.length'),0);assert.equal(doc.activeElement,el('grid'));});
setup();
check('hidden controller cannot receive stale editing gestures',()=>{read('setControllerCollapsed(true)');const project=read('JSON.stringify(p())');el('bars').onpointerdown(pointer({clientX:30,clientY:20}));assert.equal(read('gesture'),null);assert.equal(read('JSON.stringify(p())'),project);});
setup();
check('Space on the collapse button keeps native button activation instead of starting playback',()=>{let plays=0;el('play').onclick=()=>plays++;el('toggleController').focus();const ev=key('keydown',' ');assert.equal(ev.defaultPrevented,false);assert.equal(plays,0);el('toggleController').click();assert.equal(el('controllerBody').hidden,true);});
setup();read('inspectorCollapsed=false;lanesCollapsed=false;syncSidebars();');
check('inspector has a working collapse and reopen button',()=>{el('toggleInspector').click();assert.equal(read('inspectorCollapsed'),true);assert.equal(el('toggleInspector').getAttribute('aria-expanded'),'false');assert.equal(el('toggleInspector').getAttribute('aria-label'),'Show inspector');el('toggleInspector').click();assert.equal(read('inspectorCollapsed'),false);});
check('sample-name sidebar collapses independently and remains reopenable',()=>{el('toggleLanes').click();assert.equal(read('lanesCollapsed'),true);assert.equal(read('inspectorCollapsed'),false);assert.equal(el('toggleLanes').getAttribute('aria-expanded'),'false');el('toggleLanes').click();assert.equal(el('toggleLanes').getAttribute('aria-expanded'),'true');});
check('sidebar layout changes preserve timing selection zoom scroll and undo',()=>{read('selected=new Set([p().events[0].id]);view=1200;scale=.3;rowH=96;scrollY=40;update();');const before=read('JSON.stringify({p:p(),selected:[...selected],view,scale,scrollY,active,undo:history.undoStack,dirty})');el('toggleInspector').click();el('toggleLanes').click();el('toggleLanes').click();el('toggleInspector').click();assert.equal(read('JSON.stringify({p:p(),selected:[...selected],view,scale,scrollY,active,undo:history.undoStack,dirty})'),before);});
check('Ctrl+F restores sample names before focusing the filter',()=>{read('setLanesCollapsed(true);');el('grid').focus();key('keydown','f',{ctrlKey:true});assert.equal(read('lanesCollapsed'),false);assert.equal(doc.activeElement,el('filter'));});
check('collapsing a focused inspector control returns focus to the timeline',()=>{el('rowName').parentElement=el('inspector');el('rowName').focus();read('setInspectorCollapsed(true)');assert.equal(doc.activeElement,el('grid'));read('setInspectorCollapsed(false)');});
check('Space on sidebar toggle buttons does not start playback',()=>{let plays=0;el('play').onclick=()=>plays++;for(const id of ['toggleInspector','toggleLanes']){el(id).focus();assert.equal(key('keydown',' ').defaultPrevented,false);}assert.equal(plays,0);});
check('sidebar toggles ignore active editing gestures',()=>{read('gesture={type:"move"};');const before=read('JSON.stringify([inspectorCollapsed,lanesCollapsed])');el('toggleInspector').click();el('toggleLanes').click();assert.equal(read('JSON.stringify([inspectorCollapsed,lanesCollapsed])'),before);read('gesture=null;');});
check('layout preferences persist separately from the chart',()=>{const storage=new Map();sandbox.localStorage={setItem:(k,v)=>storage.set(k,v),getItem:k=>storage.get(k)};const before=read('JSON.stringify(p())');read('setInspectorCollapsed(true);setLanesCollapsed(true);');assert.deepEqual(JSON.parse(storage.get('hse-panel-layout')),{inspectorCollapsed:true,lanesCollapsed:true});assert.equal(read('JSON.stringify(p())'),before);});
check('controller and both sidebars can be collapsed independently',()=>{read('setControllerCollapsed(true)');assert.equal(read('inspectorCollapsed&&lanesCollapsed&&controllerCollapsed'),true);read('setControllerCollapsed(false)');assert.equal(read('inspectorCollapsed&&lanesCollapsed&&!controllerCollapsed'),true);});
check('selection info collapses and reopens without changing the chart or selection',()=>{read('infoCollapsed=false;syncInfoPanel();');const before=read('JSON.stringify({p:p(),selection:[...selected],undo:history.undoStack,dirty,cursor,scale,view})');el('toggleInfo').click();assert.equal(el('info').hidden,true);assert.equal(el('toggleInfo').getAttribute('aria-expanded'),'false');assert.equal(el('toggleInfo').getAttribute('aria-label'),'Show selection info');el('toggleInfo').click();assert.equal(el('info').hidden,false);assert.equal(read('JSON.stringify({p:p(),selection:[...selected],undo:history.undoStack,dirty,cursor,scale,view})'),before);});
check('selection-info visibility persists separately and hiding a focused field returns focus to the grid',()=>{el('eventTime').parentElement=el('info');el('eventTime').focus();read('setInfoCollapsed(true)');assert.equal(doc.activeElement,el('grid'));assert.equal(sandbox.localStorage.getItem('hse-info-collapsed'),'true');read('setInfoCollapsed(false)');assert.equal(sandbox.localStorage.getItem('hse-info-collapsed'),'false');el('toggleInfo').focus();assert.equal(key('keydown',' ').defaultPrevented,false);});
console.log(passed+' controller collapse checks passed.');
