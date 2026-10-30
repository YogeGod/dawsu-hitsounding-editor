import fs from 'node:fs';
const dir = new URL('./', import.meta.url);
const html = fs.readFileSync(new URL('neumorphism-current.html', dir), 'utf8');
const css = fs.readFileSync(new URL('neumorphism-depth.css', dir), 'utf8');
if (!html.includes('</head>') || html.includes('id="neumorphism-depth"')) throw Error('Invalid base');
const edited = html.replace('</head>', `<style id="neumorphism-depth">${css}</style></head>`);
if (edited.slice(edited.indexOf('<body')) !== html.slice(html.indexOf('<body'))) throw Error('Unexpected content change');
fs.writeFileSync(new URL('neumorphism-depth.html', dir), edited);
console.log('Prepared tactile refinement; all existing content retained.');
