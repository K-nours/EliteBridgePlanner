# Intégration Inara

Documentation de l'intégration avec Inara.cz pour le Guild Dashboard 501.

---

## Faction presence (BGS) — source Guild Systems

### Contexte

- Elite BGS API abandonnée (timeouts systématiques, inutilisable).
- Données BGS récupérées via **scraping** de la page présence :
  ```
  https://inara.cz/elite/minorfaction-presence/{InaraFactionId}/
  ```

### Données extraites

- **systemName** : nom du système (lien `/elite/starsystem/{id}`)
- **influence** : pourcentage (regex `\d+(?:\.\d+)?\s*%`)
- **lastUpdateText** : ex. "5 days ago" (non parsé en date)

### Limites

- **State BGS** (War, Boom, etc.) : non présent sur la page → `null`
- **IsControlled / IsThreatened** : non calculables (pas de breakdown par faction) → `false`
- **InfluenceDelta24h** : non disponible → `null`

### Fragilité du scraping

- Structure HTML non contractuelle — tout changement DOM cassera le parsing
- **Protection anti-bot Inara** : les requêtes HTTP server-side reçoivent une page "Access check required" (challenge manuel) au lieu du contenu. Le scraping ne fonctionne pas sans exécution JavaScript / navigateur réel.

### Limitation bloquante

Inara bloque les clients non-navigateur. Une requête `curl` ou `HttpClient` reçoit :
```html
<title>INARA - something happened!</title>
<h2>Access check required</h2>
We just need to make sure you're a real visitor and not a bot.
```

→ **Le scraping Inara pour BGS n'est pas utilisable en production** sans headless browser (Puppeteer, Playwright) ou résolution du challenge. Alternative à investiguer : EDSM, API officielle Frontier.

---

## Squadron roster (CMDRs)

### Contexte

- **L'API Inara ne fournit pas d'endpoint** pour récupérer le roster d'un squadron.
- Le roster est récupéré via **scraping HTML** de :
  ```
  https://inara.cz/elite/squadron-roster/{id}
  ```

### Limites identifiées

- Dépend de la **visibilité publique** du roster
- Contenu différent entre utilisateur connecté et anonyme
- Parsing fragile (structure HTML **non contractuelle**)
- Pas de garantie de stabilité

### État actuel

- Scraping implémenté dans `InaraSquadronRosterService`
- Aucun membre récupéré pour le squadron configuré
- Cause probable : visibilité restreinte ou structure HTML différente

### Décision

- **Suspendre** cette intégration pour le moment
- Ne pas bloquer le reste du dashboard

### Alternatives futures

- Roster local géré en base
- Enrichissement via API Inara (`getCommanderProfile`)
- Intégration Frontier (source officielle)
