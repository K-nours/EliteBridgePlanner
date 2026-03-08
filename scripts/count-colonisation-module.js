#!/usr/bin/env node
/**
 * Compte les lignes du module Colonisation Route Planner.
 * Usage: node scripts/count-colonisation-module.js
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..');
const FILES = [
  'elitebridgeplanner.client/src/app/devtools/colonisation-route.analyzer.ts',
  'elitebridgeplanner.client/src/app/devtools/api-explorer.service.ts',
  'elitebridgeplanner.client/src/app/devtools/api-explorer-demo.component.ts',
  'elitebridgeplanner.client/src/app/devtools/api-explorer-demo.component.html',
  'elitebridgeplanner.client/src/app/devtools/api-explorer-demo.component.scss',
  'elitebridgeplanner.client/src/app/devtools/enrichment-types.ts',
];

const counts = { ts: 0, html: 0, scss: 0 };
const byFile = [];

for (const rel of FILES) {
  const p = path.join(ROOT, rel);
  if (!fs.existsSync(p)) {
    console.warn('Fichier introuvable:', rel);
    continue;
  }
  const content = fs.readFileSync(p, 'utf8');
  const lines = content.split(/\r?\n/).length;
  const ext = path.extname(rel).slice(1);
  if (ext === 'ts') counts.ts += lines;
  else if (ext === 'html') counts.html += lines;
  else if (ext === 'scss') counts.scss += lines;
  byFile.push({ name: path.basename(rel), path: rel, lines, ext: ext.toUpperCase() });
}

const total = counts.ts + counts.html + counts.scss;

console.log('\n┌─────────────────────────────────────────────────────────────┐');
console.log('│         COLONISATION ROUTE PLANNER — Taille du module        │');
console.log('├─────────────────────────────────────────────────────────────┤');
console.log('│  TypeScript : ' + String(counts.ts).padStart(5) + ' lignes                              │');
console.log('│  HTML       : ' + String(counts.html).padStart(5) + ' lignes                              │');
console.log('│  SCSS       : ' + String(counts.scss).padStart(5) + ' lignes                              │');
console.log('├─────────────────────────────────────────────────────────────┤');
console.log('│  Total      : ' + String(total).padStart(5) + ' lignes                              │');
console.log('└─────────────────────────────────────────────────────────────┘\n');

console.log('Fichiers principaux :');
console.log('─'.repeat(60));
byFile
  .sort((a, b) => b.lines - a.lines)
  .forEach((f) => {
    console.log(`  ${String(f.lines).padStart(5)}  ${f.name.padEnd(45)}  [${f.ext}]`);
  });
console.log('─'.repeat(60));
console.log(`  ${String(total).padStart(5)}  total\n`);
