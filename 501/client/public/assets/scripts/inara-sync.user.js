// ==UserScript==
// @name         Inara Sync — Guild Dashboard
// @namespace    https://github.com/elitebridgeplanner
// @version      2.11.0
// @description  Script unique : bridge sur dashboard, extraction systèmes (faction), extraction CMDRs (squadron)
// @author       EliteBridgePlanner
// @match        https://inara.cz/elite/*
// @match        http://localhost:4200/*
// @match        https://localhost:4200/*
// @match        http://127.0.0.1:4200/*
// @grant        GM_xmlhttpRequest
// @run-at       document-idle
// ==/UserScript==

(function () {
  'use strict';

  console.log('Tampermonkey script loaded', location.href);

  const host = window.location.hostname || '';
  const path = window.location.pathname || '';
  const isDashboard = host === 'localhost' || host === '127.0.0.1' || host.includes('guild-dashboard');
  const isInara = host.includes('inara.cz');
  const isFactionPresence = isInara && path.includes('minorfaction-presence');
  const isSquadronRoster = isInara && path.includes('squadron-roster');
  const isCmdrProfile = isInara && path.includes('/elite/cmdr/');

  const BACKEND_URL = 'https://localhost:7294';

  /** URLs ou patterns identifiant l'avatar par défaut Inara (placeholder). Ne pas enregistrer en base.
   * Garder synchronisé avec avatar.constants.ts (AVATAR_PLACEHOLDER_PATTERNS). */
  const AVATAR_PLACEHOLDER_PATTERNS = [
    'inara.cz/data/avatars/default',
    'inara.cz/images/default-avatar',
    '/default-avatar',
    'noavatar',
  ];
  function isPlaceholderAvatarUrl(url) {
    if (!url || typeof url !== 'string') return false;
    const lower = url.toLowerCase();
    return AVATAR_PLACEHOLDER_PATTERNS.some(p => lower.includes(p.toLowerCase()));
  }
  function urlMatchesPlaceholderPattern(url) {
    if (!url || typeof url !== 'string') return false;
    const lower = url.toLowerCase();
    return AVATAR_PLACEHOLDER_PATTERNS.some(p => lower.includes(p.toLowerCase()));
  }

  /** Détecte si la page a été ouverte avec autoImport=1 (depuis le bouton sync du dashboard). */
  const urlParams = new URLSearchParams(location.search || '');
  const autoImport = urlParams.get('autoImport') === '1';
  const syncAvatars = urlParams.get('syncAvatars') === '1';
  const openerOrigin = urlParams.get('openerOrigin') || '';

  function notifyOpenerStarted(source) {
    if (openerOrigin && window.opener) {
      try { window.opener.postMessage({ type: 'inara-sync-started', source }, openerOrigin); } catch (_) {}
    }
  }
  function notifyOpenerSuccess(source, detail) {
    if (openerOrigin && window.opener) {
      try { window.opener.postMessage({ type: 'inara-sync-success', source, detail }, openerOrigin); } catch (_) {}
    }
  }
  function notifyOpenerError(source, message) {
    if (openerOrigin && window.opener) {
      try { window.opener.postMessage({ type: 'inara-sync-error', source, message }, openerOrigin); } catch (_) {}
    }
  }

  // ——— Contexte DASHBOARD : exposer le bridge ———
  if (isDashboard) {
    document.documentElement.setAttribute('data-inara-sync-bridge', 'true');
    window.__INARA_SYNC_BRIDGE__ = true;
    if (typeof console !== 'undefined' && console.log) {
      console.log('Inara Sync bridge active on dashboard');
    }
    return;
  }

  // ——— Styles dashboard (cohérence graphique) ———
  function injectDashboardStyles() {
    if (document.getElementById('inara-sync-styles')) return;
    const link = document.createElement('link');
    link.id = 'inara-sync-fonts';
    link.rel = 'stylesheet';
    link.href = 'https://fonts.googleapis.com/css2?family=Exo+2:wght@400;500;600&family=Orbitron:wght@600;700&display=swap';
    document.head.appendChild(link);

    const style = document.createElement('style');
    style.id = 'inara-sync-styles';
    style.textContent = `
      .inara-sync-box{position:fixed;bottom:24px;right:24px;z-index:999998;
        background:rgba(6,20,35,0.95);border:1px solid rgba(0,212,255,0.2);
        border-radius:16px;box-shadow:0 0 10px rgba(0,234,255,0.05),0 4px 20px rgba(0,0,0,0.4);
        padding:1rem 1.25rem;font-family:'Exo 2',sans-serif;
        display:flex;flex-direction:column;gap:0.5rem;min-width:200px;}
      .inara-sync-box h3{margin:0 0 0.5rem;font-family:'Orbitron',sans-serif;font-size:0.7rem;
        font-weight:600;color:#00d4ff;text-transform:uppercase;letter-spacing:0.08em;}
      .inara-sync-btn{padding:0.5rem 0.85rem;font-size:0.75rem;font-family:'Orbitron',sans-serif;
        background:rgba(0,212,255,0.2);border:1px solid rgba(0,212,255,0.4);color:#00d4ff;
        border-radius:8px;cursor:pointer;transition:background 0.15s,border-color 0.15s;}
      .inara-sync-btn:hover{background:rgba(0,212,255,0.3);border-color:rgba(0,212,255,0.5);}
      .inara-sync-menu{display:none;flex-direction:column;gap:0.35rem;margin-top:0.5rem;
        padding-top:0.5rem;border-top:1px solid rgba(0,212,255,0.14);}
      .inara-sync-menu-item{padding:0.4rem 0.6rem;font-size:0.7rem;font-family:'Exo 2',sans-serif;
        background:rgba(0,212,255,0.08);border:1px solid rgba(0,212,255,0.2);
        color:rgba(255,255,255,0.9);border-radius:6px;cursor:pointer;text-align:left;
        transition:background 0.15s;}
      .inara-sync-menu-item:hover{background:rgba(0,212,255,0.18);}
      .inara-sync-toast{position:fixed;bottom:100px;right:24px;z-index:999999;padding:1rem 1.25rem;
        font-family:'Exo 2',sans-serif;font-size:0.85rem;max-width:360px;
        background:rgba(6,20,35,0.98);border:1px solid rgba(0,212,255,0.3);
        border-radius:12px;box-shadow:0 8px 32px rgba(0,0,0,0.4);
        color:rgba(255,255,255,0.95);transition:opacity 0.3s;}
      .inara-sync-toast--error{border-color:rgba(255,107,107,0.5);}
    `;
    document.head.appendChild(style);
  }

  function showToast(msg, isError = false) {
    injectDashboardStyles();
    const id = 'inara-sync-toast';
    const existing = document.getElementById(id);
    if (existing) existing.remove();
    const el = document.createElement('div');
    el.id = id;
    el.className = 'inara-sync-toast' + (isError ? ' inara-sync-toast--error' : '');
    el.textContent = msg;
    document.body.appendChild(el);
    setTimeout(() => { el.style.opacity = '0'; setTimeout(() => el.remove(), 300); }, 4000);
  }

  // GM_xmlhttpRequest (bypass CORS) — renvoie une Promise<{ok, message?, json}>
  function gmPost(url, data) {
    return new Promise((resolve) => {
      GM_xmlhttpRequest({
        method: 'POST',
        url,
        headers: { 'Content-Type': 'application/json' },
        data: typeof data === 'string' ? data : JSON.stringify(data),
        onload: (res) => {
          let json = null;
          try { json = JSON.parse(res.responseText || '{}'); } catch (e) { json = {}; }
          if (json === null || typeof json !== 'object') json = {};
          const ok = res.status >= 200 && res.status < 300;
          const msg = json.error || json.message || res.statusText || (ok ? 'OK' : `Erreur ${res.status}`);
          if (typeof console !== 'undefined' && console.log) {
            console.log('[Inara Sync] Réponse HTTP:', { status: res.status, responseText: (res.responseText || '').slice(0, 500), json });
          }
          resolve({ ok, message: msg, json });
        },
        onerror: (err) => {
          const json = {};
          resolve({ ok: false, message: (err && err.message) || 'Erreur réseau. Vérifiez que le backend est démarré.', json });
        }
      });
    });
  }

  // ——— Contexte FACTION PRESENCE : extraction systèmes ———
  if (isFactionPresence) {
    const SPECIAL_CHARS = /[^\p{L}\p{N}\s.\-']/gu;
    const PERCENT_REGEX = /(\d+(?:[.,]\d+)?)\s*%?/;
    const UPDATED_REGEX = /(\d+\s*(?:day|hour|minute|week)s?\s*(?:ago)?|il y a \d+\s*(?:jour|heure|minute)s?)/i;
    const NUMBER_CLEAN = /[^\d]/g;

    function cleanName(str) {
      if (!str || typeof str !== 'string') return '';
      return str.replace(SPECIAL_CHARS, '').replace(/\s+/g, ' ').trim();
    }
    function parsePercent(str) {
      if (!str) return null;
      const m = String(str).match(PERCENT_REGEX);
      return m ? parseFloat(m[1].replace(',', '.')) : null;
    }
    function parseLong(str) {
      if (!str) return null;
      const n = parseInt(String(str).replace(NUMBER_CLEAN, '') || '0', 10);
      return isNaN(n) ? null : n;
    }
    function parseUpdated(str) {
      if (!str) return null;
      const s = String(str).trim();
      const m = s.match(UPDATED_REGEX);
      return m ? m[1].trim() : (s.length < 50 ? s : null);
    }
    function extractPower(cell) {
      const link = cell.querySelector('a[href*="/elite/power/"]');
      if (link) return link.textContent?.trim() || null;
      return (cell.textContent || '').trim() || null;
    }
    function findColumnIndex(headers, ...labels) {
      for (const label of labels) {
        const i = headers.findIndex((h) => h.toLowerCase().includes(label.toLowerCase()));
        if (i >= 0) return i;
      }
      return -1;
    }

    /** Extrait l'origine depuis le bloc d'infos faction (header Inara), ex. "HIP 4332". */
    function extractOriginFromHeader() {
      const log = typeof console !== 'undefined' && console.log ? (...a) => console.log('[Inara Sync][Systems][Origin]', ...a) : () => {};
      const labelTexts = ['origin', 'origine'];
      const labels = document.querySelectorAll('dt, td, th, div, span, label');
      log('extractOriginFromHeader: scanning', labels.length, 'éléments (dt,td,th,div,span,label)');
      for (const el of labels) {
        const raw = (el.textContent || '').trim();
        const rawLower = raw.toLowerCase();
        const isOriginLabel = raw && (labelTexts.includes(rawLower) || labelTexts.some(l => rawLower.startsWith(l + ' ') || rawLower.startsWith(l + ':')));
        if (isOriginLabel && !el.closest('table thead')) {
          const link = el.parentElement?.querySelector?.('a[href*="/elite/starsystem/"]')
            || el.nextElementSibling?.querySelector?.('a[href*="/elite/starsystem/"]')
            || el.closest('tr')?.querySelector?.('a[href*="/elite/starsystem/"]');
          log('Label trouvé:', raw, '| bloc:', el.tagName, el.className || '(pas de class)', '| link:', link ? link.href : 'NON');
          if (link) {
            const name = cleanName(link.textContent);
            const linkText = (link.textContent || '').trim();
            log('Texte brut lien:', JSON.stringify(linkText), '| cleanName:', name);
            if (name) {
              log('Origin détectée:', name);
              return name;
            }
          }
        }
      }
      log('Aucun label Origin/Origine trouvé avec lien starsystem');
      return null;
    }

    function extractSystems() {
      const tables = document.querySelectorAll('table');
      let targetTable = null;
      for (const t of tables) {
        if (t.querySelector('a[href*="/elite/starsystem/"]')) {
          targetTable = t;
          break;
        }
      }
      if (!targetTable) return { systems: [], error: 'Table de présence introuvable' };

      const allRows = Array.from(targetTable.querySelectorAll('tr'));
      const headerRow = targetTable.querySelector('thead tr') || allRows[0];
      const headerCells = headerRow ? Array.from(headerRow.querySelectorAll('th, td')).map((c) => (c.textContent || '').trim()) : [];
      const idxGov = findColumnIndex(headerCells, 'government', 'gov');
      const idxAlleg = findColumnIndex(headerCells, 'allegiance', 'alleg');
      const idxPower = findColumnIndex(headerCells, 'power');
      const idxPop = findColumnIndex(headerCells, 'population', 'pop');
      const idxInf = findColumnIndex(headerCells, 'influence', 'presence');
      const idxUpd = findColumnIndex(headerCells, 'updated');
      const idxFac = idxPop >= 0 ? idxPop + 1 : 5;
      const idxSta = idxPop >= 0 ? idxPop + 2 : 6;
      const iG = idxGov >= 0 ? idxGov : 1;
      const iA = idxAlleg >= 0 ? idxAlleg : 2;
      const iP = idxPower >= 0 ? idxPower : 3;
      const iPop = idxPop >= 0 ? idxPop : 4;
      const iF = idxPop >= 0 ? idxPop + 1 : 5;
      const iS = idxPop >= 0 ? idxPop + 2 : 6;
      const iI = idxInf >= 0 ? idxInf : 7;
      const iU = idxUpd >= 0 ? idxUpd : 8;

      // Header vs row structure : header a th/td pour chaque colonne ; les lignes data ont th scope="row" + td.
      // Ne pas mélanger : utiliser th,td dans les deux pour aligner les index.
      const dataRows = allRows.filter((r) => r.querySelector('a[href*="/elite/starsystem/"]'));
      const systems = [];
      const seen = new Set();

      const firstDataRow = dataRows[0];
      const firstCells = firstDataRow ? Array.from(firstDataRow.querySelectorAll('th, td')) : [];
      if (typeof console !== 'undefined' && console.log) {
        console.log('[Inara Sync] extractSystems: headerCells=', headerCells, 'cells.length=', firstCells.length, 'iI=', iI, 'iU=', iU,
          'influenceCell=', firstCells[iI]?.textContent?.trim()?.slice(0, 30), 'updatedCell=', firstCells[iU]?.textContent?.trim()?.slice(0, 30));
      }

      for (const row of dataRows) {
        const cells = Array.from(row.querySelectorAll('th, td'));
        if (cells.length < 2) continue;
        const link = row.querySelector('a[href*="/elite/starsystem/"]');
        if (!link) continue;

        const name = cleanName(link.textContent);
        if (!name || seen.has(name.toLowerCase())) continue;
        seen.add(name.toLowerCase());

        const get = (i) => (i >= 0 && cells[i] ? (cells[i].textContent || '').trim() : null);
        const gov = get(iG);
        const alleg = get(iA);
        const power = cells[iP] ? extractPower(cells[iP]) : null;
        const pop = parseLong(get(iPop));
        const facVal = get(iF);
        const staVal = get(iS);
        const factionCount = facVal ? (parseInt(facVal.replace(/\D/g, ''), 10) || null) : null;
        const stationCount = staVal ? (parseInt(staVal.replace(/\D/g, ''), 10) || null) : null;
        const influence = parsePercent(get(iI));
        const updated = parseUpdated(get(iU));

        systems.push({
          name, government: gov, allegiance: alleg, power,
          population: pop, factionCount, stationCount,
          influencePercent: influence, lastUpdatedText: updated,
          category: 'Guild', isClean: false,
        });
      }

      return { systems, error: null };
    }

    async function postSystems(payload) {
      const url = `${BACKEND_URL.replace(/\/$/, '')}/api/guild/systems/import`;
      if (typeof console !== 'undefined' && console.log) {
        const val = payload?.originSystemName;
        const present = val != null && val !== '';
        console.log('[Inara Sync][Systems] 3. postSystems: originSystemName present=', present, 'value=', val ?? '(absent)');
      }
      const r = await gmPost(url, payload);
      if (!r.ok) return { ok: false, message: r.message, json: {} };
      const j = r.json ?? {};
      const inserted = j.inserted ?? 0;
      const updated = j.updated ?? 0;
      const skipped = j.skipped ?? 0;
      const deleted = j.deleted ?? 0;
      const totalReceived = j.totalReceived ?? 0;
      return { ok: true, message: `Importé : ${inserted} insérés, ${updated} mis à jour, ${skipped} ignorés (${totalReceived} reçus)`, json: j };
    }

    function runSystems(mode) {
      const { systems, error } = extractSystems();
      if (error) { showToast(error, true); return; }
      if (systems.length === 0) {
        showToast('Aucun système extrait. Vérifiez que la page affiche le tableau de présence.', true);
        return;
      }
      const originSystemName = extractOriginFromHeader();
      const payload = { originSystemName: originSystemName || undefined, systems };
      if (typeof console !== 'undefined' && console.log) {
        const extracted = !!originSystemName;
        const inPayload = payload.originSystemName != null && payload.originSystemName !== '';
        console.log('[Inara Sync][Systems] 1. Origin detected:', extracted ? originSystemName : 'NON');
        console.log('[Inara Sync][Systems] 2. Payload envoyé:', JSON.stringify({ originSystemName: payload.originSystemName ?? '(absent)', systemsCount: systems.length }));
        console.log('[Inara Sync][Systems] DIAG: extracted=' + extracted + ' inPayload=' + inPayload + ' → ' + (extracted && inPayload ? 'OK (backend devrait recevoir)' : extracted ? 'PERDU?' : 'non extrait (DOM)'));
      }
      if (mode === 'download') {
        const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `guild-systems-inara-${new Date().toISOString().slice(0, 10)}.json`;
        a.click();
        URL.revokeObjectURL(a.href);
        showToast(`${systems.length} système(s) téléchargé(s)`);
      } else {
        showToast('Envoi en cours…');
        postSystems(payload).then((r) => showToast(r.message, !r.ok));
      }
    }

    injectFactionButton();
    function injectFactionButton() {
      if (document.getElementById('inara-sync-faction-btn')) return;
      injectDashboardStyles();
      const container = document.createElement('div');
      container.id = 'inara-sync-faction-container';
      container.className = 'inara-sync-box';

      const title = document.createElement('h3');
      title.textContent = 'Inara Sync';
      container.appendChild(title);

      const btn = document.createElement('button');
      btn.id = 'inara-sync-faction-btn';
      btn.className = 'inara-sync-btn';
      btn.textContent = 'Extraire les systèmes';
      btn.onclick = () => { const m = document.getElementById('inara-sync-faction-menu'); m.style.display = m.style.display === 'none' ? 'flex' : 'none'; };

      const menu = document.createElement('div');
      menu.id = 'inara-sync-faction-menu';
      menu.className = 'inara-sync-menu';

      const dl = document.createElement('button');
      dl.className = 'inara-sync-menu-item';
      dl.textContent = 'Télécharger JSON';
      dl.onclick = () => { runSystems('download'); menu.style.display = 'none'; };

      const post = document.createElement('button');
      post.className = 'inara-sync-menu-item';
      post.textContent = 'Envoyer au backend';
      post.onclick = () => { runSystems('post'); menu.style.display = 'none'; };

      menu.appendChild(dl);
      menu.appendChild(post);
      container.appendChild(btn);
      container.appendChild(menu);
      document.body.appendChild(container);
    }
    if (autoImport) {
      setTimeout(() => {
        console.log('[Inara Sync] Auto-import systèmes (autoImport=1)');
        notifyOpenerStarted('systems');
        const { systems, error } = extractSystems();
        if (error) {
          showToast(error, true);
          notifyOpenerError('systems', error);
          return;
        }
        if (systems.length === 0) {
          const msg = 'Aucun système extrait. Vérifiez que la page affiche le tableau de présence.';
          showToast(msg, true);
          notifyOpenerError('systems', msg);
          return;
        }
        const originSystemName = extractOriginFromHeader();
        const payload = { originSystemName: originSystemName || undefined, systems };
        if (typeof console !== 'undefined' && console.log) {
          const extracted = !!originSystemName;
          const inPayload = payload.originSystemName != null && payload.originSystemName !== '';
          console.log('[Inara Sync][Systems] 1. Origin detected:', extracted ? originSystemName : 'NON');
          console.log('[Inara Sync][Systems] 2. Payload envoyé:', JSON.stringify({ originSystemName: payload.originSystemName ?? '(absent)', systemsCount: systems.length }));
          console.log('[Inara Sync][Systems] DIAG: extracted=' + extracted + ' inPayload=' + inPayload + ' → ' + (extracted && inPayload ? 'OK (backend devrait recevoir)' : extracted ? 'PERDU?' : 'non extrait (DOM)'));
        }
        showToast('Envoi en cours…');
        postSystems({ originSystemName: originSystemName || undefined, systems }).then((r) => {
          try {
            if (!r.ok) {
              showToast(r.message, true);
              notifyOpenerError('systems', r.message);
              return;
            }
            console.log('[Inara Sync] Import systèmes réussi');
            showToast(r.message);
            const j = r.json ?? {};
            const inserted = j.inserted ?? 0;
            const updated = j.updated ?? 0;
            const totalReceived = j.totalReceived ?? systems.length;
            const detail = { inserted, updated, total: totalReceived };
            console.log('[Inara Sync] Avant postMessage:', { hasOpener: !!window.opener, typeofOpener: typeof window.opener, openerOrigin, urlCourante: location.href });
            notifyOpenerSuccess('systems', detail);
            console.log('[Inara Sync] Tentative fermeture');
            window.close();
            console.log('[Inara Sync] window.close() appelé');
          } catch (e) {
            console.log('[Inara Sync] Erreur fin de flux (postMessage/close):', e);
            try { notifyOpenerSuccess('systems', { inserted: 0, updated: 0, total: systems.length }); } catch (_) {}
            try { window.close(); } catch (_) {}
          }
        });
      }, 1200);
    }
    return;
  }

  // ——— Contexte SQUADRON ROSTER : extraction CMDRs ou sync avatars ———
  if (isSquadronRoster) {
    /** Extrait les URLs complètes des pages CMDR depuis le tableau roster (pour sync avatars). */
    function extractCmdrPageUrls() {
      const urls = [];
      const seen = new Set();
      const tables = document.querySelectorAll('table');
      let rosterTable = null;
      for (const t of tables) {
        const cmdrLink = t.querySelector('a[href*="/elite/cmdr/"]');
        if (cmdrLink) {
          const row = cmdrLink.closest('tr');
          if (row && row.querySelectorAll('td').length >= 2) {
            rosterTable = t;
            break;
          }
        }
      }
      const root = rosterTable || document.body;
      root.querySelectorAll('a[href*="/elite/cmdr/"]').forEach(link => {
        const href = (link.getAttribute('href') || '').trim();
        if (!href.includes('/elite/cmdr/') || href.match(/\/elite\/cmdr\/\s*$/)) return;
        const full = href.startsWith('http') ? href : new URL(href, location.origin).href;
        if (seen.has(full)) return;
        seen.add(full);
        urls.push(full);
      });
      return urls;
    }

    if (syncAvatars && openerOrigin && window.opener) {
      setTimeout(() => {
        const urls = extractCmdrPageUrls();
        console.log('[Inara Sync] syncAvatars=1 :', urls.length, 'lien(s) CMDR extrait(s)');
        try {
          window.opener.postMessage({ type: 'inara-roster-cmdr-urls', urls }, openerOrigin);
        } catch (e) {
          console.log('[Inara Sync] postMessage inara-roster-cmdr-urls échec:', e);
        }
      }, 1500);
    }

    // Répare UTF-8 mal interprété comme Latin-1 (ex. "AperÃ§u" → "Aperçu"). N'applique que si mojibake détecté.
    function fixUtf8Mojibake(str) {
      if (!str || typeof str !== 'string') return str || '';
      if (!/Ã|Â/.test(str)) return str;
      try {
        const bytes = new Uint8Array(str.length);
        for (let i = 0; i < str.length; i++) bytes[i] = str.charCodeAt(i) & 0xff;
        return new TextDecoder('utf-8').decode(bytes);
      } catch { return str; }
    }

    // Noms d'interface à exclure (colonnes, boutons, libellés) — en minuscules
    const ROSTER_BLOCKLIST = new Set([
      'commandant', 'aperçu', 'commander', 'preview', 'cmdr', 'rank', 'game', 'power',
      'elite', 'elite i', 'elite ii', 'elite iii', 'elite iv', 'elite v', 'élite v',
      'entrepreneur', 'explorer', 'combat', 'trade', 'cqc', 'federation', 'empire'
    ].map(s => s.toLowerCase()));

    function isBlockedName(name) {
      if (!name || name.length < 2) return true;
      const lower = name.toLowerCase().trim();
      if (ROSTER_BLOCKLIST.has(lower)) return true;
      if (lower.length < 3) return true;
      return false;
    }

    function extractCommanders() {
      const commanders = [];
      const seen = new Set();
      // Cibler uniquement le tableau roster : lignes tbody avec des cellules td (données)
      const tables = document.querySelectorAll('table');
      let rosterTable = null;
      for (const t of tables) {
        const cmdrLink = t.querySelector('a[href*="/elite/cmdr/"]');
        if (cmdrLink) {
          const row = cmdrLink.closest('tr');
          if (row && row.querySelectorAll('td').length >= 2) {
            rosterTable = t;
            break;
          }
        }
      }
      const root = rosterTable || document.body;
      root.querySelectorAll('a[href*="/elite/cmdr/"]').forEach(link => {
        const row = link.closest('tr');
        if (!row) return;
        const cells = row.querySelectorAll('td');
        if (cells.length < 2) return;
        if (row.querySelector('th')) return;
        const href = (link.getAttribute('href') || '').trim();
        if (!href.includes('/elite/cmdr/') || href.match(/\/elite\/cmdr\/\s*$/)) return;
        const inaraUrl = href.startsWith('http') ? href : new URL(href, location.origin).href;
        let name = fixUtf8Mojibake((link.textContent || '').trim());
        if (!name || seen.has(name.toLowerCase())) return;
        if (isBlockedName(name)) return;
        seen.add(name.toLowerCase());

        let role = null;
        const idx = Array.from(cells).findIndex((c) => c.contains(link));
        if (idx >= 0 && cells[idx + 1]) role = fixUtf8Mojibake((cells[idx + 1].textContent || '').trim()) || null;

        commanders.push({ name, role: role || undefined, inaraUrl });
      });

      const names = commanders.map((c) => c.name);
      console.log('[Inara Sync] extractCommanders: count=' + commanders.length + ', names=' + JSON.stringify(names));
      return commanders;
    }

    function filterAndValidate(commanders) {
      const filtered = [];
      const seen = new Set();
      for (const c of commanders) {
        const name = (c.name || '').trim();
        if (!name) continue;
        const key = name.toLowerCase();
        if (seen.has(key)) continue;
        if (isBlockedName(name)) continue;
        seen.add(key);
        filtered.push({ name, role: c.role || undefined, inaraUrl: c.inaraUrl || undefined });
      }
      console.log('[Inara Sync] Après validation: ' + filtered.length + ' CMDR(s), noms=' + JSON.stringify(filtered.map(f => f.name)));
      return filtered;
    }

    async function postCommanders(payload) {
      const url = `${BACKEND_URL.replace(/\/$/, '')}/api/sync/inara/commanders/import`;
      console.log('[Inara Sync] Envoi POST roster:', url);
      const r = await gmPost(url, payload);
      if (!r.ok) return { ok: false, message: r.message, json: {} };
      const j = r.json ?? {};
      const imported = j.imported ?? 0;
      const totalReceived = j.totalReceived ?? 0;
      return { ok: true, message: `Importé : ${imported} CMDR(s) (${totalReceived} reçus)`, json: j };
    }

    function runCommanders(mode) {
      const raw = extractCommanders();
      const commanders = filterAndValidate(raw);
      if (commanders.length === 0) {
        showToast('Aucun CMDR valide extrait. Vérifiez la page roster.', true);
        return;
      }
      const payload = { commanders };
      if (mode === 'download') {
        const json = JSON.stringify(payload, null, 2);
        const blob = new Blob(['\uFEFF' + json], { type: 'application/json;charset=utf-8' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `squadron-cmdrs-${new Date().toISOString().slice(0, 10)}.json`;
        a.click();
        URL.revokeObjectURL(a.href);
        showToast(`${commanders.length} CMDR(s) téléchargé(s)`);
      } else {
        showToast('Envoi en cours…');
        postCommanders(payload).then((r) => {
          if (r.ok) {
            showToast(r.message);
          } else {
            showToast('Erreur : ' + r.message, true);
          }
        });
      }
    }

    function injectRosterButton() {
      if (document.getElementById('inara-sync-roster-btn')) return;
      injectDashboardStyles();
      const container = document.createElement('div');
      container.id = 'inara-sync-roster-container';
      container.className = 'inara-sync-box';

      const title = document.createElement('h3');
      title.textContent = 'Inara Sync';
      container.appendChild(title);

      const btn = document.createElement('button');
      btn.id = 'inara-sync-roster-btn';
      btn.className = 'inara-sync-btn';
      btn.textContent = 'Extraire les CMDRs';
      btn.onclick = () => { const m = document.getElementById('inara-sync-roster-menu'); m.style.display = m.style.display === 'none' ? 'flex' : 'none'; };

      const menu = document.createElement('div');
      menu.id = 'inara-sync-roster-menu';
      menu.className = 'inara-sync-menu';

      const dl = document.createElement('button');
      dl.className = 'inara-sync-menu-item';
      dl.textContent = 'Télécharger JSON';
      dl.onclick = () => { runCommanders('download'); menu.style.display = 'none'; };

      const post = document.createElement('button');
      post.className = 'inara-sync-menu-item';
      post.textContent = 'Envoyer au backend';
      post.onclick = () => { runCommanders('post'); menu.style.display = 'none'; };

      menu.appendChild(dl);
      menu.appendChild(post);
      container.appendChild(btn);
      container.appendChild(menu);
      document.body.appendChild(container);
    }

    const doRosterAutoImport = () => {
      const raw = extractCommanders();
      const commanders = filterAndValidate(raw);
      if (commanders.length === 0) {
        const msg = 'Aucun CMDR valide extrait. Vérifiez la page roster.';
        showToast(msg, true);
        notifyOpenerError('roster', msg);
        return;
      }
      showToast('Envoi en cours…');
      postCommanders({ commanders }).then((r) => {
        try {
          if (!r.ok) {
            showToast('Erreur : ' + r.message, true);
            notifyOpenerError('roster', r.message);
            return;
          }
          console.log('[Inara Sync] Import roster réussi');
          showToast(r.message);
          const j = r.json ?? {};
          const imported = j.imported ?? commanders.length;
          const totalReceived = j.totalReceived ?? commanders.length;
          const detail = { imported, total: totalReceived };
          console.log('[Inara Sync] Avant postMessage:', { hasOpener: !!window.opener, typeofOpener: typeof window.opener, openerOrigin, urlCourante: location.href });
          notifyOpenerSuccess('roster', detail);
          console.log('[Inara Sync] Tentative fermeture');
          window.close();
          console.log('[Inara Sync] window.close() appelé');
        } catch (e) {
          console.log('[Inara Sync] Erreur fin de flux roster:', e);
          try { notifyOpenerSuccess('roster', { imported: commanders.length, total: commanders.length }); } catch (_) {}
          try { window.close(); } catch (_) {}
        }
      });
    };
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', () => {
        injectRosterButton();
        if (autoImport) setTimeout(() => { console.log('[Inara Sync] Auto-import CMDRs (autoImport=1)'); notifyOpenerStarted('roster'); doRosterAutoImport(); }, 1200);
      });
    } else {
      injectRosterButton();
      if (autoImport) setTimeout(() => { console.log('[Inara Sync] Auto-import CMDRs (autoImport=1)'); notifyOpenerStarted('roster'); doRosterAutoImport(); }, 1200);
    }
    return;
  }

  // ——— Contexte CMDR PROFILE : extraction avatar ———
  if (isCmdrProfile) {
    const CMDR_NAME_BLOCKLIST = ['commandant', 'commander', 'profil', 'aperçu', 'preview', 'cmdr', 'rôle', 'role', 'inara'];

    function isBlockedName(name) {
      if (!name || typeof name !== 'string') return true;
      const n = name.trim().toLowerCase();
      if (n.length < 2) return true;
      return CMDR_NAME_BLOCKLIST.includes(n);
    }

    /** Extrait l'URL de l'image de profil depuis a[href*="/elite/cmdr/"] img[src*="/data/"] */
    function extractAvatar() {
      const link = document.querySelector('a[href*="/elite/cmdr/"] img[src*="/data/"]');
      if (link) {
        const src = link.src || link.getAttribute('src') || '';
        const url = src.startsWith('http') ? src : new URL(src, location.origin).href;
        if (url) {
          console.log('[Inara Sync] avatarUrl extrait:', url);
          return url.trim();
        }
      }
      const fallback = document.querySelector('img[src*="/data/"]');
      if (fallback) {
        const src = fallback.src || fallback.getAttribute('src') || '';
        const url = src.startsWith('http') ? src : new URL(src, location.origin).href;
        if (url) {
          console.log('[Inara Sync] avatarUrl extrait (fallback):', url);
          return url.trim();
        }
      }
      console.log('[Inara Sync] avatarUrl extrait: (aucun)');
      return null;
    }

    /** Supprime uniquement les préfixes UI (CMD, CMDR, Commander, Commandant). Ne touche pas aux caractères valides du nom. */
    function normalizeCommanderName(raw) {
      if (!raw || typeof raw !== 'string') return '';
      let s = raw.trim();
      const prefixes = ['CMDR ', 'Commander ', 'Commandant ', 'CMD ', 'Cmdr '];
      for (const p of prefixes) {
        if (s.toLowerCase().startsWith(p.toLowerCase())) {
          s = s.slice(p.length).trim();
          break;
        }
      }
      return s.trim();
    }

    /** Sépare le nom du reste du titre. N'utilise que pipe | et en-dash – (séparateurs de sections Inara), pas le tiret -. */
    function extractNameFromTitlePart(raw) {
      if (!raw || typeof raw !== 'string') return raw || '';
      const sep = /\s*[|\u2013]\s*/;
      const parts = raw.split(sep);
      return (parts[0] || '').trim();
    }

    /** Caractères autorisés dans un nom : lettres, chiffres, tiret -, apostrophe ', espaces. */
    const VALID_NAME_CHARS = /^[\p{L}\p{N}\s\-']+$/u;
    function sanitizeCommanderName(name) {
      if (!name || typeof name !== 'string') return { name: '', stripped: false };
      const trimmed = name.trim();
      const cleaned = trimmed.replace(/[\s]+/g, ' ');
      if (VALID_NAME_CHARS.test(cleaned)) return { name: cleaned, stripped: false };
      const kept = cleaned.replace(/[^\p{L}\p{N}\s\-']/gu, '');
      const changed = kept !== cleaned;
      return { name: kept.replace(/[\s]+/g, ' ').trim(), stripped: changed };
    }

    /** Extrait le nom du CMDR. Priorité document.title. Préserve noms avec tiret (Kal-Hagar), chiffres (Arbiiter-117). */
    function extractCommanderName() {
      let title = (document.title || '').trim();
      if (!title) {
        const ogTitle = document.querySelector('meta[property="og:title"]');
        title = (ogTitle && ogTitle.getAttribute('content')) || '';
      }
      console.log('[Inara Sync][Avatar] document.title brut:', JSON.stringify(title));

      if (title) {
        const patterns = [
          { re: /(?:CMDR|Commandant|Cmdr|CMD)\s*[:–\-]?\s*(.+?)(?:\s*[|\u2013]\s*|$)/i, idx: 1 },
          { re: /^(.+?)\s*[|\u2013]\s*Elite/i, idx: 1 },
        ];
        for (const { re, idx } of patterns) {
          const m = title.match(re);
          if (m && m[idx]) {
            const rawPart = m[idx].trim();
            const rawName = extractNameFromTitlePart(rawPart);
            const unprefixed = normalizeCommanderName(rawName);
            const { name, stripped } = sanitizeCommanderName(unprefixed);
            if (stripped) console.log('[Inara Sync][Avatar] filtre caractères:', JSON.stringify(unprefixed), '->', JSON.stringify(name));
            if (name && !isBlockedName(name) && name.length >= 2 && name.length < 50) {
              console.log('[Inara Sync][Avatar] rawName=', JSON.stringify(rawPart), 'normalizedName=', JSON.stringify(name));
              return name;
            }
          }
        }
      }

      const headerH1 = document.querySelector('[class*="mainheader"] h1, [class*="profile"] h1, header h1, .mainheader, h1');
      if (headerH1) {
        const t = (headerH1.textContent || '').trim();
        console.log('[Inara Sync][Avatar] header rawText:', JSON.stringify(t), 'selector:', headerH1.className || headerH1.tagName);
        const h1Match = t.match(/(?:CMDR|Commandant|Cmdr|CMD)\s*[:–\-]?\s*(.+?)(?:\s|$)/i) || (t.length >= 2 && t.length < 50 && !isBlockedName(t) ? [null, t] : null);
        if (h1Match && h1Match[1]) {
          const unprefixed = normalizeCommanderName(h1Match[1]);
          const { name, stripped } = sanitizeCommanderName(unprefixed);
          if (stripped) console.log('[Inara Sync][Avatar] filtre caractères:', JSON.stringify(unprefixed), '->', JSON.stringify(name));
          if (name && !isBlockedName(name) && name.length >= 2) {
            console.log('[Inara Sync][Avatar] rawName=', JSON.stringify(h1Match[1]), 'normalizedName=', JSON.stringify(name));
            return name;
          }
        }
      }
      console.log('[Inara Sync][Avatar] commanderName final: (aucun) — title invalide ou absent');
      return null;
    }

    async function postAvatar(avatarUrl, commanderName) {
      const url = `${BACKEND_URL.replace(/\/$/, '')}/api/sync/inara/avatar`;
      const r = await gmPost(url, { avatarUrl, commanderName });
      if (!r.ok) return { ok: false, message: r.message };
      return { ok: true, message: `Avatar mis à jour pour ${commanderName}` };
    }

    function runAvatar() {
      const avatarUrl = extractAvatar();
      const commanderName = extractCommanderName();
      if (!avatarUrl) {
        showToast('Aucune image avatar trouvée sur cette page.', true);
        return;
      }
      if (isPlaceholderAvatarUrl(avatarUrl)) {
        console.log('[Inara Sync][Avatar] Placeholder détecté: ' + avatarUrl);
        if (!urlMatchesPlaceholderPattern(avatarUrl)) {
          console.warn('[Inara Sync][Avatar] WARNING: URL ignorée comme placeholder mais ne correspond à aucun pattern connu');
        }
        showToast('Avatar par défaut Inara — ignoré');
        return;
      }
      if (!commanderName || isBlockedName(commanderName)) {
        showToast('Nom du CMDR introuvable sur cette page', true);
        return;
      }
      const payload = { avatarUrl, commanderName };
      console.log('[Inara Sync] Payload avatar:', JSON.stringify(payload));
      showToast('Envoi en cours…');
      postAvatar(avatarUrl, commanderName).then((r) => showToast(r.message, !r.ok));
    }

    function injectAvatarButton() {
      if (document.getElementById('inara-sync-avatar-btn')) return;
      injectDashboardStyles();
      const container = document.createElement('div');
      container.id = 'inara-sync-avatar-container';
      container.className = 'inara-sync-box';

      const title = document.createElement('h3');
      title.textContent = 'Inara Sync';
      container.appendChild(title);

      const btn = document.createElement('button');
      btn.id = 'inara-sync-avatar-btn';
      btn.className = 'inara-sync-btn';
      btn.textContent = 'Extraire avatar';
      btn.onclick = () => runAvatar();
      container.appendChild(btn);
      document.body.appendChild(container);
    }

    const doAvatarAutoImport = () => {
      const avatarUrl = extractAvatar();
      const commanderName = extractCommanderName();
      console.log('[Inara Sync] Avatar extraction:', { avatarUrl: !!avatarUrl, commanderName: commanderName || '(vide)' });
      if (!avatarUrl) {
        const msg = 'Aucune image avatar trouvée sur cette page.';
        console.log('[Inara Sync] Erreur avatar:', msg);
        showToast(msg, true);
        notifyOpenerError('avatar', msg);
        return;
      }
      if (isPlaceholderAvatarUrl(avatarUrl)) {
        console.log('[Inara Sync][Avatar] Placeholder détecté: ' + avatarUrl);
        if (!urlMatchesPlaceholderPattern(avatarUrl)) {
          console.warn('[Inara Sync][Avatar] WARNING: URL ignorée comme placeholder mais ne correspond à aucun pattern connu — risque confusion avatar réel/placeholder');
        }
        showToast('Avatar par défaut Inara — ignoré');
        notifyOpenerSuccess('avatar', { commanderName: commanderName || '', skippedPlaceholder: true });
        try { window.close(); } catch (_) {}
        return;
      }
      if (!commanderName || isBlockedName(commanderName)) {
        const msg = 'Nom du CMDR introuvable sur cette page';
        console.log('[Inara Sync][Avatar] Erreur:', msg, { commanderName, isBlocked: commanderName ? isBlockedName(commanderName) : 'N/A' });
        showToast(msg, true);
        notifyOpenerError('avatar', msg);
        return;
      }
      const payload = { avatarUrl, commanderName };
      console.log('[Inara Sync][Avatar] Payload envoyé:', JSON.stringify(payload));
      showToast('Envoi en cours…');
      postAvatar(avatarUrl, commanderName).then((r) => {
        try {
          if (!r.ok) {
            showToast(r.message, true);
            notifyOpenerError('avatar', r.message);
            return;
          }
          console.log('[Inara Sync] Import avatar réussi');
          showToast(r.message);
          console.log('[Inara Sync] Avant postMessage:', { hasOpener: !!window.opener, typeofOpener: typeof window.opener, openerOrigin, urlCourante: location.href });
          notifyOpenerSuccess('avatar', { commanderName });
          console.log('[Inara Sync] Tentative fermeture');
          window.close();
          console.log('[Inara Sync] window.close() appelé');
        } catch (e) {
          console.log('[Inara Sync] Erreur fin de flux avatar:', e);
          try { notifyOpenerSuccess('avatar', { commanderName }); } catch (_) {}
          try { window.close(); } catch (_) {}
        }
      });
    };
    const doAvatarSetup = () => {
      injectAvatarButton();
      if (autoImport) setTimeout(() => { console.log('[Inara Sync] Auto-import avatar (autoImport=1)'); notifyOpenerStarted('avatar'); doAvatarAutoImport(); }, 1200);
    };
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', doAvatarSetup);
    } else {
      doAvatarSetup();
    }
    return;
  }
})();
