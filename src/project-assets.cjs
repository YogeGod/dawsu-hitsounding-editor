"use strict";
const H=require('./model');
// Imported asset IDs are local to the file, not global names in this editor process.
function unpack(value){if(!value||value.format!=='hitsound-editor')throw Error('Unsupported project file.');const project=H.clone(H.validate(value.project)),source=value.assets||{},ids=new Map(),assets=new Map();function key(old){if(!old)return old;if(!ids.has(old))ids.set(old,H.id('asset'));return ids.get(old);}
 for(const [old,asset] of Object.entries(source)){if(!asset||typeof asset.name!=='string'||typeof asset.data!=='string'||asset.data.length>280000000)throw Error('Invalid embedded audio.');assets.set(key(old),{name:asset.name,data:asset.data});}
 for(const row of project.rows)if(row.assetKey)row.assetKey=key(row.assetKey);
 if(project.audio?.assetKey)project.audio.assetKey=key(project.audio.assetKey);
 for(const map of project.beatmaps){if(map.archiveAssetKey)map.archiveAssetKey=key(map.archiveAssetKey);for(const row of map.originalRows||[])if(row.assetKey)row.assetKey=key(row.assetKey);}
 return {project,assets};
}
module.exports={unpack};
