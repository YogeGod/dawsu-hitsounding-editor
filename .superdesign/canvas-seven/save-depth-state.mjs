import fs from 'node:fs';
const dir = new URL('./', import.meta.url);
const current = fs.readFileSync(new URL('neumorphism-current.html', dir), 'utf8');
const saved = fs.readFileSync(new URL('neumorphism-depth-verified.html', dir), 'utf8');
const result = JSON.parse(fs.readFileSync(new URL('neumorphism-depth-import.json', dir), 'utf8').replace(/^\uFEFF/, ''));
const id = 'cdfc8520-1f27-4950-9b2c-d2825d247aed';
if (result.draftId !== id || result.currentVersion !== 2 || !saved.includes('id="neumorphism-depth"')) throw Error('Unexpected saved version');
if ((current.match(/<button\b/g)||[]).length !== (saved.match(/<button\b/g)||[]).length) throw Error('Control content changed');
const resumePath = new URL('../resume.json', dir);
const state = JSON.parse(fs.readFileSync(resumePath, 'utf8'));
const target = state.targets['main-editor'];
target.activeDraftId = id;
Object.assign(target.drafts[id], {
  currentVersion: result.currentVersion,
  visualDirection: 'Neumorphism with stronger raised buttons, recessed fields/counters and pressed toggles',
  authoringMethod: 'direct style refinement imported after generation credit limit',
  previewUrl: result.previewUrl,
  canvasNodeUrl: result.nodeUrl
});
target.updatedAt = new Date().toISOString();
fs.writeFileSync(new URL('../resume.pending.json', dir), JSON.stringify(state, null, 2)+'\n');
fs.renameSync(new URL('../resume.pending.json', dir), resumePath);
const projectPath = new URL('../project.json', dir);
const project = JSON.parse(fs.readFileSync(projectPath, 'utf8'));
project.activeDraftId = id;
project.status = 'neumorphism-depth-refined-on-canvas';
for (const draft of project.styleDrafts) if (draft.id === id) draft.currentVersion = 2;
fs.writeFileSync(projectPath, JSON.stringify(project, null, 2)+'\n');
console.log('Verified same draft, version 2, all 57 controls retained; saved Neumorphism as active.');
