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



sandbox.HSESampleRender=require(root+'src/sample-render');
function setup(){if(read('renamingLane'))read('finishLaneRename(false);');reset();read('dirty=false;scrollY=0;lanesCollapsed=false;update();');}
(async()=>{
 setup();const originalName=read('activeRow().name'),before=read('JSON.stringify(p().events)');read('beginLaneRename(active);');assert.ok(read('renamingLane'));let input=read('renamingLane.input');input.value='Snare';input.onkeydown({key:'Enter',stopPropagation(){},preventDefault(){}});check('Enter commits a visual lane label without changing the sample or notes',()=>{assert.equal(read('activeRow().label'),'Snare');assert.equal(read('activeRow().name'),originalName);assert.equal(read('JSON.stringify(p().events)'),before);assert.equal(el('rowName').value,'Snare');assert.equal(el('sampleFilename').textContent,'Sample: '+originalName);});
 read('beginLaneRename(active);');input=read('renamingLane.input');input.value='Cancelled';input.onkeydown({key:'Escape',stopPropagation(){},preventDefault(){}});check('Escape cancels lane renaming without creating another undo step',()=>{assert.equal(read('activeRow().label'),'Snare');assert.equal(read('history.undoStack.length'),1);});
 el('filter').value='snare';read('update();');check('lane filtering matches aliases as well as sample filenames',()=>assert.equal(read('visibleRows().length'),1));el('filter').value=originalName;read('update();');assert.equal(read('visibleRows().length'),1);el('filter').value='';
 setup();read('lanesCollapsed=true;update();');el('rows').ondblclick({target:{closest:selector=>selector==='button,input'?null:{dataset:{rowId:read('active')}}},preventDefault(){},clientX:40,clientY:15});check('double-click opens the inline name field and expands collapsed names',()=>{assert.ok(read('renamingLane'));assert.equal(read('lanesCollapsed'),false);});read('finishLaneRename(false);');
 setup();const count=read('p().rows.length'),events=read('JSON.stringify(p().events)');el('addLane').click();check('Add lane creates an empty mapped lane and starts inline naming',()=>{assert.equal(read('p().rows.length'),count+1);assert.equal(read('JSON.stringify(p().events)'),events);assert.ok(read('renamingLane'));});read('finishLaneRename(false);');await el('deleteLane').onclick();check('empty lanes can be removed and restored with Undo',()=>{assert.equal(read('p().rows.length'),count);el('undo').click();assert.equal(read('p().rows.length'),count+1);});
 setup();const snapshot=read('JSON.stringify(p())');const deleting=el('deleteLane').onclick();check('deleting a populated lane explains that its hitsounds are removed',()=>{assert.equal(el('dialog').open,true);assert.ok(el('dialogBody').children[0].textContent.includes('Gameplay objects and sample files are kept'));});el('dialog').returnValue='cancel';el('dialog').open=false;el('dialog').onclose();await deleting;assert.equal(read('JSON.stringify(p())'),snapshot);
 const confirm=el('deleteLane').onclick();el('dialog').returnValue='apply';el('dialog').open=false;el('dialog').onclose();await confirm;check('confirmed lane deletion preserves gameplay objects and supports Undo',()=>{assert.equal(read('p().rows.length'),3);el('undo').click();assert.equal(read('JSON.stringify(p())'),snapshot);});
 setup();el('rowName').value='Inspector label';await el('rowName').onchange();check('the inspector name field is visual-only too',()=>{assert.equal(read('activeRow().label'),'Inspector label');assert.equal(read('activeRow().name'),originalName);});
 let decodes=0,reads=0;sandbox.OfflineAudioContext=class {async decodeAudioData(){decodes++;return {sampleRate:48000,numberOfChannels:1,getChannelData:()=>new Float32Array([.5,.25])};}};sandbox.atob=s=>Buffer.from(s,'base64').toString('binary');sandbox.desktop.asset=async()=>{reads++;return {data:'AAAA'};};read('ensureAudioOutput=()=>{throw Error("Native audio must not be opened for export");};');sandbox.planFixture={renderRequests:[{ownerId:'one',layers:[{assetKey:'a',volume:80},{assetKey:'b',volume:30}]},{ownerId:'two',layers:[{assetKey:'b',volume:30},{assetKey:'a',volume:80}]}]};const rendered=await read('renderExportMixes(planFixture)');check('export decoding is offline, deduplicates repeated mixes and preserves the project',()=>{assert.equal(reads,2);assert.equal(decodes,2);assert.equal(rendered.mixes.length,2);assert.equal(rendered.mixes[0].bytes,rendered.mixes[1].bytes);assert.equal(read('activeRow().label'),'Inspector label');});
 setup();read('beginLaneRename(active);renamingLane.input.value="Pending name";');const close={preventDefault(){this.blocked=true;}};for(const handler of handlers.beforeunload)handler(close);check('closing with an unfinished lane name commits it and protects the unsaved edit',()=>{assert.equal(read('activeRow().label'),'Pending name');assert.equal(close.blocked,true);});
 sandbox.OfflineAudioContext=class {async decodeAudioData(){return {sampleRate:48000,numberOfChannels:6,getChannelData:()=>new Float32Array([.5])};}};await assert.rejects(read('renderExportMixes(planFixture)'),/mono or stereo/);check('multichannel samples are rejected instead of silently losing channels',()=>assert.ok(true));
 console.log(passed+' lane and rendering UI checks passed.');
})().catch(e=>{console.error(e);process.exitCode=1;});
