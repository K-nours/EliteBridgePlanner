#!/usr/bin/env node
/**
 * Analyse des fichiers du backfill Frontier Journal — rapport préliminaire.
 *
 * Usage: node analyze-frontier-journal.js [--data-dir PATH] [--output FILE]
 *   --data-dir   Dossier contenant les 3 fichiers JSON (défaut: ../server/Data/frontier-journal)
 *   --output     Fichier JSON de sortie (défaut: frontier-journal-report.json dans le data-dir)
 *   --skip-raw   Ignorer frontier-journal-raw.json (utile si fichier trop volumineux)
 *
 * Génère un rapport diagnostic : stats globales, erreurs, distribution temporelle, cohérence.
 */

const fs = require('fs');
const path = require('path');

function parseArgs() {
  const args = process.argv.slice(2);
  const out = { dataDir: null, outputFile: null, skipRaw: false };
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--data-dir' && args[i + 1]) {
      out.dataDir = args[++i];
    } else if (args[i] === '--output' && args[i + 1]) {
      out.outputFile = args[++i];
    } else if (args[i] === '--skip-raw') {
      out.skipRaw = true;
    }
  }
  return out;
}

function loadProgress(dataDir) {
  const p = path.join(dataDir, 'frontier-journal-progress.json');
  if (!fs.existsSync(p)) throw new Error(`Fichier absent: ${p}`);
  return JSON.parse(fs.readFileSync(p, 'utf8'));
}

function loadLogs(dataDir) {
  const p = path.join(dataDir, 'frontier-journal-log.json');
  if (!fs.existsSync(p)) throw new Error(`Fichier absent: ${p}`);
  const lines = fs.readFileSync(p, 'utf8').split('\n').filter(Boolean);
  return lines.map((line) => {
    try {
      return JSON.parse(line);
    } catch {
      return null;
    }
  }).filter(Boolean);
}

function loadRaw(dataDir, skipRaw) {
  if (skipRaw) return null;
  const p = path.join(dataDir, 'frontier-journal-raw.json');
  if (!fs.existsSync(p)) return null;
  const stat = fs.statSync(p);
  const sizeMB = stat.size / (1024 * 1024);
  if (sizeMB > 200) {
    console.warn(`⚠ Raw file volumineux (${sizeMB.toFixed(1)} Mo). Si erreur mémoire: node --max-old-space-size=2048 analyze-frontier-journal.js`);
  }
  try {
    return JSON.parse(fs.readFileSync(p, 'utf8'));
  } catch (e) {
    if (e.message && (e.message.includes('out of memory') || e.message.includes('allocation'))) {
      console.warn('⚠ Raw file trop volumineux. Utilisez --skip-raw ou: node --max-old-space-size=2048 analyze-frontier-journal.js');
      return null;
    }
    throw e;
  }
}

function getAllDatesBetween(startStr, minStr) {
  const dates = [];
  const start = new Date(startStr);
  const min = new Date(minStr);
  const cur = new Date(start);
  while (cur >= min) {
    dates.push(cur.toISOString().slice(0, 10));
    cur.setDate(cur.getDate() - 1);
  }
  return dates;
}

function median(arr) {
  if (arr.length === 0) return 0;
  const s = [...arr].sort((a, b) => a - b);
  const m = Math.floor(s.length / 2);
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}

