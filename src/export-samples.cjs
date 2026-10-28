"use strict";
const fs=require('fs'),path=require('path');
function bundled(key,folder){if(typeof key!=='string'||!/^osu-default:(normal|soft|drum)-hit(normal|whistle|finish|clap)\.wav$/.test(key))return null;const name=key.slice(12);return {name,data:fs.readFileSync(path.join(folder,name)).toString('base64')};}
function filename(row,asset){if(!/^(normal|soft|drum)-hit(normal|whistle|finish|clap)(\d*)\.(wav|ogg|mp3)$/i.test(row.name))return row.name;const ext=/\.(wav|ogg|mp3)$/i.exec(asset.name)?.[0];return ext?row.name.replace(/\.(wav|ogg|mp3)$/i,ext):row.name;}
module.exports={bundled,filename};
