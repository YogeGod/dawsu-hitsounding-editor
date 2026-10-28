const fs=require("fs"),vm=require("vm"),assert=require("assert/strict");
const root=require("path").resolve(__dirname,"..")+"/",H=require(root+"src/model.js"),handlers={},docHandlers={},elements=new Map(),all=[],timers=new Map();let nextTimer=0;
const ctx=new Proxy({},{get:()=>()=>{}});
let doc;
function el(id="",tagName="DIV"){
 if(elements.has(id))return elements.get(id);
 const node={id,tagName:tagName.toUpperCase(),dataset:{},attributes:{},children:[],classList:{toggle(){},remove(){},add(){}},style:{},value:"",title:"",type:"",clientWidth:1000,clientHeight:300,open:false,checked:false,hidden:id==="hoverHelp",disabled:false,isConnected:true,textContent:"",
 setAttribute(k,v){this.attributes[k]=v;if(k==="title")this.title=v;},getAttribute(k){return k==="title"?this.title:this.attributes[k]??null;},hasAttribute(k){return k==="title"?!!this.title:k in this.attributes;},removeAttribute(k){delete this.attributes[k];if(k==="title")this.title="";},
 contains(other){for(let n=other;n;n=n.parentElement)if(n===this)return true;return false;},
 closest(){for(let n=this;n;n=n.parentElement)if(n.dataset.help||n.title)return n;return null;},
 focus(){doc.activeElement=this;},select(){this.selectedText=true;},
 setPointerCapture(){},getBoundingClientRect(){return {left:0,top:0,bottom:300,width:250,height:80};},getContext(){return ctx;},
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

check("Select starts with normal tool state",()=>assert.equal(el("grid").dataset.tool,"select"));
check("Alt selects Draw temporarily without changing chosen Select",()=>{key("keydown","Alt");assert.equal(el("grid").dataset.tool,"draw");assert.equal(read("tool"),"select");});
check("Alt-click inserts a sound as one undo transaction",()=>{const n=read("p().events.length");el("grid").onpointerdown(pointer({altKey:true}));assert.equal(read("gesture.type"),"paint");el("grid").onpointerup();assert.equal(read("p().events.length"),n+1);assert.equal(read("history.undoStack.length"),1);});
check("Releasing Alt restores Select immediately",()=>{key("keyup","Alt");assert.equal(el("grid").dataset.tool,"select");});
check("Alt release during a stroke preserves its Draw mode until mouse-up",()=>{key("keydown","Alt");el("grid").onpointerdown(pointer({altKey:true,clientX:30}));key("keyup","Alt");assert.equal(el("grid").dataset.tool,"draw");el("grid").onpointermove(pointer({clientX:45}));assert.equal(read("gesture.mode"),"draw");el("grid").onpointerup();assert.equal(el("grid").dataset.tool,"select");});
check("Alt cannot turn an in-progress Select drag into painting",()=>{el("grid").onpointerdown(pointer({clientX:0}));assert.equal(read("gesture.type"),"move");key("keydown","Alt");assert.equal(el("grid").dataset.tool,"select");el("grid").onpointermove(pointer({altKey:true,clientX:60}));assert.equal(read("gesture.type"),"move");el("grid").onpointerup();assert.equal(el("grid").dataset.tool,"draw");key("keyup","Alt");});
check("Alt release restores the previous non-Select tool",()=>{read('setTool("erase")');key("keydown","Alt");assert.equal(el("grid").dataset.tool,"draw");key("keyup","Alt");assert.equal(el("grid").dataset.tool,"erase");});
check("Ctrl+Alt and AltGr do not engage Draw",()=>{key("keydown","Alt",{altKey:true,ctrlKey:true});assert.equal(el("grid").dataset.tool,"erase");key("keydown","Alt",{altKey:true,getModifierState:k=>k==="AltGraph"});assert.equal(el("grid").dataset.tool,"erase");});
check("Focus loss clears Alt and rolls back an unfinished stroke",()=>{read('setTool("select")');const n=read("p().events.length"),h=read("history.undoStack.length");key("keydown","Alt");el("grid").onpointerdown(pointer({altKey:true,clientX:105}));assert.ok(read("gesture"));for(const f of handlers.blur)f();assert.equal(read("gesture"),null);assert.equal(read("p().events.length"),n);assert.equal(read("history.undoStack.length"),h);assert.equal(el("grid").dataset.tool,"select");});
check("Explicit Draw keeps its pen state after Alt release",()=>{read('setTool("draw")');key("keydown","Alt");key("keyup","Alt");assert.equal(el("grid").dataset.tool,"draw");});
console.log(passed+" interaction checks passed.");



reset();
check("every main control and drawing tool has descriptive hover help",()=>{const help=read("HSEHelp");for(const [id,parts] of Object.entries(help.labels)){assert.ok(el(id).dataset.help.includes(parts[0]),id);assert.ok(el(id).attributes["aria-label"],id);}for(const tool of Object.keys(help.tools))assert.ok(el("tool-"+tool).dataset.help);assert.equal(el("edit-copy").attributes["aria-keyshortcuts"],"Control+c");});
check("hover shows the command name and shortcut",()=>{dispatch("pointerover",{target:el("edit-copy"),clientX:100,clientY:60});flush();assert.equal(el("hoverHelp").hidden,false);assert.match(el("hoverHelp").textContent,/Copy selected events/);assert.match(el("hoverHelp").textContent,/Ctrl\+C/);});
check("hover labels disappear before editing and do not show during a drag",()=>{dispatch("pointerdown",{target:el("grid")});assert.equal(el("hoverHelp").hidden,true);read('gesture={type:"paint"}');dispatch("pointerover",{target:el("grid"),clientX:15,clientY:15});flush();assert.equal(el("hoverHelp").hidden,true);read("gesture=null");});
check("sound hover describes the actual volume and MIDI velocity",()=>{dispatch("pointerover",{target:el("grid"),clientX:0,clientY:15});flush();assert.match(el("hoverHelp").textContent,/Volume: 85%/);assert.match(el("hoverHelp").textContent,/MIDI velocity: 108/);});
check("Ctrl+C copies selected events without modifying the project",()=>{read("selected=new Set(p().events.slice(0,3).map(e=>e.id));update()");const before=read("JSON.stringify(p())");const ev=key("keydown","c",{ctrlKey:true});assert.equal(ev.defaultPrevented,true);assert.equal(read("clipboard.events.length"),3);assert.equal(read("JSON.stringify(p())"),before);assert.equal(read("history.undoStack.length"),0);assert.equal(el("edit-paste").disabled,false);});
check("Ctrl+V pastes at cursor with original spacing, values and independent identities",()=>{read("cursor=2500");const saved=read("JSON.stringify(clipboard)"),before=read("p().events.length");key("keydown","v",{ctrlKey:true});assert.equal(read("p().events.length"),before+3);assert.equal(read("p().events.filter(e=>selected.has(e.id)).map(e=>e.timeMs).join(',')"),"2500,3000,3500");assert.equal(read("p().events.filter(e=>selected.has(e.id)).every(e=>e.volume===85&&e.performance.velocity===108)"),true);assert.equal(read("p().events.filter(e=>selected.has(e.id)).some(e=>clipboard.events.some(old=>old.id===e.id))"),false);assert.equal(read("JSON.stringify(clipboard)"),saved);assert.equal(read("history.undoStack.length"),1);});
check("Ctrl+Z and Ctrl+Shift+Z undo and redo one paste",()=>{const before=read("p().events.length");key("keydown","z",{ctrlKey:true});assert.equal(read("p().events.length"),before-3);key("keydown","Z",{ctrlKey:true,shiftKey:true});assert.equal(read("p().events.length"),before);key("keydown","z",{ctrlKey:true});key("keydown","y",{ctrlKey:true});assert.equal(read("p().events.length"),before);});
check("Ctrl+X cuts a selection and one undo restores it",()=>{read("selected=new Set(p().events.slice(0,2).map(e=>e.id));update()");const before=read("JSON.stringify(p())");key("keydown","x",{ctrlKey:true});assert.equal(read("clipboard.events.length"),2);assert.equal(read("selected.size"),0);key("keydown","z",{ctrlKey:true});assert.equal(read("JSON.stringify(p())"),before);});
check("text input retains native copy and cut shortcuts",()=>{el("filter").value="sample";doc.activeElement=el("filter");const before=read("JSON.stringify(p())"),clip=read("JSON.stringify(clipboard)");assert.equal(key("keydown","x",{ctrlKey:true}).defaultPrevented,false);assert.equal(key("keydown","c",{ctrlKey:true}).defaultPrevented,false);assert.equal(read("JSON.stringify(p())"),before);assert.equal(read("JSON.stringify(clipboard)"),clip);el("filter").value="";});
check("clicking timeline releases text focus and re-enables event shortcuts",()=>{el("grid").onpointerdown(pointer({clientX:151,clientY:15}));el("grid").onpointerup();assert.equal(doc.activeElement,el("grid"));key("keydown","a",{ctrlKey:true});assert.equal(read("selected.size"),read("p().events.length"));});
check("Ctrl+Shift+A clears selection and disables selection-only menu commands",()=>{key("keydown","A",{ctrlKey:true,shiftKey:true});assert.equal(read("selected.size"),0);assert.equal(el("edit-cut").disabled,true);assert.equal(el("edit-copy").disabled,true);});
check("empty copy and cut retain the existing event clipboard",()=>{const before=read("JSON.stringify(clipboard)");key("keydown","c",{ctrlKey:true});key("keydown","x",{ctrlKey:true});assert.equal(read("JSON.stringify(clipboard)"),before);});
check("Edit menu Copy and Paste use the same actions as shortcuts",()=>{read("selected=new Set(p().events.slice(0,2).map(e=>e.id));cursor=4500;update()");el("edit-copy").click();assert.equal(read("clipboard.events.length"),2);const n=read("p().events.length");el("edit-paste").click();assert.equal(read("p().events.length"),n+2);assert.equal(read("Math.min(...p().events.filter(e=>selected.has(e.id)).map(e=>e.timeMs))"),4500);});
check("repeated Ctrl+D duplicates the newest copy without replacing the clipboard",()=>{read("selected=new Set(p().events.slice(0,2).map(e=>e.id));update()");const clip=read("JSON.stringify(clipboard)");key("keydown","d",{ctrlKey:true});const first=read("Math.min(...p().events.filter(e=>selected.has(e.id)).map(e=>e.timeMs))");key("keydown","d",{ctrlKey:true});assert.ok(read("Math.min(...p().events.filter(e=>selected.has(e.id)).map(e=>e.timeMs))")>first);assert.equal(read("JSON.stringify(clipboard)"),clip);});
check("Delete removes selected sounds without modifying gameplay objects",()=>{const before=read("JSON.stringify(p().objects)"),n=read("p().events.length"),count=read("selected.size");key("keydown","Delete");assert.equal(read("p().events.length"),n-count);assert.equal(read("JSON.stringify(p().objects)"),before);});
check("Ctrl+F focuses and selects the sample filter",()=>{key("keydown","f",{ctrlKey:true});assert.equal(doc.activeElement,el("filter"));assert.equal(el("filter").selectedText,true);read("focusEditor()");});
check("new shortcuts route to their existing actions",()=>{const calls={};for(const id of ["repeat","help","newProject","save","open","quantize","loop"])el(id).onclick=()=>calls[id]=(calls[id]||0)+1;read("selected=new Set([p().events[0].id]);update()");for(const [k,extra] of [["r",{ctrlKey:true}],["F1",{}],["n",{ctrlKey:true}],["s",{ctrlKey:true}],["o",{ctrlKey:true}],["q",{}],["l",{}]])key("keydown",k,extra);for(const id of ["repeat","help","newProject","save","open","quantize","loop"])assert.equal(calls[id],1,id);});
console.log(passed+" total interaction checks passed.");




(async()=>{
 reset();
 const sources=[],gains=[],ticks=new Map();let interval=0;
 const audio={currentTime:20,destination:{},resume(){},
  createGain(){const node={gain:{value:1,cancelScheduledValues(){},setTargetAtTime(v,t,s){this.value=v;this.lastTarget={v,t,s};}},connect(target){this.target=target;},disconnect(){}};gains.push(node);return node;},
  createBufferSource(){const node={buffer:null,playbackRate:{value:1},connect(target){this.target=target;},disconnect(){this.disconnected=true;},start(when,offset=0,duration){this.started={when,offset,duration};},stop(){this.stopped=true;}};sources.push(node);return node;}
 };
 sandbox.AudioContext=function(){return audio;};sandbox.setInterval=fn=>{ticks.set(++interval,fn);return interval;};sandbox.clearInterval=id=>ticks.delete(id);
 read('p().audio={assetKey:"song",name:"song.wav",durationMs:8000,volume:60,offsetMs:500};p().rows[0].assetKey="kick";buffers.set("song",{duration:8});buffers.set("kick",{duration:.2});update();');
 read('for(const row of p().rows){const key=H.sampleAsset(row);if(key&&!buffers.has(key))buffers.set(key,{duration:.2});}');
 const initialEvents=read("JSON.stringify(p().events)"),initialObjects=read("JSON.stringify(p().objects)");
 await read("play()");
 const song=read("songVoice"),gain=read("songGain"),sample=sources.find(s=>s!==song);
 const approx=(a,b)=>assert.ok(Math.abs(a-b)<1e-8,a+" ≠ "+b);
 check("backing routes through its own gain and honors positive offset",()=>{assert.equal(song.target,gain);approx(gain.gain.value,.6);approx(song.started.when,20.55);approx(song.started.offset,0);});
 check("volume slider previews immediately without changing sample gain or adding undo entries",()=>{for(const v of [25,0,30]){el("songVolume").value=String(v);el("songVolume").oninput();approx(gain.gain.value,v/100);}approx(sample.target.gain.value,.85);assert.equal(read("history.undoStack.length"),0);assert.equal(el("songVolumeValue").textContent,"30%");});
 await el("songVolume").onchange();
 check("one slider drag saves one undoable song-volume edit",()=>{assert.equal(read("p().audio.volume"),30);assert.equal(read("history.undoStack.length"),1);el("undo").click();assert.equal(read("p().audio.volume"),60);approx(gain.gain.value,.6);});
 audio.currentTime=20.25;el("songOffset").value="1200";await el("songOffset").onchange();
 check("changing offset while playing replaces only the backing source at the correct time",()=>{assert.equal(song.stopped,true);assert.equal(sample.stopped,undefined);const replacement=read("songVoice");approx(replacement.started.when,21.25);approx(replacement.started.offset,0);assert.equal(read("playing"),true);});
 check("undoing offset resynchronizes the playing backing",()=>{el("undo").click();assert.equal(read("p().audio.offsetMs"),500);approx(read("songVoice").started.when,20.55);});
 el("songOffset").value="-250";await el("songOffset").onchange();
 check("negative live offset seeks forward inside the source audio",()=>{approx(read("songVoice").started.when,20.25);approx(read("songVoice").started.offset,.45);});
 check("song edits preserve every event and gameplay object",()=>{assert.equal(read("JSON.stringify(p().events)"),initialEvents);assert.equal(read("JSON.stringify(p().objects)"),initialObjects);});
 check("keyboard arrows on the song slider do not nudge selected sounds",()=>{doc.activeElement=el("songVolume");read("selected=new Set([p().events[0].id])");const ev=key("keydown","ArrowLeft");assert.equal(ev.defaultPrevented,false);assert.equal(read("JSON.stringify(p().events)"),initialEvents);read("focusEditor()");});
 el("songOffset").value="2000";await el("songOffset").onchange();
 check("extending offset during playback also extends the playback end",()=>{audio.currentTime=read("playingOrigin")+8.5;for(const fn of [...ticks.values()])fn();assert.equal(read("playing"),true);audio.currentTime=read("playingOrigin")+10.001;for(const fn of [...ticks.values()])fn();assert.equal(read("playing"),false);});

 check("sample row grip drag changes only presentation order in one undo step",()=>{
  read('stop();history=new H.History(H.demo());selected=new Set();active=p().rows[0].id;update();');
  const before=read('JSON.stringify(p().events)'),ids=read('p().rows.map(r=>r.id)');
  const holder=el("rows").children[0],handle=holder.children[0].children[0];
  handle.onpointerdown({...pointer(),stopPropagation(){}});el("rows").onpointermove(pointer({clientY:90}));el("rows").onpointerup();
  assert.equal(read('p().rows[2].id'),ids[0]);assert.equal(read('JSON.stringify(p().events)'),before);assert.equal(read('history.undoStack.length'),1);read('history.undo();update();');assert.equal(read('p().rows[0].id'),ids[0]);
 });
 check("locator ruler drag creates a separate undoable cycle region",()=>{
  read('range=null;scale=.12;view=0;history.project=H.setCycle(p(),{leftMs:0,rightMs:500});');el("ruler").onpointerdown(pointer({clientX:90,clientY:5}));el("ruler").onpointermove(pointer({clientX:300,clientY:5}));el("ruler").onpointerup();
  assert.equal(read('H.cycleSettings(p()).leftMs'),750);assert.equal(read('H.cycleSettings(p()).rightMs'),2500);assert.equal(read('range'),null);
 });
 read('stop();history=new H.History(H.demo());p().audio={assetKey:"song",durationMs:8000};p().rows[0].assetKey="kick";p().events=p().events.filter(e=>e.rowId===p().rows[0].id&&[1000,1500,2000].includes(e.timeMs));history.project=H.setCycle(p(),{leftMs:1000,rightMs:2000,enabled:true});cursor=1500;looping=true;update();');
 el('startMode').value='cursor';sources.length=0;await read('play()');const clock=read('transport.clock');
 const advance=t=>{audio.currentTime=clock+t;for(const fn of [...ticks.values()])fn();};
 advance(.4);
 check("cycle schedules backing and boundary sounds on the same clock without a restart delay",()=>{
  const songs=sources.filter(s=>s.target===read('songGain'));assert.equal(songs.length,2);approx(songs[0].started.when,clock);approx(songs[0].started.offset,1.5);approx(songs[1].started.when,clock+.5);approx(songs[1].started.offset,1);approx(songs[0].started.duration,.5);approx(songs[1].started.duration,1);
  const hits=sources.filter(s=>s.target!==read('songGain'));assert.equal(hits.length,2);approx(hits[0].started.when,clock);approx(hits[1].started.when,clock+.5);
 });
 advance(.51);check("cursor wraps while playback stays active",()=>{assert.equal(read('playing'),true);approx(read('cursor'),1010);});
 advance(.9);advance(1.4);advance(1.51);
 check("later laps retain exact sample-clock timing",()=>{const songs=sources.filter(s=>s.target===read('songGain'));approx(songs[2].started.when,clock+1.5);approx(read('cursor'),1010);});
 check("toggling cycle off rebases the transport at the current wrapped position",()=>{read('commit("Disable cycle",H.setCycle(p(),{enabled:false}))');assert.equal(read('transport.cycle.enabled'),false);approx(read('transport.start'),1010);assert.equal(read('playing'),true);assert.equal(read('transport.returnMs'),1500);});
 read('stop();history.project=H.setCycle(p(),{leftMs:3000,rightMs:1000,enabled:true});cursor=500;update();');sources.length=0;await read('play()');const skipClock=read('transport.clock');audio.currentTime=skipClock+.4;for(const fn of [...ticks.values()])fn();
 check("reversed locators schedule an exact skip in song audio",()=>{const songs=sources.filter(s=>s.target===read('songGain'));approx(songs[0].started.offset,.5);approx(songs[1].started.when,skipClock+.5);approx(songs[1].started.offset,3);});
 read('stop()');
 check("all audio routes through global gain",()=>{assert.equal(read('songGain').target,read('masterGain'));assert.equal(read('masterGain').target,audio.destination);});
 elements.get('masterVolume').value='25';elements.get('masterVolume').oninput();
 check("global slider updates independently of chart and song volume",()=>{assert.equal(read('masterVolume'),25);approx(read('masterGain.gain.value'),.25);assert.equal(elements.get('masterVolumeValue').textContent,'25%');});
 elements.get('masterVolume').value='0';elements.get('masterVolume').oninput();
 check("global volume can mute",()=>{assert.equal(read('masterVolume'),0);approx(read('masterGain.gain.value'),0);});
 read('history.project.rows[0].assetKey=null;history.project.rows[0].name="soft-hitclap2.wav";buffers.set("osu-default:soft-hitclap.wav",{duration:1});');
 const saved=read('JSON.stringify(p())');sources.length=0;await read('audition(p().rows[0].id,63)');
 check("default skin previews use a real buffer and global gain",()=>{assert.equal(sources.length,1);assert.equal(sources[0].target.target,read('masterGain'));assert.equal(read('JSON.stringify(p())'),saved);});
 check("explicit missing filenames do not resolve to default audio",()=>assert.equal(read('rowSampleAsset(p().rows[0],{source:{explicitFilename:true}})'),null));
 read('history.project.events=[{id:"fallback-test",rowId:p().rows[0].id,timeMs:0,durationMs:125,volume:47}];cursor=0;history.project=H.setCycle(p(),{enabled:false});');sources.length=0;await read('play()');
 check("scheduled default hits retain volume and use the shared output",()=>{const hits=sources.filter(s=>s.target!==read('songGain'));assert.equal(hits.length,1);assert.equal(hits[0].buffer,read('buffers.get("osu-default:soft-hitclap.wav")'));approx(hits[0].target.gain.value,.47);assert.equal(hits[0].target.target,read('masterGain'));});read('stop()');
 const safeProject=read('JSON.stringify(p())');sandbox.HSEAudioBridge={AudioBridge:{}};sandbox.desktop.audioConfigure=async()=>{throw Error('device disconnected');};sandbox.desktop.audioClose=async()=>true;read('outputSettings={...defaultAudioSettings,backend:"asio",deviceId:"missing"};');
 await assert.rejects(read('ensureAudioOutput()'),/Browser output is active/);
 check("native device failures recover to browser output",()=>assert.equal(read('outputSettings.backend'),'browser'));
 check("device recovery preserves chart timing volumes and selection data",()=>assert.equal(read('JSON.stringify(p())'),safeProject));
 console.log(passed+" total interaction and audio-engine checks passed.");
})().catch(error=>{console.error(error);process.exitCode=1;});
