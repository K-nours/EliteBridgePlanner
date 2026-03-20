# Intégration Inara

Documentation de l'intégration avec Inara.cz pour le Guild Dashboard 501.

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