function analyze(dataDir, opts) {
  const progress = loadProgress(dataDir);
  const logs = loadLogs(dataDir);
  const raw = loadRaw(dataDir, opts.skipRaw);

  const report = {
    generatedAt: new Date().toISOString(),
    global: {},
    errors: {},
    temporal: {},
    payloads: {},
    consistency: {},
    conclusion: [],
  };

  const startDate = progress.StartDate || progress.startDate;
  const minDate = progress.MinDate || progress.minDate;
  const total = progress.TotalDaysProcessed || progress.totalDaysProcessed || 0;
  const success = progress.SuccessCount || progress.successCount || 0;
  const empty = progress.EmptyCount || progress.emptyCount || 0;
  const errors = progress.ErrorCount || progress.errorCount || 0;

  report.global = {
    period: { startDate, minDate },
    totalDaysProcessed: total,
    successCount: success,
    emptyCount: empty,
    errorCount: errors,
    pctWithData: total > 0 ? ((success / total) * 100).toFixed(2) : 0,
    pctEmpty: total > 0 ? ((empty / total) * 100).toFixed(2) : 0,
    pctErrors: total > 0 ? ((errors / total) * 100).toFixed(2) : 0,
    completed: progress.Completed ?? progress.completed,
    startedAt: progress.StartedAt || progress.startedAt,
    updatedAt: progress.UpdatedAt || progress.updatedAt,
  };

  const errorLogs = logs.filter((e) => (e.Type || e.type) === 'error');
  const byStatus = {};
  for (const e of errorLogs) {
    const code = e.HttpStatusCode ?? e.httpStatusCode ?? 0;
    byStatus[code] = (byStatus[code] || 0) + 1;
  }

  const firstError = errorLogs[0];
  const lastError = errorLogs[errorLogs.length - 1];
  const first401 = errorLogs.find((e) => (e.HttpStatusCode ?? e.httpStatusCode) === 401);

  report.errors = {
    total: errorLogs.length,
    byHttpStatusCode: byStatus,
    count401: byStatus[401] || 0,
    count500: byStatus[500] || 0,
    otherCodes: Object.entries(byStatus)
      .filter(([k]) => k !== '401' && k !== '500')
      .reduce((acc, [k, v]) => ({ ...acc, [k]: v }), {}),
    firstOccurrence: firstError
      ? {
          date: firstError.RequestedDate || firstError.requestedDate,
          timestamp: firstError.Timestamp || firstError.timestamp,
          statusCode: firstError.HttpStatusCode ?? firstError.httpStatusCode,
        }
      : null,
    first401Occurrence: first401
      ? {
          date: first401.RequestedDate || first401.requestedDate,
          timestamp: first401.Timestamp || first401.timestamp,
        }
      : null,
    lastOccurrence: lastError
      ? {
          date: lastError.RequestedDate || lastError.requestedDate,
          timestamp: lastError.Timestamp || lastError.timestamp,
        }
      : null,
    tokenExpirationLikely: (byStatus[401] || 0) > 10,
  };

  let successDates = [];
  if (raw) {
    successDates = Object.entries(raw)
      .filter(([, v]) => (v.Status || v.status) === 'success')
      .map(([d]) => d)
      .sort();
  } else {
    successDates = logs
      .filter((e) => (e.Type || e.type) === 'success')
      .map((e) => e.RequestedDate || e.requestedDate)
      .filter(Boolean);
    successDates = [...new Set(successDates)].sort();
  }

  const firstSuccess = successDates[0];
  const lastSuccess = successDates[successDates.length - 1];

  const byMonth = {};
  for (const d of successDates) {
    const m = d.slice(0, 7);
    byMonth[m] = (byMonth[m] || 0) + 1;
  }

  report.temporal = {
    totalDaysWithData: successDates.length,
    firstDateWithData: firstSuccess || null,
    lastDateWithData: lastSuccess || null,
    byMonth: Object.entries(byMonth)
      .sort(([a], [b]) => a.localeCompare(b))
      .reduce((acc, [k, v]) => ({ ...acc, [k]: v }), {}),
  };

  if (raw && successDates.length > 0) {
    const payloadSizes = [];
    const eventCounts = [];
    for (const d of successDates) {
      const entry = raw[d];
      if (!entry) continue;
      const size = entry.PayloadSize ?? entry.payloadSize ?? 0;
      payloadSizes.push(size);
      let count = entry.EntriesCount ?? entry.entriesCount;
      if (count == null || count < 0) {
        try {
          const p = entry.Payload ?? entry.payload;
          if (typeof p === 'string' && p.length > 2) {
            const arr = JSON.parse(p);
            count = Array.isArray(arr) ? arr.length : 0;
          }
        } catch {
          count = null;
        }
      }
      if (count != null && count >= 0) eventCounts.push(count);
    }

    report.payloads = {
      daysAnalyzed: payloadSizes.length,
      payloadSize: {
        min: Math.min(...payloadSizes),
        max: Math.max(...payloadSizes),
        avg: Math.round(
          payloadSizes.reduce((a, b) => a + b, 0) / payloadSizes.length
        ),
        median: Math.round(median(payloadSizes)),
      },
    };

    if (eventCounts.length > 0) {
      report.payloads.eventsPerDay = {
        min: Math.min(...eventCounts),
        max: Math.max(...eventCounts),
        avg: (
          eventCounts.reduce((a, b) => a + b, 0) / eventCounts.length
        ).toFixed(1),
        median: median(eventCounts),
      };
      const sorted = [...eventCounts].sort((a, b) => b - a);
      report.payloads.highestActivityDays = sorted.slice(0, 5);
      report.payloads.lowestActivityDays = sorted.slice(-5).reverse();
    }
  }

  const expectedDates = getAllDatesBetween(startDate, minDate);
  const rawDates = raw ? Object.keys(raw) : logs.map((e) => e.RequestedDate || e.requestedDate).filter(Boolean);
  const rawSet = new Set(rawDates);
  const missing = expectedDates.filter((d) => !rawSet.has(d));
  const extra = rawDates.filter((d) => !expectedDates.includes(d));

  report.consistency = {
    expectedDateCount: expectedDates.length,
    actualDateCount: rawSet.size,
    missingDates: missing.length,
    missingSample: missing.slice(0, 10),
    extraDates: extra.length,
    hasGaps: missing.length > 0,
  };

  report.conclusion = buildConclusion(report);
  return report;
}

