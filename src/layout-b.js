"use strict";
// Layout controls only; row heights and timeline zoom never edit chart data.
function bSyncZoom(){
 $('zoom').value=100*Math.log(scale/.0001)/Math.log(50000);
 $('zoom').setAttribute('aria-valuetext',(scale*1000).toFixed(1)+' pixels per second');
 $('rowHeight').setAttribute('aria-valuetext',Math.round(rowH)+' pixels per lane');
 $('zoomOut').disabled=scale<=.0001;$('zoomIn').disabled=scale>=5;
 $('rowZoomOut').disabled=rowH<=18;$('rowZoomIn').disabled=rowH>=96;
}
function bSetHorizontalZoom(value){
 if(gesture||rowDrag||locatorDrag||!Number.isFinite(value))return;
 const width=$('grid').clientWidth,playheadX=xAt(cursor),anchor=playheadX>=0&&playheadX<=width?playheadX:width/2;
 const time=timeAt(anchor),next=H.clamp(value,.0001,5);view=Math.max(0,time-anchor/next);scale=next;update();
}
const bOriginalUpdate=update;
update=function(){bOriginalUpdate();bSyncZoom();};
$('zoom').oninput=()=>bSetHorizontalZoom(.0001*Math.pow(50000,Number($('zoom').value)/100));
$('zoomIn').onclick=()=>bSetHorizontalZoom(scale*1.5);
$('zoomOut').onclick=()=>bSetHorizontalZoom(scale/1.5);
$('rowZoomIn').onclick=()=>setRowHeight(rowH+4);
$('rowZoomOut').onclick=()=>setRowHeight(rowH-4);
bSyncZoom();

let bLaneSizes={spectrogram:96,objects:48},bLaneDrag=null;
try{const saved=JSON.parse(localStorage.getItem('hse-b-lane-heights')||'{}');for(const name of ['spectrogram','objects'])if(Number.isFinite(saved[name]))bLaneSizes[name]=H.clamp(saved[name],name==='spectrogram'?40:24,name==='spectrogram'?480:240);}catch{}
function bApplyLaneSizes(prefer){
 const editor=$('editor'),body=$('controllerBody'),fixed=$('laneTop').offsetHeight+$('controllerHead').offsetHeight+(body.hidden?0:body.offsetHeight)+$('scroll').offsetHeight+12+100;
 const budget=Math.max(64,editor.clientHeight-fixed);let wave=H.clamp(bLaneSizes.spectrogram,40,480),objects=H.clamp(bLaneSizes.objects,24,240);
 if(wave+objects>budget){if(prefer==='objects'){objects=Math.max(24,Math.min(objects,budget-40));wave=Math.max(40,Math.min(wave,budget-objects));}else{objects=Math.max(24,Math.min(objects,budget-40));wave=Math.max(40,Math.min(wave,budget-objects));}}
 $('waveRow').style.height=wave+'px';$('objectRow').style.height=objects+'px';
 for(const [name,value,min,max] of [['spectrogram',wave,40,Math.min(480,budget-objects)],['objects',objects,24,Math.min(240,budget-wave)]]){const handle=$(name==='spectrogram'?'resizeSpectrogram':'resizeObjects');handle.setAttribute('aria-valuemin',min);handle.setAttribute('aria-valuemax',Math.max(min,max));handle.setAttribute('aria-valuenow',Math.round(value));handle.setAttribute('aria-valuetext',Math.round(value)+' pixels');}
 return {spectrogram:wave,objects};
}
function bSaveLaneSizes(){try{localStorage.setItem('hse-b-lane-heights',JSON.stringify(bLaneSizes));}catch{}}
function bResizeLane(name,value,save=true){if(!Number.isFinite(value))return;const bounds=name==='spectrogram'?[40,480]:[24,240];const handle=$(name==='spectrogram'?'resizeSpectrogram':'resizeObjects'),limit=Number(handle.getAttribute('aria-valuemax'))||bounds[1];bLaneSizes[name]=H.clamp(value,bounds[0],Math.min(bounds[1],Math.max(bounds[0],limit)));const actual=bApplyLaneSizes(name);bLaneSizes[name]=actual[name];if(save)bSaveLaneSizes();update();}
for(const [id,name] of [['resizeSpectrogram','spectrogram'],['resizeObjects','objects']]){
 const handle=$(id);
 handle.onpointerdown=ev=>{if(ev.button!==0||gesture||rowDrag||locatorDrag)return;ev.preventDefault();ev.stopPropagation();handle.focus({preventScroll:true});handle.setPointerCapture(ev.pointerId);bLaneDrag={name,y:ev.clientY,size:Number(handle.getAttribute('aria-valuenow')),before:{...bLaneSizes}};document.body.classList.add('bResizingLane');};
 handle.onpointermove=ev=>{if(bLaneDrag?.name!==name)return;bResizeLane(name,bLaneDrag.size+ev.clientY-bLaneDrag.y,false);};
 handle.onpointerup=()=>{if(bLaneDrag?.name!==name)return;bLaneDrag=null;document.body.classList.remove('bResizingLane');bSaveLaneSizes();};
 handle.onpointercancel=()=>{if(!bLaneDrag)return;bLaneSizes=bLaneDrag.before;bLaneDrag=null;document.body.classList.remove('bResizingLane');bApplyLaneSizes();update();};
 handle.ondblclick=ev=>{ev.preventDefault();bResizeLane(name,name==='spectrogram'?96:48);};
 handle.onkeydown=ev=>{if(!['ArrowUp','ArrowDown','Home','End'].includes(ev.key)||gesture)return;ev.preventDefault();ev.stopPropagation();const value=Number(handle.getAttribute('aria-valuenow')),step=ev.shiftKey?1:8;bResizeLane(name,ev.key==='Home'?Number(handle.getAttribute('aria-valuemin')):ev.key==='End'?Number(handle.getAttribute('aria-valuemax')):value+(ev.key==='ArrowUp'?-step:step));};
}
new ResizeObserver(()=>{bApplyLaneSizes();}).observe($('editor'));
new ResizeObserver(()=>{bApplyLaneSizes();}).observe($('controllerBody'));
bApplyLaneSizes();update();
