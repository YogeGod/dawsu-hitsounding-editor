const fs=require("fs"),vm=require("vm"),assert=require("assert/strict");
const root=require("path").resolve(__dirname,"..")+"/",H=require(root+"src/model.js"),handlers={},docHandlers={},elements=new Map(),all=[],timers=new Map();let nextTimer=0;
let barRects=[];const barCtx=new Proxy({},{get:(_,key)=>key==="fillRect"?((...args)=>barRects.push(args)):(()=>{})});
let rulerText=[],bodyLines=[],objectArcs=[],objectPoints=[];const rulerCtx=new Proxy({},{get:(_,k)=>k==="fillText"?((text,x,y)=>rulerText.push({text,x,y})):(()=>{})});const objectCtx=new Proxy({lineWidth:1},{get:(t,k)=>k==="beginPath"?(()=>objectPoints=[]):k==="moveTo"||k==="lineTo"?((x,y)=>objectPoints.push([x,y])):k==="stroke"?(()=>{if(t.lineWidth===7)bodyLines.push(objectPoints);}):k==="arc"?((...args)=>objectArcs.push(args)):k in t?t[k]:(()=>{})});
const ctx=new Proxy({},{get:()=>()=>{}});
let doc;
function el(id="",tagName="DIV"){
 if(elements.has(id))return elements.get(id);
 const node={id,tagName:tagName.toUpperCase(),dataset:{},attributes:{},children:[],classList:{toggle(){},remove(){},add(){}},style:{},value:"",title:"",type:"",clientWidth:1000,clientHeight:300,open:false,checked:false,hidden:id==="hoverHelp",disabled:false,isConnected:true,textContent:"",
 setAttribute(k,v){this.attributes[k]=v;if(k==="title")this.title=v;},getAttribute(k){return k==="title"?this.title:this.attributes[k]??null;},hasAttribute(k){return k==="title"?!!this.title:k in this.attributes;},removeAttribute(k){delete this.attributes[k];if(k==="title")this.title="";},
 contains(other){for(let n=other;n;n=n.parentElement)if(n===this)return true;return false;},
 closest(){for(let n=this;n;n=n.parentElement)if(n.dataset.help||n.title)return n;return null;},
 focus(){doc.activeElement=this;},select(){this.selectedText=true;},
 setPointerCapture(){},getBoundingClientRect(){return {left:0,top:0,bottom:300,width:250,height:80};},remove(){if(this.parentElement)this.parentElement.children=this.parentElement.children.filter(n=>n!==this);this.isConnected=false;},getContext(){return this.id==="bars"?barCtx:this.id==="objects"?objectCtx:this.id==="ruler"?rulerCtx:ctx;},
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



const F=require(root+'src/formats.cjs'),fixture=fs.readFileSync(root+'test/fixtures/sliders-and-meter.osu','utf8');
function setup(){reset();read('scale=.12;view=0;scrollY=0;cursor=0;');}
(async()=>{
 setup();let picked='E:/dawsu!/Songs',calls=0;sandbox.desktop.pickSongsFolder=async()=>{calls++;return picked;};
 const before=read('JSON.stringify(p())'),pref=read('preferencesBody({songsFolder:"F:/dawsu!/Songs"})'),panel=pref.element.children[1];
 check('Preferences General tab displays the current folder without changing the project',()=>{assert.equal(pref.element.children[0].children[0].getAttribute('role'),'tab');assert.equal(pref.value().songsFolder,'F:/dawsu!/Songs');assert.equal(read('JSON.stringify(p())'),before);});
 await panel.children[1].onclick();check('Choose folder updates the preference draft through the desktop picker',()=>{assert.equal(calls,1);assert.equal(pref.value().songsFolder,'E:/dawsu!/Songs');assert.equal(read('JSON.stringify(p())'),before);});
 picked=null;await panel.children[1].onclick();check('cancelling folder selection preserves the draft folder',()=>assert.equal(pref.value().songsFolder,'E:/dawsu!/Songs'));
 panel.children[2].click();check('Clear resets the folder draft',()=>assert.equal(pref.value().songsFolder,''));
 setup();read('history.project=H.setSignatures(p(),[{timeMs:0,numerator:3,denominator:4},{timeMs:1500,numerator:6,denominator:8}]);cursor=2000;update();');
 const editor=read('signatureEditor()');
 check('signature editor displays numerator, denominator and timed changes',()=>assert.deepEqual(JSON.parse(JSON.stringify(editor.value())).map(s=>[s.timeMs,s.numerator,s.denominator]),[[0,3,4],[1500,6,8]]));
 editor.element.children[2].click();check('Add change at playhead inherits the active signature',()=>{const last=editor.value().at(-1);assert.equal(last.timeMs,2000);assert.equal(last.numerator,6);assert.equal(last.denominator,8);});
 check('signature footer follows playback cursor changes during render',()=>{assert.equal(el('timeSignature').textContent,'6/8');read('cursor=0;render();');assert.equal(el('timeSignature').textContent,'3/4');});
 check('ruler advances to bar two after three quarter notes in 3/4',()=>{read('history.project=H.setSignatures(p(),[{timeMs:0,numerator:3,denominator:4}]);');rulerText=[];read('render();');const second=rulerText.find(t=>t.text==='2');assert.ok(second);assert.equal(second.x,185);});
 setup();sandbox.sliderFixture=F.osuRead(fixture).project;read('history=new H.History(sliderFixture);active=p().rows[0].id;update();');
 check('gameplay lane renders slider body spans and circular head/end markers',()=>{bodyLines=[];objectArcs=[];read('renderGameplay()');assert.equal(bodyLines.length,1);assert.ok(objectArcs.length>=3);assert.equal(bodyLines[0][0][0],120);assert.equal(bodyLines[0][1][0],240);});
 check('clicking a slider body selects its linked head, repeat and end sounds',()=>{el('objects').onpointerdown(pointer({clientX:150}));assert.equal(read('selected.size'),5);assert.equal(read('p().events.filter(e=>selected.has(e.id)).every(e=>p().objects.find(o=>o.id===e.ownerId).role.startsWith("slider-"))'),true);});
 check('clicking a slider head still selects only sounds on that edge',()=>{el('objects').onpointerdown(pointer({clientX:120}));assert.equal(read('selected.size'),2);assert.equal(read('p().events.filter(e=>selected.has(e.id)).every(e=>p().objects.find(o=>o.id===e.ownerId).role==="slider-head")'),true);});
 check('slider body stays visible when its head is outside the viewport',()=>{read('view=1300;');bodyLines=[];read('renderGameplay()');assert.equal(bodyLines.length,1);assert.equal(bodyLines[0][0][0],-7);assert.equal(bodyLines[0][1][0],84);});
 setup();sandbox.sliderFixture=F.osuRead(fixture).project;read('history=new H.History(sliderFixture);cursor=1500;');
 const timing=read('timingEditor()'),table=timing.element.children[2],buttons=timing.element.children[3];
 check('timing panel displays red and green offsets BPM SV and all point settings',()=>{assert.equal(table.children.length,4);assert.equal(table.children[1].children[0].value,'red');assert.equal(table.children[2].children[0].value,'green');assert.equal(table.children[2].children[2].value,2);assert.equal(table.children[2].children[3].disabled,true);assert.equal(timing.value().length,3);});
 check('timing controls edit volume and effect bits while retaining other flags',()=>{table.children[2].children[6].value='33';table.children[2].children[7].checked=true;table.children[2].children[8].checked=true;const point=timing.value()[1];assert.equal(point.volume,33);assert.equal(point.effects,9);});
 check('add inherited point uses the exact playhead position',()=>{buttons.children[1].click();assert.equal(timing.value().find(t=>t.timeMs===1500).red,false);});
 check('timing filter hides nonmatching rows',()=>{timing.element.children[1].value='green';timing.element.children[1].oninput();assert.equal(table.children[1].hidden,true);assert.equal(table.children[2].hidden,false);});
 check('Go moves the playhead to the entered timing offset',()=>{table.children[2].children[9].children[0].click();assert.equal(read('cursor'),1000);});
 check('invalid timing values are rejected by the panel',()=>{table.children[1].children[2].value='0';assert.throws(()=>timing.value(),/Timing values/);});
 setup();const midiTiming=read('timingEditor()');check('MIDI projects show tempo rows without inherited add controls',()=>{assert.equal(midiTiming.element.children[3].children.length,1);assert.equal(midiTiming.element.children[2].children[1].children[0].disabled,true);});
 setup();sandbox.phaseFixture=F.osuRead(fixture.replace('3000,400,7','3137,400,7')).project;read('history=new H.History(phaseFixture);view=3000;scale=1;');
 const gridXs=[],lineCtx=new Proxy({moveTo(x,y){gridXs.push(x);}},{get:(t,k)=>t[k]||(()=>{})});sandbox.phaseCtx=lineCtx;read('lines(phaseCtx,600,100)');
 check('canvas grid restarts at an off-phase red timing point',()=>{assert.ok(gridXs.includes(137.5));assert.ok(gridXs.includes(237.5));assert.equal(gridXs.includes(200.5),false);});
 check('manual playhead snapping matches the restarted drawn grid',()=>assert.equal(read('gridCursorTime(3240)'),3237));
 setup();const audioUI=read('audioPreferencesBody({backend:"browser"})'),audioFields=audioUI.element.children;
 check('Audio preferences default to browser and disable native-only controls',()=>{assert.equal(audioUI.value().backend,'browser');assert.equal(audioFields[1].children[0].disabled,true);assert.equal(audioFields[3].children[0].disabled,true);});
 sandbox.desktop.audioDevices=async()=>({devices:[{api:'asio',id:'Driver A',name:'Driver A'},{api:'wasapi',id:'speakers',name:'Speakers'}],errors:[]});await audioUI.refresh();
 audioFields[0].children[0].value='asio';audioFields[0].children[0].onchange();
 check('ASIO lists installed drivers and enables channel selection',()=>{assert.equal(audioFields[1].children[0].children[1].value,'Driver A');assert.equal(audioFields[6].children[0].disabled,false);assert.equal(audioFields[4].children[0].disabled,true);});
 audioFields[1].children[0].value='Driver A';audioFields[6].children[0].value='3';
 check('Audio draft stores ASIO driver and zero-based output pair without chart edits',()=>{const before=read('JSON.stringify(p())'),draft=audioUI.value();assert.equal(draft.deviceId,'Driver A');assert.equal(draft.channelOffset,2);assert.equal(read('JSON.stringify(p())'),before);});
 audioFields[0].children[0].value='wasapi-exclusive';audioFields[0].children[0].onchange();
 check('WASAPI exclusive exposes output devices and buffer controls',()=>{assert.equal(audioFields[1].children[0].children[1].value,'speakers');assert.equal(audioFields[4].children[0].disabled,false);assert.match(audioFields[7].textContent,/Exclusive mode/);});
 setup();sandbox.phaseFixture=F.osuRead(fixture.replace('0,500,3','0,500,4').replace('3000,400,7','3137,400,4')).project;read('history=new H.History(phaseFixture);view=3137;scale=1;');
 const barXs=[],beatXs=[],emphasisCtx={strokeStyle:'',lineWidth:1,beginPath(){},moveTo(x){if(this.strokeStyle==="#73737e")barXs.push(x);else if(this.strokeStyle==="#4d4d57")beatXs.push(x);},lineTo(){},stroke(){}};sandbox.emphasisCtx=emphasisCtx;read('lines(emphasisCtx,600,100)');
 check('same-meter red timing point anchors the emphasized bar line',()=>assert.ok(barXs.includes(.5)));
 check('emphasized beats share the restarted snap grid without phantom old-grid lines',()=>{assert.ok(beatXs.includes(400.5));assert.equal(beatXs.includes(290.5),false);for(const x of [...barXs,...beatXs].filter(x=>x>=0&&x<=600))assert.ok(Math.abs((x-.5)/100-Math.round((x-.5)/100))<1e-7);});
 console.log(passed+' preferences, signatures and slider UI checks passed.');
})().catch(e=>{console.error(e);process.exitCode=1;});
