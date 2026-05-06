# Intégration EDSM – R&D Guild Systems

Étude technique ciblée sur l’utilisation d’EDSM (Elite Dangerous Star Map) comme source de données pour le panneau Guild Systems.

---

## Contexte et objectif

**Validations antérieures :**
- Frontier : OK (identité joueur, CAPI)
- Inara avatar API : bloqué (autorisation application)
- Elite BGS : instable (timeouts)
- EDDN : utile en complément, pas comme source BGS principale

**Objectif de cette étude :** déterminer si EDSM peut fournir (directement ou indirectement) les données suivantes pour une faction :

- liste des systèmes
- influence par système
- stations contrôlées
- état BGS / contrôle
- données suffisantes pour alimenter le panneau Guild Systems

---

## Statut technique synthétique

| Besoin Guild Systems   | EDSM fournit ? | Détail |
|------------------------|----------------|--------|
| Liste des systèmes    | ❌ Non         | Pas d’API faction → systèmes. Nécessite liste connue en amont. |
| Influence par système | ❌ Non         | API ne retourne que la faction contrôlante, pas les %. |
| Stations contrôlées   | ❌ Non         | Non exposé dans l’API Systems. |
| État BGS (factionState) | ✅ Oui       | Uniquement pour la faction contrôlante du système. |
| Contrôle (IsControlled) | ✅ Partiel   | Dérivable si `faction == FactionName` pour les systèmes connus. |

---

## 1. API EDSM côté public

### Endpoints documentés

| Endpoint | Description | Faction / BGS |
|----------|-------------|---------------|
| `GET /api-v1/system` | Info d’un système par nom | `information.faction`, `information.factionState` |
| `GET /api-v1/systems` | Info de plusieurs systèmes (noms ou préfixe) | Idem |
| `GET /api-v1/sphere-systems` | Systèmes dans une sphère (rayon) | Idem |
| `GET /api-v1/cube-systems` | Systèmes dans un cube | Idem |
| `GET /api-commander-v1/*` | Données commandant (ranks, credits, etc.) | ❌ Non pertinent |
| `GET /api-logs-v1/*` | Logs de vol commandant | ❌ Non pertinent |
| `GET /api-v1/faction` | Référencé dans les pages, **404 en pratique** | N/A |

### Données système exposées avec `showInformation=1`

```json
{
  "name": "HIP 4332",
  "information": {
    "allegiance": "Empire",
    "government": "Feudal",
    "faction": "The 501st Guild",
    "factionState": "None",
    "population": 121797,
    "security": "Medium",
    "economy": "Colony",
    "secondEconomy": "Extraction",
    "reserve": "Common"
  }
}
```

**Important :** `faction` désigne **uniquement la faction contrôlante** du système. Aucun champ `influence` ni liste des factions présentes avec leurs %.

---

## 2. Tests effectués

Date des tests : 21 mars 2025. EDSM affichait une charge serveur très élevée (~4 300 %).

### 2.1 The 501st Guild (faction projet)

| Test | URL / paramètres | HTTP | Temps | Résultat |
|------|------------------|------|-------|----------|
| Système Hip 4332 | `GET /api-v1/system?systemName=Hip%204332&showInformation=1` | 200 | ~0,59 s | `faction: "The 501st Guild"`, `factionState: "None"` |
| Système Mayang | `GET /api-v1/system?systemName=Mayang&showInformation=1` | 200 | ~0,59 s | `faction: "The 501st Guild"`, `factionState: "None"` |
| Système Sabines | `GET /api-v1/system?systemName=Sabines&showInformation=1` | 200 | OK | `faction: "The 501st Guild"` |
| Système Achuar | `GET /api-v1/system?systemName=Achuar&showInformation=1` | 200 | ~0,90 s | `faction: "Brian Solutions"` — **pas The 501st** (présent à ~1,5 % en seed) |

**Conclusion The 501st :** EDSM renvoie correctement la faction contrôlante. Pour Achuar, la faction projet n’est pas contrôlante → EDSM ne l’expose pas, aucune donnée d’influence disponible.

### 2.2 Systèmes multiples (batch)

| Test | URL | HTTP | Temps | Résultat |
|------|-----|------|-------|----------|
| Batch Hip 4332, Mayang, Sabines | `GET /api-v1/systems?systemName[]=Hip+4332&systemName[]=Mayang&systemName[]=Sabines&showInformation=1` | 200 | ~0,77 s | Tableau JSON avec les 3 systèmes, faction et factionState pour chacun. |

### 2.3 Faction populaire (Sol)

| Test | URL | HTTP | Temps | Résultat |
|------|-----|------|-------|----------|
| Sol | `GET /api-v1/system?systemName=Sol&showInformation=1` | 200 | ~0,50 s | `faction: "Mother Gaia"`, `factionState: "None"` |

Comportement identique : pas de problème spécifique à notre faction.

### 2.4 Endpoints / pages non fonctionnels

| Test | URL | HTTP | Résultat |
|------|-----|------|----------|
| API faction | `GET /api-v1/faction`, `GET /api-v1/faction?factionName=The%20501st%20Guild` | 404 | Page « Page not found » (HTML). Endpoint inexistant ou désactivé. |
| Page faction web | `GET /en/faction/name/The%20501st%20Guild` | 502 | Bad Gateway (charge serveur). |
| Recherche factions | `GET /en_GB/search/factions` | 502 | Bad Gateway. |
| Nightly Dumps | `GET /en/nightly-dumps` | 502 | Bad Gateway. |
| Embed faction | `GET /en_GB/tools/embed?query=faction::The%20501st%20Guild` | 200 | HTML « Unauthorized referral » — widgets réservés aux référents approuvés. |

