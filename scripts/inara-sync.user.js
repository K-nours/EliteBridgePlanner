// ==UserScript==
// @name         Inara Sync — Guild Dashboard
// @namespace    https://github.com/elitebridgeplanner
// @version      2.17.0
// @description  Script unique : bridge sur dashboard, extraction systèmes (faction), extraction CMDRs (squadron)
// @author       EliteBridgePlanner
// @match        https://inara.cz/elite/*
// @match        http://localhost:4200/*
// @match        https://localhost:4200/*
// @match        http://127.0.0.1:4200/*
// @grant        GM_xmlhttpRequest
// @connect      localhost
// @connect      127.0.0.1
// @run-at       document-idle
// ==/UserScript==

(function () {
  'use strict';

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

  /** postMessage Systems uniquement après réponse backend, jamais avant/pendant. Ignore COOP. */
  function safePostMessageSystems(type, payload) {
    if (!openerOrigin || !window.opener) {
      console.log('[Inara Sync][Systems] postMessage skipped (no opener)');
      return;
    }
    try {
      window.opener.postMessage({ type, source: 'systems', ...payload }, openerOrigin);
      console.log('[Inara Sync][Systems] postMessage SUCCESS', type);
    } catch (e) {
      console.log('[Inara Sync][Systems] postMessage ERROR (ignored)', e?.message || String(e));
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

  // ——— Styles identiques au bouton Import Inara (systems-menu-item) ———
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
      .inara-sync-box{position:fixed !important;bottom:24px !important;right:24px !important;z-index:999998 !important;
        background:rgba(6,20,35,0.98) !important;border:1px solid rgba(0,212,255,0.4) !important;
        border-radius:4px !important;box-shadow:0 4px 12px rgba(0,0,0,0.4) !important;
        padding:0.5rem !important;font-family:'Exo 2',sans-serif !important;
        display:flex !important;flex-direction:column !important;gap:0.2rem !important;min-width:180px !important;}
      .inara-sync-box h3{margin:0 0 0.35rem !important;font-family:'Orbitron',sans-serif !important;font-size:0.65rem !important;
        font-weight:600 !important;color:#00d4ff !important;text-transform:uppercase !important;letter-spacing:0.1em !important;}
      button.inara-sync-btn{padding:0.35rem 0.6rem !important;font-size:0.65rem !important;font-family:'Orbitron',sans-serif !important;
        background:rgba(0,212,255,0.1) !important;border:1px solid rgba(0,212,255,0.25) !important;color:#00d4ff !important;
        border-radius:4px !important;cursor:pointer !important;text-align:left !important;transition:background 0.15s !important;
        display:flex !important;align-items:center !important;justify-content:flex-start !important;width:100% !important;margin:0 !important;box-sizing:border-box !important;line-height:1 !important;}
      button.inara-sync-btn:hover:not(:disabled){background:rgba(0,212,255,0.25) !important;}
      button.inara-sync-btn:disabled{opacity:0.5 !important;cursor:not-allowed !important;}
      .inara-sync-menu{display:none !important;flex-direction:column !important;gap:0.2rem !important;margin-top:0.25rem !important;
        padding-top:0.25rem !important;border-top:1px solid rgba(0,212,255,0.2) !important;}
      .inara-sync-menu.inara-sync-menu--open{display:flex !important;}
      button.inara-sync-menu-item{padding:0.35rem 0.6rem !important;font-size:0.65rem !important;font-family:'Orbitron',sans-serif !important;
        background:rgba(0,212,255,0.1) !important;border:1px solid rgba(0,212,255,0.25) !important;color:#00d4ff !important;
        border-radius:4px !important;cursor:pointer !important;text-align:left !important;transition:background 0.15s !important;
        display:flex !important;align-items:center !important;justify-content:flex-start !important;width:100% !important;margin:0 !important;box-sizing:border-box !important;line-height:1 !important;}
      button.inara-sync-menu-item:hover:not(:disabled){background:rgba(0,212,255,0.25) !important;}
      button.inara-sync-menu-item:disabled{opacity:0.5 !important;cursor:not-allowed !important;}
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

  // GM_xmlhttpRequest (bypass CORS). Diagnostic: onload/onerror/ontimeout/onabort — Promise toujours résolue ou rejetée.
  function gmPost(url, data) {
    return new Promise((resolve, reject) => {
      let settled = false;
      const startMs = Date.now();
      const MAX_PREVIEW = 200;

      function finish(result, eventType, status, statusText, respPreview) {
        if (settled) return;
        settled = true;
        clearTimeout(safetyTimer);
        const elapsedMs = Date.now() - startMs;
        const preview = (respPreview || '').slice(0, MAX_PREVIEW);
        console.log('[Inara Sync][gmPost] event=', eventType, 'status=', status, 'statusText=', statusText, 'elapsedMs=', elapsedMs, 'responsePreview=', preview);
        if (result) result.gmPostDiag = { event: eventType, status, statusText, elapsedMs, responsePreview: preview };
        resolve(result || { ok: false, message: statusText || eventType, json: {}, responseText: '' });
      }

      const safetyTimer = setTimeout(() => {
        if (settled) return;
        settled = true;
        clearTimeout(safetyTimer);
        const elapsedMs = Date.now() - startMs;
        console.log('[Inara Sync][gmPost] SAFETY: aucun callback après', elapsedMs, 'ms — reject');
        reject(new Error('gmPost: aucun callback après ' + elapsedMs + ' ms'));
      }, 65000);

      GM_xmlhttpRequest({
        method: 'POST',
        url,
        headers: { 'Content-Type': 'application/json' },
        data: typeof data === 'string' ? data : JSON.stringify(data),
        timeout: 60000,
        onload: (res) => {
          const respText = res.responseText || '';
          let json = {};
          try { json = JSON.parse(respText) || {}; } catch (e) { /**/ }
          if (typeof json !== 'object') json = {};
          const ok = res.status >= 200 && res.status < 300;
          const msg = json.error || json.message || res.statusText || (ok ? 'OK' : `Erreur ${res.status}`);
          finish({ ok, message: msg, json, responseText: respText }, 'onload', res.status, res.statusText || '', respText);
        },
        onerror: (err) => {
          const st = (err && err.message) || 'Network error';
          finish({ ok: false, message: st, json: {}, responseText: '' }, 'onerror', 0, st, '');
        },
        ontimeout: () => {
          finish({ ok: false, message: 'Délai dépassé (60s).', json: {}, responseText: '' }, 'ontimeout', 0, 'Timeout', '');
        },
        onabort: () => {
          finish({ ok: false, message: 'Requête interrompue (abort).', json: {}, responseText: '' }, 'onabort', 0, 'Aborted', '');
        }
      });
    });
  }

  // ——— Contexte FACTION PRESENCE : extraction systèmes ———
  if (isFactionPresence) {
    /** Diagnostic Systems : true = envoyer 1 seul système (test volume vs champ). */
    const DEBUG_SEND_MINIMAL = false;
    /** Limite de systèmes après extraction. 0 = pas de limite. 10, 25, 50, 100, 173 pour tests volume. */
    const DEBUG_LIMIT_SYSTEMS = 0;
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

    /** Extrait les tags depuis les classes du <tr> : ctrl, noctrl, colony, nocolony. */
    function extractTagsFromRow(row) {
      const tags = [];
      const cls = (row.className || '').toLowerCase();
      if (cls.includes('ctrl')) tags.push('ctrl');
      if (cls.includes('noctrl')) tags.push('noctrl');
      if (cls.includes('colony')) tags.push('colony');
      if (cls.includes('nocolony')) tags.push('nocolony');
      return tags;
    }

    /** Mapping strict tooltip FR -> état BGS. Évite les faux positifs (proximité, etc.). */
    const TOOLTIP_TO_STATE = [
      { pattern: /le\s+conflit\s+est\s+en\s+cours/i, state: 'Conflit' },
      { pattern: /conflit\s+est\s+en\s+cours/i, state: 'Conflit' },
      { pattern: /la\s+guerre\s+civile\s+est\s+en\s+cours/i, state: 'Civil War' },
      { pattern: /guerre\s+civile\s+est\s+en\s+cours/i, state: 'Civil War' },
      { pattern: /l['']?élection\s+est\s+en\s+cours/i, state: 'Election' },
      { pattern: /élection\s+est\s+en\s+cours/i, state: 'Election' },
      { pattern: /la\s+retraite\s+est\s+en\s+cours/i, state: 'Retreat' },
      { pattern: /retraite\s+est\s+en\s+cours/i, state: 'Retreat' },
      { pattern: /l['']?expansion\s+est\s+en\s+cours/i, state: 'Expansion' },
      { pattern: /expansion\s+est\s+en\s+cours/i, state: 'Expansion' },
    ];
    const EXCLUDE_PATTERNS = [
      /influence\s+est\s+proche/i,
      /proche\s+de\s+celle\s+d['']?une\s+autre\s+faction/i,
      /5%\s*(de\s*)?différence/i,
      /différence\s+ou\s+moins/i,
    ];

    /** Extrait les états BGS depuis data-tooltiptext de la cellule Influence. Mapping strict. */
    function extractStatesFromInfluenceCellTooltips(infCellEl) {
      const tooltips = [];
      if (!infCellEl) return { tooltips, states: [] };
      const els = infCellEl.querySelectorAll('[data-tooltiptext]');
      for (const el of els) {
        const t = (el.getAttribute('data-tooltiptext') || '').trim();
        if (t) tooltips.push(t);
      }
      const states = new Set();
      for (const text of tooltips) {
        const lower = text.toLowerCase();
        let excluded = false;
        for (const re of EXCLUDE_PATTERNS) {
          if (re.test(text)) { excluded = true; break; }
        }
        if (excluded) continue;
        for (const { pattern, state } of TOOLTIP_TO_STATE) {
          if (pattern.test(text)) {
            states.add(state);
            break;
          }
        }
      }
      return { tooltips, states: Array.from(states) };
    }

    /** Retourne le tableau des systèmes (présence), ou null si introuvable. */
    function getSystemsTable() {
      const tables = document.querySelectorAll('table');
      for (const t of tables) {
        if (t.querySelector('a[href*="/elite/starsystem/"]')) return t;
      }
      return null;
    }

    /** Extrait l'origine depuis l'encart faction (au-dessus du tableau), jamais depuis le tableau paginé. */
    function extractOriginFromHeader() {
      const log = typeof console !== 'undefined' && console.log ? (...a) => console.log('[Inara Sync][Systems][Origin]', ...a) : () => {};
      const systemsTable = getSystemsTable();
      const labelTexts = ['origin', 'origine'];
      const labels = document.querySelectorAll('dt, td, th, div, span, label');
      log('extractOriginFromHeader: scanning encart uniquement (hors tableau systèmes)');
      for (const el of labels) {
        if (systemsTable && el.closest && el.closest('table') === systemsTable) continue;
        const raw = (el.textContent || '').trim();
        const rawLower = raw.toLowerCase();
        const isOriginLabel = raw && (labelTexts.includes(rawLower) || labelTexts.some(l => rawLower.startsWith(l + ' ') || rawLower.startsWith(l + ':')));
        if (isOriginLabel && !el.closest('table thead')) {
          const link = el.parentElement?.querySelector?.('a[href*="/elite/starsystem/"]')
            || el.nextElementSibling?.querySelector?.('a[href*="/elite/starsystem/"]')
            || el.closest('tr')?.querySelector?.('a[href*="/elite/starsystem/"]');
          log('Label trouvé (encart):', raw, '| link:', link ? link.href : 'NON');
          if (link) {
            const name = cleanName(link.textContent);
            if (name) {
              log('Origin détectée:', name);
              return name;
            }
          }
        }
      }
      log('Aucun label Origin/Origine trouvé dans l\'encart faction');
      return null;
    }

    /** Nom du premier système visible dans le tableau. */
    function getFirstSystemName(table) {
      const firstLink = table.querySelector('tbody tr a[href*="/elite/starsystem/"]');
      return firstLink ? cleanName(firstLink.textContent) : null;
    }

    /** Retourne le bouton Next (DOM Inara DataTables) et le nombre de pages. Ne dépend pas du texte visible. */
    function findPaginationControls(table) {
      const nextSelectors = [
        '#DataTables_Table_0_next',
        '.dataTables_paginate .paginate_button.next',
        'a.paginate_button.next[data-dt-idx="next"]',
        '.dataTables_paginate a.paginate_button.next',
        '[id$="_next"]',
      ];
      let nextBtn = null;
      let usedSelector = '';
      const wrapper = table.closest('.dataTables_wrapper') || document;
      for (const sel of nextSelectors) {
        try {
          const el = wrapper.querySelector(sel);
          if (el) {
            nextBtn = el;
            usedSelector = sel;
            break;
          }
        } catch (_) {}
      }
      if (!nextBtn) return { nextBtn: null, totalPages: 1, disabled: true, currentPage: 1, nextText: null };
      const cls = (nextBtn.className || '').toLowerCase();
      const disabled = cls.includes('disabled') ||
        nextBtn.getAttribute('aria-disabled') === 'true' ||
        nextBtn.getAttribute('tabindex') === '-1';
      const pager = nextBtn.closest('.dataTables_paginate');
      let totalPages = 1;
      if (pager) {
        const pageButtons = pager.querySelectorAll('.paginate_button[data-dt-idx]');
        const indexes = [];
        for (const b of pageButtons) {
          const idx = b.getAttribute('data-dt-idx');
          if (idx && idx !== 'previous' && idx !== 'next') {
            const n = parseInt(idx, 10);
            if (!isNaN(n)) indexes.push(n);
          }
        }
        if (indexes.length > 0) totalPages = Math.max(...indexes) + 1;
        else {
          const nums = [];
          pager.querySelectorAll('.paginate_button').forEach((p) => {
            const n = parseInt((p.textContent || '').trim(), 10);
            if (!isNaN(n) && n >= 1 && n <= 999) nums.push(n);
          });
          if (nums.length > 0) totalPages = Math.max(...nums);
        }
      }
      const currentPage = pager ? (() => {
        const cur = pager.querySelector('.paginate_button.current');
        if (cur) {
          const idx = cur.getAttribute('data-dt-idx');
          if (idx) { const n = parseInt(idx, 10); if (!isNaN(n)) return n + 1; }
          const t = parseInt((cur.textContent || '').trim(), 10);
          if (!isNaN(t)) return t;
        }
        return 1;
      })() : 1;
      return { nextBtn, totalPages, disabled, currentPage, nextText: usedSelector };
    }

    /** Retourne à la page 1 si nécessaire. Attend le changement du tableau. Retourne { ok, reason }. */
    async function goToPage1IfNeeded(table) {
      const { currentPage } = findPaginationControls(table);
      if (currentPage <= 1) return { ok: true, reason: null };
      const pager = table.closest('.dataTables_wrapper')?.querySelector('.dataTables_paginate');
      if (!pager) return { ok: false, reason: 'pager not found' };
      const prev = getFirstSystemName(table);
      const page1Btn = pager.querySelector('.paginate_button[data-dt-idx="0"]')
        || Array.from(pager.querySelectorAll('.paginate_button')).find((b) => (b.textContent || '').trim() === '1');
      if (page1Btn) {
        page1Btn.click();
      } else {
        const prevBtn = pager.querySelector('.paginate_button.previous, .paginate_button[data-dt-idx="previous"]');
        if (!prevBtn) return { ok: false, reason: 'no page1 or previous button' };
        for (let i = 0; i < currentPage - 1; i++) {
          prevBtn.click();
          await new Promise((r) => setTimeout(r, 150));
        }
      }
      const { changed } = await waitForTableChange(table, prev);
      if (!changed) return { ok: false, reason: 'table did not change' };
      await new Promise((r) => setTimeout(r, 75));
      return { ok: true, reason: null };
    }

    /** Attend que le premier système change (max 6s, poll 80ms). Retourne { changed, newFirst }. */
    function waitForTableChange(table, previousFirstSystem) {
      return new Promise((resolve) => {
        const start = Date.now();
        const maxMs = 6000;
        const pollMs = 80;
        const check = () => {
          const current = getFirstSystemName(table);
          const timedOut = Date.now() - start > maxMs;
          if (current !== previousFirstSystem || timedOut) {
            resolve({ changed: current !== previousFirstSystem, newFirst: current });
            return;
          }
          setTimeout(check, pollMs);
        };
        check();
      });
    }

    /** Clique sur Next et attend le changement du contenu. Retourne { ok, reason }. */
    async function goToNextPage(table, pageNum) {
      const prev = getFirstSystemName(table);
      const { nextBtn, disabled } = findPaginationControls(table);
      if (!nextBtn) return { ok: false, reason: 'next not found' };
      if (disabled) return { ok: false, reason: 'next disabled' };
      nextBtn.click();
      const { changed } = await waitForTableChange(table, prev);
      if (!changed) return { ok: false, reason: 'table did not change' };
      await new Promise((r) => setTimeout(r, 75));
      return { ok: true, reason: null };
    }

    function parseDataRowsFromTable(table, indices, seen) {
      const allRows = Array.from(table.querySelectorAll('tr'));
      const dataRows = allRows.filter((r) => r.querySelector('a[href*="/elite/starsystem/"]'));
      const systems = [];
      const { iG, iA, iP, iPop, iF, iS, iI, iU } = indices;
      for (const row of dataRows) {
        const cells = Array.from(row.querySelectorAll('th, td'));
        if (cells.length < 2) continue;
        const link = row.querySelector('a[href*="/elite/starsystem/"]');
        if (!link) continue;
        const name = cleanName(link.textContent);
        if (!name || seen.has(name.toLowerCase())) continue;
        seen.add(name.toLowerCase());
        const href = link.getAttribute('href');
        const inaraUrl = href ? (href.startsWith('http') ? href : 'https://inara.cz' + (href.startsWith('/') ? '' : '/') + href) : undefined;
        const get = (i) => (i >= 0 && cells[i] ? (cells[i].textContent || '').trim() : null);
        const infCellEl = iI >= 0 && cells[iI] ? cells[iI] : null;
        const tags = extractTagsFromRow(row);
        const influencePercent = parsePercent(get(iI));
        const { tooltips: rawTooltips, states: extractedStates } = extractStatesFromInfluenceCellTooltips(infCellEl);
        const system = {
          name,
          inaraUrl,
          government: get(iG),
          allegiance: get(iA),
          power: cells[iP] ? extractPower(cells[iP]) : null,
          population: parseLong(get(iPop)),
          factionCount: (() => { const v = get(iF); return v ? (parseInt(v.replace(/\D/g, ''), 10) || null) : null; })(),
          stationCount: (() => { const v = get(iS); return v ? (parseInt(v.replace(/\D/g, ''), 10) || null) : null; })(),
          influencePercent,
          influenceDelta72h: undefined,
          states: extractedStates.length ? extractedStates : undefined,
          tags: tags.length ? tags : undefined,
          lastUpdatedText: parseUpdated(get(iU)),
          category: 'Guild',
          isClean: false,
        };
        systems.push(system);
      }
      return systems;
    }

    async function extractSystems() {
      console.log('[Inara Sync][Systems][DIAG] extraction START');
      const targetTable = getSystemsTable();
      if (!targetTable) {
        console.log('[Inara Sync][Systems][DIAG] extraction DONE (error: table absente)');
        return { systems: [], error: 'Table de présence introuvable' };
      }

      const linesPerPage = Array.from(targetTable.querySelectorAll('tbody tr')).filter((r) => r.querySelector('a[href*="/elite/starsystem/"]')).length;
      const allRows = Array.from(targetTable.querySelectorAll('tr'));
      const headerRow = targetTable.querySelector('thead tr') || allRows[0];
      const headerCells = headerRow ? Array.from(headerRow.querySelectorAll('th, td')).map((c) => (c.textContent || '').trim()) : [];
      const idxGov = findColumnIndex(headerCells, 'government', 'gov');
      const idxAlleg = findColumnIndex(headerCells, 'allegiance', 'alleg');
      const idxPower = findColumnIndex(headerCells, 'power');
      const idxPop = findColumnIndex(headerCells, 'population', 'pop');
      const idxInf = findColumnIndex(headerCells, 'influence', 'presence');
      const idxUpd = findColumnIndex(headerCells, 'updated');
      const idxDelta = findColumnIndex(headerCells, 'change', 'variation', 'delta', 'trend');
      const indices = {
        iG: idxGov >= 0 ? idxGov : 1,
        iA: idxAlleg >= 0 ? idxAlleg : 2,
        iP: idxPower >= 0 ? idxPower : 3,
        iPop: idxPop >= 0 ? idxPop : 4,
        iF: idxPop >= 0 ? idxPop + 1 : 5,
        iS: idxPop >= 0 ? idxPop + 2 : 6,
        iI: idxInf >= 0 ? idxInf : 7,
        iU: idxUpd >= 0 ? idxUpd : 8,
        iD: idxDelta,
      };

      const systems = [];
      const seen = new Set();
      const { nextBtn, totalPages: detectedPages } = findPaginationControls(targetTable);
      const totalPages = Math.max(1, detectedPages);

      const usePagination = nextBtn && totalPages > 1;

      if (usePagination) {
        const goTo1 = await goToPage1IfNeeded(targetTable);
        if (!goTo1.ok) {
          return { systems: [], error: 'Impossible de revenir à la page 1 (extraction incomplète évitée)' };
        }
        let pagesTraversed = 0;
        const maxPages = Math.min(totalPages, 20);
        for (let p = 1; p <= maxPages; p++) {
          const pageSystems = parseDataRowsFromTable(targetTable, indices, seen);
          systems.push(...pageSystems);
          pagesTraversed = p;
          if (p < maxPages) {
            const result = await goToNextPage(targetTable, p);
            if (!result.ok) break;
          }
        }
      } else {
        systems.push(...parseDataRowsFromTable(targetTable, indices, seen));
      }
      console.log('[Inara Sync][Systems][DIAG] extraction DONE', systems.length, 'systèmes');
      return { systems, error: null };
    }

    async function postSystems(payload, diag) {
      const url = `${BACKEND_URL.replace(/\/$/, '')}/api/guild/systems/import`;
      const payloadStr = JSON.stringify(payload);
      console.log('[Inara Sync][Systems][DIAG] payload bytes=', payloadStr.length);
      console.log('[Inara Sync][Systems][DIAG] firstSystem JSON=', JSON.stringify(payload.systems?.[0], null, 2));
      if (diag) console.log('[Inara Sync][Systems][DIAG] isSystemsSubmitting avant post=', diag.submitting);
      console.log('[Inara Sync][Systems][DIAG] postSystems START → gmPost');
      console.log('[Inara Sync][Systems] postMessage START skipped');
      let r;
      try {
        r = await gmPost(url, payload);
        console.log('[Inara Sync][Systems][DIAG] gmPost returned');
      } catch (e) {
        console.log('[Inara Sync][Systems][DIAG] gmPost threw', e?.message || e);
        return { ok: false, message: (e && e.message) || 'Erreur gmPost', json: {} };
      }
      console.log('[Inara Sync][Systems][DIAG] gmPost ok=', r.ok, 'gmPostDiag=', r.gmPostDiag, 'response raw(200)=', (r.responseText || '').slice(0, 200));
      if (diag) console.log('[Inara Sync][Systems][DIAG] isSystemsSubmitting après réponse=', diag.submitting);
      if (!r.ok) return { ok: false, message: r.message, json: {} };
      const j = r.json ?? {};
      const inserted = j.inserted ?? 0;
      const updated = j.updated ?? 0;
      const skipped = j.skipped ?? 0;
      const deleted = j.deleted ?? 0;
      const totalReceived = j.totalReceived ?? 0;
      const msg = `Importé : ${inserted} insérés, ${updated} mis à jour, ${skipped} ignorés (${totalReceived} reçus)`;
      return { ok: true, message: msg, json: j };
    }

    async function runSystems(mode, limitSystems) {
      let isSystemsSubmitting = false;
      console.log('[Inara Sync][Systems][DIAG] isSystemsSubmitting au clic=', isSystemsSubmitting);
      showToast('Extraction en cours (chargement de toutes les pages)…');
      const { systems, error } = await extractSystems();
      if (error) { showToast(error, true); return; }
      if (systems.length === 0) {
        showToast('Aucun système extrait. Vérifiez que la page affiche le tableau de présence.', true);
        return;
      }
      const forceMinimal = mode === 'post-minimal';
      const effectiveLimit = limitSystems ?? (DEBUG_LIMIT_SYSTEMS > 0 ? DEBUG_LIMIT_SYSTEMS : 0);
      let systemsToSend = systems;
      if (forceMinimal || (DEBUG_SEND_MINIMAL && systems.length > 0)) systemsToSend = systems.slice(0, 1);
      else if (effectiveLimit > 0) systemsToSend = systems.slice(0, effectiveLimit);
      const originSystemName = forceMinimal ? 'HIP 4332' : extractOriginFromHeader();
      const payload = { originSystemName: originSystemName || undefined, systems: systemsToSend };
      console.log('[Inara Sync][Systems][DIAG] payload BUILD START');
      console.log('[Inara Sync][Systems][DIAG] payload BUILD DONE', 'extracted=', systems.length, 'sending=', systemsToSend.length, 'limit=', effectiveLimit || 'none');
      if (forceMinimal) console.log('[Inara Sync][Systems] TEST A: originSystemName=HIP 4332, 1 système');
      if (mode === 'download') {
        const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `guild-systems-inara-${new Date().toISOString().slice(0, 10)}.json`;
        a.click();
        URL.revokeObjectURL(a.href);
        showToast(`${systems.length} système(s) téléchargé(s)`);
      } else {
        isSystemsSubmitting = true;
        console.log('[Inara Sync][Systems][DIAG] isSystemsSubmitting avant post=', isSystemsSubmitting);
        showToast(forceMinimal ? 'Test A minimal…' : 'Envoi en cours…');
        const diag = { submitting: true };
        postSystems(payload, diag)
          .then((r) => {
            console.log('[Inara Sync][Systems][DIAG] postSystems SUCCESS ok=', r.ok);
            showToast(r.message, !r.ok);
          })
          .catch((err) => {
            console.log('[Inara Sync][Systems][DIAG] postSystems ERROR', err);
            const msg = (err && err.message) || String(err) || 'Erreur inattendue';
            showToast('Erreur : ' + msg, true);
          })
          .finally(() => {
            isSystemsSubmitting = false;
            diag.submitting = false;
            console.log('[Inara Sync][Systems][DIAG] after postSystems (finally) isSystemsSubmitting=', isSystemsSubmitting);
          });
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
      btn.onclick = (e) => { e.stopPropagation(); const m = document.getElementById('inara-sync-faction-menu'); m.classList.toggle('inara-sync-menu--open'); };

      const menu = document.createElement('div');
      menu.id = 'inara-sync-faction-menu';
      menu.className = 'inara-sync-menu';

      const dl = document.createElement('button');
      dl.className = 'inara-sync-menu-item';
      dl.textContent = 'Télécharger JSON';
      dl.onclick = (e) => { e.stopPropagation(); runSystems('download'); menu.classList.remove('inara-sync-menu--open'); };

      const post = document.createElement('button');
      post.className = 'inara-sync-menu-item';
      post.textContent = 'Envoyer au backend';
      post.onclick = (e) => { e.stopPropagation(); runSystems('post'); menu.classList.remove('inara-sync-menu--open'); };

      menu.appendChild(dl);
      menu.appendChild(post);
      container.appendChild(btn);
      container.appendChild(menu);
      document.body.appendChild(container);
    }
    if (autoImport) {
      setTimeout(async () => {
        console.log('[Inara Sync][Systems] postMessage START skipped');
        showToast('Extraction en cours (chargement de toutes les pages)…');
        const { systems, error } = await extractSystems();
        if (error) {
          showToast(error, true);
          safePostMessageSystems('inara-sync-error', { message: error });
          return;
        }
        if (systems.length === 0) {
          const msg = 'Aucun système extrait. Vérifiez que la page affiche le tableau de présence.';
          showToast(msg, true);
          safePostMessageSystems('inara-sync-error', { message: msg });
          return;
        }
        const originSystemName = extractOriginFromHeader();
        const systemsToSend = DEBUG_SEND_MINIMAL && systems.length > 0 ? systems.slice(0, 1) : systems;
        showToast('Envoi en cours…');
        postSystems({ originSystemName: originSystemName || undefined, systems: systemsToSend })
          .then((r) => {
            if (!r.ok) {
              showToast(r.message, true);
              safePostMessageSystems('inara-sync-error', { message: r.message });
              return;
            }
            showToast(r.message);
            const j = r.json ?? {};
            safePostMessageSystems('inara-sync-success', {
              detail: {
                inserted: j.inserted ?? 0,
                updated: j.updated ?? 0,
                total: j.totalReceived ?? systems.length,
                edsm: j.edsm,
              },
            });
            window.close();
          })
          .catch((err) => {
            const msg = (err && err.message) || String(err) || 'Erreur inattendue';
            showToast('Erreur : ' + msg, true);
            safePostMessageSystems('inara-sync-error', { message: msg });
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
      btn.onclick = (e) => { e.stopPropagation(); const m = document.getElementById('inara-sync-roster-menu'); m.classList.toggle('inara-sync-menu--open'); };

      const menu = document.createElement('div');
      menu.id = 'inara-sync-roster-menu';
      menu.className = 'inara-sync-menu';

      const dl = document.createElement('button');
      dl.className = 'inara-sync-menu-item';
      dl.textContent = 'Télécharger JSON';
      dl.onclick = (e) => { e.stopPropagation(); runCommanders('download'); menu.classList.remove('inara-sync-menu--open'); };

      const post = document.createElement('button');
      post.className = 'inara-sync-menu-item';
      post.textContent = 'Envoyer au backend';
      post.onclick = (e) => { e.stopPropagation(); runCommanders('post'); menu.classList.remove('inara-sync-menu--open'); };

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