function buildConclusion(report) {
  const g = report.global;
  const e = report.errors;
  const t = report.temporal;
  const c = report.consistency;
  const conclusions = [];

  conclusions.push(
    `Le backfill a couvert ${g.totalDaysProcessed} jours entre ${g.period.startDate} et ${g.period.minDate}.`
  );
  conclusions.push(
    `${g.successCount} jours contiennent des données (${g.pctWithData}%).`
  );

  if (t.firstDateWithData && t.lastDateWithData) {
    conclusions.push(
      `Les données sont concentrées entre ${t.firstDateWithData} et ${t.lastDateWithData}.`
    );
  }

  if (e.total > 0) {
    let errLine = `${e.total} erreur(s) détectée(s), principalement des 401 (${e.count401})`;
    const refDate = e.first401Occurrence?.date || e.firstOccurrence?.date;
    if (refDate) {
      errLine += ` à partir du ${refDate}`;
    }
    errLine += '.';
    conclusions.push(errLine);
    if (e.tokenExpirationLikely) {
      conclusions.push('Probable expiration du token Frontier pendant le run.');
    }
  }

  if (c.hasGaps) {
    conclusions.push(
      `${c.missingDates} date(s) manquante(s) par rapport à la période attendue.`
    );
  }

  if (g.pctErrors > 5) {
    conclusions.push('Les données récupérées sont exploitables mais partielles.');
  } else if (g.pctWithData < 5) {
    conclusions.push(
      'Peu de jours avec données — profil d\'activité faible ou token expiré tôt.'
    );
  } else {
    conclusions.push('Les données récupérées semblent exploitables.');
  }

  return conclusions;
}