---

## 3. Données vérifiées pour Guild Systems

| Donnée | Disponible EDSM ? | Commentaire |
|--------|-------------------|-------------|
| Récupération d’une minor faction par nom | ❌ Non | Pas d’API. Page web 502. |
| Liste des systèmes liés à une faction | ❌ Non | Nécessite liste connue (ex. GuildSystem). |
| Influence associée | ❌ Non | Jamais exposée dans l’API Systems. |
| Stations contrôlées | ❌ Non | Non présent dans la réponse. |
| État / factionState / contrôle | ✅ Oui (partiel) | `factionState` pour la faction contrôlante. `IsControlled` dérivable si `faction == FactionName`. |

---

## 4. Clé API EDSM

**Utilité pour Guild Systems :** **aucune**.

La clé API EDSM est utilisée uniquement pour :
- API Commander v1 : rangs, credits, materials (données joueur privées)
- API Logs v1 : logs de vol, position, commentaires (données joueur privées)

L’API Systems v1 (system, systems, sphere-systems, cube-systems) **ne requiert pas de clé** et ne propose pas d’endpoints additionnels avec clé pour les factions ou l’influence.

**Conclusion :** une clé EDSM n’apporte rien pour le besoin Guild Systems (BGS, faction, influence).

---

## 5. Limites et risques

1. **Modèle système-centré, pas faction-centré** : EDSM est orienté « système → faction contrôlante ». Il n’y a pas d’API « faction → systèmes ».
2. **Pas d’influence** : les pourcentages d’influence par faction ne sont jamais exposés.
3. **Charge serveur** : lors des tests, charge à ~4 300 %, pages faction/dumps en 502. Risque de délais ou indisponibilité.
4. **Factions non contrôlantes** : si la faction projet a une présence mais ne contrôle pas le système, EDSM ne fournit aucune donnée pour ce système.
5. **Normalisation des noms** : « Hip 4332 » vs « HIP 4332 » — EDSM normalise (testé OK).

---

## 6. Verdict final par niveau d’usage

| Niveau | Verdict | Justification |
|--------|---------|---------------|
| **Source principale** | ❌ Non | Pas d’influence, pas de liste systèmes par faction, pas de stations contrôlées. |
| **Source complémentaire** | ✅ Oui (limité) | Pour une liste de systèmes déjà connue (GuildSystem), EDSM peut fournir : `IsControlled` (faction == FactionName), `State` (factionState). Permet de vérifier contrôle et état. |
| **Non exploitable** | Non | L’API Systems fonctionne et peut être utilisée en complément. |

---

## 7. Recommandation pour Guild Systems

**Usage recommandé :** source complémentaire uniquement.

**Scénario d’intégration possible :**
1. Conserver la liste des systèmes en base (GuildSystem).
2. Appeler `GET /api-v1/systems` avec `systemName[]` pour chaque système connu.
3. Pour chaque système : si `information.faction == Guild.FactionName` → `IsControlled = true`, `State = information.factionState`.
4. Si `faction != FactionName` → `IsControlled = false` ; `InfluencePercent` reste non fourni (null ou valeur seed).
5. Pas de stations contrôlées, pas d'influence ; `InfluenceDelta72h` provient de l'enrichissement EDSM (voir §8).

**Données non couvertes par EDSM :**
- `InfluencePercent`
- `InfluenceDelta72h` (calculé par enrichissement EDSM, voir §8)
- Stations contrôlées
- Liste des systèmes d’une faction (nécessite liste externe)

---

## 8. API Factions (tendance d'influence, delta 72h)

L’endpoint `api-system-v1/factions?systemName=X&showHistory=1` fournit `influence` + `influenceHistory` pour calculer la variation d'influence.

### Fenêtre de calcul : 72h (3 jours)

L'affichage EDSM sur les pages système (triangle vert/rouge, ex. « +4,573 % ») utilise une fenêtre d'environ **72 heures**, pas 24h. L'implémentation actuelle a été alignée sur ce comportement :

| Fenêtre | Problème | Résultat |
|---------|----------|----------|
| 24h | Les données BGS sont en ticks quotidiens. Une fenêtre 24h tombe souvent sur le **même tick** → delta = 0 alors qu'EDSM affiche une tendance nette. | ❌ Désalignement avec EDSM |
| **72h** | Couvre plusieurs ticks, capture la variation réelle. Correspond à l'affichage EDSM. | ✅ Cohérence avec l'UI EDSM |

**Exemple (NGC 6357 Sector AV-Y c35, mars 2026) :** avec 24h → 31,2 % − 31,2 % = 0 %. Avec 72h → 31,2 % − 26,6 % = **+4,6 %** (identique à EDSM).

Le champ en base est `InfluenceDelta72h` (aligné avec la nomenclature métier).

### Batch et implémentation

**Batch multi-systèmes :** ❌ Non supporté. Un seul `systemName` par requête. Une requête avec `systemName[]=A&systemName[]=B` ne retourne qu’un seul système.

**Implémentation actuelle :** mode batché — N requêtes en parallèle par batch (ex. 20), puis délai entre batchs. 173 systèmes → ~9 batchs de 20 au lieu de 173 appels séquentiels. Voir `EdsmDeltaEnrichmentService`.

---

## 9. Références

- [EDSM API Systems v1](https://www.edsm.net/en/api-v1)
- [EDSM API Commander v1](https://www.edsm.net/en/api-commander-v1)
- [EDSM Embeddable widgets](https://www.edsm.net/en_GB/tools/embed/examples)
- [GUILD-SYSTEMS.md](./GUILD-SYSTEMS.md) — contexte panneau et besoins actuels
