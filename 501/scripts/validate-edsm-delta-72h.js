#!/usr/bin/env node
/**
 * Script de validation : vérifier que notre calcul delta 72h correspond à l'affichage EDSM.
 * 
 * Usage: node validate-edsm-delta-72h.js [systemName1] [systemName2] ...
 * Ex: node validate-edsm-delta-72h.js "NGC 6357 Sector AV-Y c35" "HIP 4332" Mayang
 * 
 * Compare les deltas 24h vs 72h pour The 501st Guild.
 * À comparer manuellement avec la page EDSM du système (triangle vert/rouge).
 */

const FACTION_NAME = 'The 501st Guild';
const WINDOW_24H = 86400;
const WINDOW_72H = 72 * 3600;

const systems = process.argv.slice(2).length
  ? process.argv.slice(2)
  : ['NGC 6357 Sector AV-Y c35', 'HIP 4332', 'Mayang'];

async function fetchSystem(systemName) {
  const url = `https://www.edsm.net/api-system-v1/factions?systemName=${encodeURIComponent(systemName)}&showHistory=1`;
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

function computeDelta(history, currentInfluence, windowSec) {
  if (!history || typeof history !== 'object') return null;
  const nowTs = Math.floor(Date.now() / 1000);
  const targetTs = nowTs - windowSec;
  let bestTs = null;
  let bestVal = 0;
  for (const [tsStr, val] of Object.entries(history)) {
    const ts = parseInt(tsStr, 10);
    if (isNaN(ts) || ts > targetTs) continue;
    if (bestTs === null || ts > bestTs) {
      bestTs = ts;
      bestVal = parseFloat(val);
    }
  }
  if (bestTs === null) return null;
  return (currentInfluence - bestVal) * 100;
}

async function main() {
  console.log('Validation delta EDSM — The 501st Guild');
  console.log('Compare nos calculs 24h/72h avec l\'affichage EDSM (triangle vert/rouge)\n');

  for (const systemName of systems) {
    try {
      const data = await fetchSystem(systemName);
      const faction = data.factions?.find(
        (f) => f.name?.toLowerCase() === FACTION_NAME.toLowerCase()
      );
      if (!faction) {
        console.log(`${systemName}: faction non trouvée`);
        continue;
      }

      const influence = parseFloat(faction.influence ?? 0) * 100;
      const delta24h = computeDelta(faction.influenceHistory, faction.influence, WINDOW_24H);
      const delta72h = computeDelta(faction.influenceHistory, faction.influence, WINDOW_72H);

      console.log(`${systemName}:`);
      console.log(`  Influence actuelle: ${influence.toFixed(2)}%`);
      console.log(`  Delta 24h (notre ancien): ${delta24h != null ? delta24h.toFixed(2) + '%' : 'N/A'}`);
      console.log(`  Delta 72h (aligné EDSM):   ${delta72h != null ? delta72h.toFixed(2) + '%' : 'N/A'}`);
      console.log(`  → À vérifier sur https://www.edsm.net/en/system/name/${encodeURIComponent(systemName.replace(/ /g, '+'))}`);
      console.log('');
    } catch (err) {
      console.error(`${systemName}: erreur`, err.message);
    }
  }
}

main();
