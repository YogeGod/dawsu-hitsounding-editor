(function(root){
"use strict";
const labels={
 songMute:["Mute song audio","Silence the backing track without changing its volume or exported audio. Soloing a sample lane also silences the song; clearing solo restores it unless this mute is enabled."],
 toggleInspector:["Inspector sidebar","Collapse the inspector to a narrow rail; click again to restore it."],
 toggleLanes:["Sample lane names","Collapse sample names to a compact row strip. Row selection and reordering remain available; Ctrl+F reopens the filter."],
 preferences:["Preferences","Choose your dawsu! Songs folder and configure Audio output, devices, sample rate, buffering and ASIO driver settings."],
 timeSignature:["Time signatures","Edit beats per bar, beat unit and changes at specific times. Bar lines and MIDI export use this map."],
 toggleController:["Show or hide controller lane","Collapse the lower volume/velocity panel to give the sample grid more space. Click again to restore it; selection and values stay unchanged."],
 rowHeight:["Vertical zoom","Adjust sample row height from 18 to 96 pixels. Ctrl+Shift+wheel zooms vertically around the pointer without moving sounds.","Ctrl+Shift+mouse wheel"],
 rowHeightValue:["Sample row height","Row labels and event positions scale together. Diamond sizes follow horizontal and vertical zoom."],
 loopLeft:["Left cycle locator","Enter the left flag position in milliseconds. Ctrl-click the ruler or Ctrl+Num 1 sets it from the cursor."],
 loopRight:["Right cycle locator","Enter the right flag position. A right flag before the left flag skips that section during playback."],
 loopSelection:["Locators to selection","Use the editing range, or encompass selected events including their lengths.","P"],
 swapLoop:["Exchange locators","Switch between repeating and skipping the marked section."],
 startMode:["Playback start mode","Start at the cursor, left locator, selection, or selection with a locator fallback."],
 returnStart:["Return on stop","Space, Play and Stop return to the exact position where playback began."],
 rowUp:["Move row up","Reorder the active sample row without changing its events or MIDI mapping."],
 rowDown:["Move row down","Reorder the active sample row. You can also drag its grip."],
 open:["Open","Import a project, MIDI file or osu beatmap.","Ctrl+O"],
 save:["Save project","Save your work with its loaded audio and samples.","Ctrl+S"],
 exportMidi:["Export MIDI","Write a MIDI copy. Output volume stays separate from MIDI velocity."],
 exportOsu:["Export beatmap","Export a beatmap copy. Choose Normal beatmap to preserve gameplay objects, or HS diff (rhythm guide) to create a separate difficulty from hitsound events with a circle-count preview. Circle layers that need independent volumes are mixed into generated WAVs; identical mixes share a file. Unsupported slider-edge changes show a conflict."],
 editMenu:["Edit","Selection, clipboard and undo commands with their keyboard shortcuts."],
 undo:["Undo","Undo the last complete edit or drag.","Ctrl+Z"],
 redo:["Redo","Restore the last undone edit.","Ctrl+Y / Ctrl+Shift+Z"],
 help:["Help and shortcuts","Show every keyboard shortcut and mouse modifier, with search and an editor guide.","F1"],
 snap:["Toggle snapping","Use the selected snap target when placing or moving sounds.","J"],
 snapMode:["Snap target","Grid: align to beat divisions. Grid relative: retain the original offset. Objects: nearest gameplay object. Cursor: playback position."],
 divisor:["Beat snap","Scroll over this selector, or hold Alt and scroll anywhere in the editor. Up chooses finer divisions; down chooses coarser divisions.","Alt+mouse wheel"],
 quantize:["Snap selected events","Apply snapping to existing events with adjustable strength.","Q"],
 repeat:["Repeat selected events","Choose how many copies to make and their spacing.","Ctrl+R"],
 bulk:["Select and transform","Find events by volume, then select, scale, set volume or mute them."],
 colour:["Event colours","Display output volume, MIDI velocity or object-assignment status."],
 fit:["Fit timeline","Zoom out to see the complete project."],
 zoomOut:["Zoom out","Show more time in the timeline.","Ctrl+mouse wheel"],
 zoomIn:["Zoom in","Show finer timing detail.","Ctrl+mouse wheel"],
 zoom:["Horizontal zoom","Adjust timeline detail. Ctrl+mouse wheel zooms around the pointer."],
 selectionCount:["Selection","Selected events are highlighted in the timeline and controller. The active sample row remains fully visible."],
 eventTime:["Start time","Move selected events by a shared offset, in milliseconds. Gameplay objects stay in place."],
 eventLength:["Event length","Set the selected event lengths in milliseconds. Sample playback remains one-shot."],
 eventVolume:["Output volume","Edit selected sounds from 1–100%. Relative preserves differences; Set all gives them the same value."],
 eventVelocity:["MIDI velocity","Edit selected MIDI velocities from 1–127, independently of output volume."],
 valueMode:["Value editing mode","Relative: change the selection by a shared amount. Set all: apply the entered value to every selected event."],
 owner:["Gameplay-object assignment","Shows whether the selected sound is linked to an existing gameplay object."],
 assign:["Assign to gameplay objects","Match selected sounds to nearby objects without moving gameplay geometry."],
 rowName:["Lane display name","Rename this lane visually. Its sample filename and export mapping stay unchanged. Double-click a lane name to edit it inline."],
 rowNote:["MIDI note number","Change the active row's MIDI note mapping."],
 rowChannel:["MIDI channel","Choose channel 1–16 for the active row's MIDI notes."],
 replaceSample:["Choose sample","Load an audio file for the active row."],
 useSample:["Use this sample for selection","Change the sample row of selected sounds while preserving their time, volume and MIDI identity."],
 insertVolume:["New-event volume","Default output volume for newly drawn sounds; existing sounds are unchanged."],
 insertLength:["New-event length","Default MIDI length in milliseconds for newly drawn sounds."],
 showLength:["Show event lengths","Show lengths instead of diamonds. Drag a right edge to resize."],
 demoTones:["Preview tones","Use a generated tone when a row has no loaded sample. This only affects preview."],
 addLane:["Add lane","Create an empty lane using the active sample. Choose a different sample in the inspector if needed."],
 deleteLane:["Delete lane","Remove this lane and its hitsound events. Gameplay objects and sample files are kept; Undo restores the lane."],
 addSamples:["Add sample rows","Choose audio files to add as new sample rows."],
 loadAudio:["Load song audio","Choose the backing audio and display its waveform."],
 newProject:["New empty project","Start a new project. Save your current work first.","Ctrl+N"],
 songVolume:["Song volume","Adjust only the backing song from 0–100%. Zero mutes the song; sample volumes are unchanged. Changes can be heard during playback."],
 songVolumeValue:["Song volume","The current backing-song volume percentage."],
 songOffset:["Song offset in milliseconds","Positive values play the song later; negative values start it earlier. Press Enter or click away to apply. The waveform follows this offset; sounds and gameplay objects stay in place."],
 songPlaybackHint:["Song playback settings","Volume and offset are saved in the editor project. MIDI and beatmap exports keep the original audio file and chart timing."],
 audioState:["Song audio","The currently loaded backing track."],
 assetState:["Sample status","Indicates whether the active row has a loaded audio sample."],
 rangeState:["Selected time range","Use the Range tool to drag a time range; Shift limits it to one row.","2"],
 rangeAction:["Edit selected range","Copy, move or delete event starts inside the range. The range end is excluded."],
 history:["Recent edits","Each complete gesture is one undo step.","Ctrl+Z / Ctrl+Y"],
 filter:["Filter sample rows","Find sample rows by name.","Ctrl+F"],
 ruler:["Time ruler","Click to move the playback cursor. The upper labels show bars and beats; the lower labels show seconds."],
 wave:["Song waveform","Audio amplitude over time. Click to seek; Ctrl+wheel zooms. The waveform follows the song audio offset."],
 objects:["Gameplay objects","Circles are tap objects. Sliders have a connected body, H head, repeat chevrons and E end. Click an edge for its sounds, or the body for all linked slider-edge sounds."],
 grid:["Sample timeline","Select or move sounds. Ctrl-click toggles selection; Shift-click adds. Hold Alt to draw.","1 Select / Alt Draw"],
 rows:["Sample lanes","Click a row to make it active. Ctrl-click selects the row's sounds; Shift-click adds them. Double-click auditions the sample."],
 vertical:["Scroll sample rows","Move through sample rows. Wheel scrolls; Ctrl+Shift+wheel adjusts row height."],
 controller:["Controller property","Switch the lower panel between output volume and MIDI velocity."],
 rampShape:["Ramp shape","Choose the shape used by the Ramp tool or Shift-drag in the controller."],
 controllerScope:["Controller targets","The full active sample row is always shown. Group edits affect only selected sounds in this row."],
 bars:["Volume and velocity controller","Click a bar to edit it. Drag a selected bar to edit the selected sounds in this row. Alt-drag paints across the row; Shift-drag creates a ramp."],
 left:["Scroll timeline left","Move the timeline view earlier.","Shift+mouse wheel"],
 right:["Scroll timeline right","Move the timeline view later.","Shift+mouse wheel"],
 horizontal:["Scroll timeline","Move through time without changing sound positions."],
 toStart:["Return to start","Move the playback cursor to the beginning.","Home"],
 stop:["Stop","Stop at the current position, or return to the playback start when Return on stop is checked."],
 play:["Play or pause","Preview the song and sample rows.","Space"],
 loop:["Cycle playback","Repeat between L/R flags. Reverse the flags to skip the section. Independent of your editing selection.","Num / or L"],
 clock:["Playback position","Current playback time in minutes, seconds and milliseconds."],
 tempo:["Tempo","Edit a single-tempo project. Imported multi-point tempo maps are preserved."],
 follow:["Follow playback","Scroll the timeline to keep the playback cursor visible."],
 status:["Editor status","Shows the latest operation, playback state or problem."],
 screenshot:["Save editor image","Save a PNG of the current editor view."]
};
const tools={
 select:["Select and move","Select sounds or drag a selection rectangle. Drag selected events to move them. Double-click empty space to insert a sound; right-click a sound to delete it. Ctrl+Up/Down moves selected sounds between rows; Ctrl+Left/Right moves them to adjacent grid lines.","1"],
 range:["Range selection","Drag a time range. Hold Shift to restrict the range to one row.","2"],
 draw:["Draw","Start in empty space to place sounds; drag to repeat with Snap on. Start on a sound to delete it; drag to erase more in that row. The stroke keeps its insert/erase mode until release. Snap off places one sound per stroke.","3 / hold Alt"],
 erase:["Erase","Remove sounds crossed by the stroke.","4"],
 mute:["Mute sounds","Toggle event mute. This is separate from inherited volume.","5"],
 ramp:["Ramp","Drag in the lower controller to draw a value ramp.","6 / Shift-drag controller"],
 audition:["Audition","Click a row's sound to preview its sample.","7"]
};
function install(options={}){
 const tip=document.getElementById("hoverHelp");let target=null,timer=null,point={x:0,y:0},shownText="";
 function annotate(el,parts){if(!el)return;el.dataset.help=parts.join("\n");el.setAttribute("aria-label",parts[0]);el.setAttribute("aria-description",parts.slice(1).join(" "));if(parts[2]){const shortcuts=parts[2].split(/\s*\/\s*/).filter(s=>/^(?:(?:Ctrl|Shift|Alt)\+)*(?:[a-z0-9]|F\d{1,2}|Space|Home|Delete|Escape|Alt)$/i.test(s)).map(s=>s.replace(/Ctrl\+/g,"Control+").replace(/\+([A-Z])$/,(_,c)=>"+"+c.toLowerCase())).join(" ");if(shortcuts)el.setAttribute("aria-keyshortcuts",shortcuts);}el.removeAttribute("title");if(el.parentElement?.tagName==="LABEL")el.parentElement.dataset.help=parts.join("\n");}
 for(const [id,parts] of Object.entries(labels))annotate(document.getElementById(id),parts);
 for(const el of document.querySelectorAll("button[data-tool]"))annotate(el,tools[el.dataset.tool]);
 for(const el of document.querySelectorAll("[data-command]")){const shortcut=el.querySelector("kbd")?.textContent||"";const descriptions={undo:"Undo the last completed edit.",redo:"Restore the last undone edit.",cut:"Copy selected sounds, then remove them. Undo restores the cut sounds.",copy:"Copy selected sounds without changing them.",paste:"Place copied sounds at the playback cursor, preserving their spacing and values.",duplicate:"Make another copy of the selection without changing the clipboard.",repeat:"Choose the number of copies and their spacing.",delete:"Remove selected sounds. Gameplay objects stay in place.",selectAll:"Select every sound in the project.",deselect:"Clear the event selection and time range."};annotate(el,[el.dataset.label||el.textContent.trim(),descriptions[el.dataset.command],shortcut]);}
 function hide(){clearTimeout(timer);timer=null;tip.hidden=true;target=null;shownText="";}
 function place(){const box=tip.getBoundingClientRect(),w=window.innerWidth,h=window.innerHeight;tip.style.left=Math.max(8,Math.min(point.x+15,w-box.width-8))+"px";tip.style.top=Math.max(8,point.y+20+box.height>h?point.y-box.height-12:point.y+20)+"px";}
 function content(el,ev){return options.describe?.(el,ev)||el.dataset.help||el.getAttribute("title")||"";}
 function show(el,ev){
  if(options.isBusy?.()||!el)return hide();
  const next=content(el,ev);if(!next)return hide();
  if(target===el&&shownText===next){if(!tip.hidden)place();return;}
  hide();target=el;shownText=next;
  if(el.hasAttribute("title")){el.dataset.help=el.getAttribute("title");el.removeAttribute("title");}
  timer=setTimeout(()=>{if(options.isBusy?.()||!el.isConnected)return hide();tip.textContent=next;tip.hidden=false;place();},300);
 }
 document.addEventListener("pointerover",ev=>{point={x:ev.clientX,y:ev.clientY};show(ev.target.closest("[data-help],[title]"),ev);});
 document.addEventListener("pointermove",ev=>{point={x:ev.clientX,y:ev.clientY};show(ev.target.closest("[data-help],[title]"),ev);});
 document.addEventListener("pointerout",ev=>{if(target&&(!ev.relatedTarget?.nodeType||!target.contains(ev.relatedTarget)))hide();});
 document.addEventListener("pointerdown",hide,true);
 document.addEventListener("keydown",hide,true);
 document.addEventListener("focusin",ev=>{const el=ev.target.closest("[data-help]");if(!el||el.tagName==="CANVAS")return;const box=el.getBoundingClientRect();point={x:box.left,y:box.bottom};show(el,ev);});
 document.addEventListener("focusout",hide);
 document.addEventListener("scroll",hide,true);
 window.addEventListener("blur",hide);
 return {hide};
}
const api={labels,tools,install};if(typeof module!=="undefined")module.exports=api;else root.HSEHelp=api;
})(globalThis);