function formatReport(report) {
  const lines = [];
  const sep = () => lines.push('─'.repeat(60));

  lines.push('\n📋 RAPPORT FRONTIER JOURNAL — ANALYSE PRÉLIMINAIRE');
  sep();
  lines.push('\n1. RAPPORT GLOBAL');
  lines.push(`   Période      : ${report.global.period.startDate} → ${report.global.period.minDate}`);
  lines.push(`   Jours traités: ${report.global.totalDaysProcessed}`);
  lines.push(`   Succès (data): ${report.global.successCount} (${report.global.pctWithData}%)`);
  lines.push(`   Vides        : ${report.global.emptyCount} (${report.global.pctEmpty}%)`);
  lines.push(`   Erreurs      : ${report.global.errorCount} (${report.global.pctErrors}%)`);
  lines.push(`   Terminé      : ${report.global.completed ? 'Oui' : 'Non'}`);

  sep();
  lines.push('\n2. ANALYSE DES ERREURS');
  lines.push(`   Total erreurs: ${report.errors.total}`);
  lines.push(`   401          : ${report.errors.count401}`);
  lines.push(`   500          : ${report.errors.count500}`);
  if (Object.keys(report.errors.otherCodes).length > 0) {
    lines.push(`   Autres       : ${JSON.stringify(report.errors.otherCodes)}`);
  }
  if (report.errors.firstOccurrence) {
    lines.push(`   Première     : ${report.errors.firstOccurrence.date} (HTTP ${report.errors.firstOccurrence.statusCode ?? '?'})`);
  }
  if (report.errors.first401Occurrence) {
    lines.push(`   Premier 401  : ${report.errors.first401Occurrence.date}`);
  }
  if (report.errors.lastOccurrence) {
    lines.push(`   Dernière     : ${report.errors.lastOccurrence.date}`);
  }
  if (report.errors.tokenExpirationLikely) {
    lines.push('   ⚠ Probable expiration du token Frontier pendant le run.');
  }

  sep();
  lines.push('\n3. DISTRIBUTION TEMPORELLE DES SUCCÈS');
  lines.push(`   Jours avec données: ${report.temporal.totalDaysWithData}`);
  lines.push(`   Première date     : ${report.temporal.firstDateWithData ?? '—'}`);
  lines.push(`   Dernière date     : ${report.temporal.lastDateWithData ?? '—'}`);
  if (Object.keys(report.temporal.byMonth || {}).length > 0) {
    lines.push('   Par mois:');
    for (const [m, cnt] of Object.entries(report.temporal.byMonth)) {
      lines.push(`     ${m}: ${cnt} jours`);
    }
  }

  if (report.payloads && report.payloads.daysAnalyzed) {
    sep();
    lines.push('\n4. ANALYSE DES PAYLOADS');
    lines.push(`   Jours analysés: ${report.payloads.daysAnalyzed}`);
    if (report.payloads.payloadSize) {
      const ps = report.payloads.payloadSize;
      lines.push(`   Taille payload (octets): min=${ps.min}, max=${ps.max}, moy=${ps.avg}, médiane=${ps.median}`);
    }
    if (report.payloads.eventsPerDay) {
      const ep = report.payloads.eventsPerDay;
      lines.push(`   Événements/jour: min=${ep.min}, max=${ep.max}, moy=${ep.avg}, médiane=${ep.median}`);
    }
  }

  sep();
  lines.push('\n5. VÉRIFICATION COHÉRENCE');
  lines.push(`   Dates attendues : ${report.consistency.expectedDateCount}`);
  lines.push(`   Dates présentes : ${report.consistency.actualDateCount}`);
  lines.push(`   Manquantes      : ${report.consistency.missingDates}`);
  if (report.consistency.missingSample?.length) {
    lines.push(`   Ex. manquantes  : ${report.consistency.missingSample.join(', ')}`);
  }

  sep();
  lines.push('\n6. CONCLUSION');
  for (const c of report.conclusion) {
    lines.push(`   • ${c}`);
  }
  sep();
  lines.push('');

  return lines.join('\n');
}

function main() {
  const opts = parseArgs();
  const baseDir = path.resolve(__dirname);
  const dataDir =
    opts.dataDir ||
    path.join(baseDir, '..', 'server', 'Data', 'frontier-journal');

  if (!fs.existsSync(dataDir)) {
    console.error(`Dossier inexistant: ${dataDir}`);
    process.exit(1);
  }

  console.log(`Lecture des fichiers depuis: ${dataDir}\n`);

  let report;
  try {
    report = analyze(dataDir, opts);
  } catch (err) {
    console.error('Erreur:', err.message);
    process.exit(1);
  }

  const text = formatReport(report);
  console.log(text);

  const outputPath =
    opts.outputFile || path.join(dataDir, 'frontier-journal-report.json');
  fs.writeFileSync(
    outputPath,
    JSON.stringify(
      {
        ...report,
        conclusion: report.conclusion,
      },
      null,
      2
    ),
    'utf8'
  );
  console.log(`Rapport JSON enregistré: ${outputPath}`);
}

main();
