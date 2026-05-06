# Archi — CMDRs de l'escadron

Panneau listant les membres actifs de l'escadron. Alimenté par le roster Inara (scraping HTML) et les profils Frontier.

## Ce que ça fait

- Affiche les membres du squadron avec leur nom, rang et avatar
- Indique les membres récemment actifs (via le journal Frontier si disponible)
- Permet de déclencher une sync manuelle du roster Inara

## Flux de données

```
Inara (page HTML publique /elite/squadron-roster/{id})
       ↓
  InaraSquadronRosterService (scraping)
       ↓
  SQL Server (SquadronMembers, SquadronSnapshots)
       ↓
  GET /api/dashboard/commanders
       ↓
  cmdrs-panel (Angular)
```

## Composants clés

**Frontend**
- `cmdrs-panel` — liste paginée, avatars, menu contextuel

**Backend**
- `CommandersService` — lecture des membres en base
- `SquadronSyncService` — orchestration de la sync Inara
- `InaraSquadronRosterService` — scraping de la page HTML Inara
- `SyncController` — endpoints de déclenchement

## État actuel

> ⚠️ **Intégration suspendue.** Le roster Inara n'est pas accessible via l'API officielle. Le scraping HTML est fragile et dépend de la visibilité publique du roster. Voir [INTEGRATION-INARA.md](INTEGRATION-INARA.md).

- Si le roster est privé sur Inara → 0 membres renvoyés (comportement normal)
- L'`InaraSquadronId` est configuré statiquement (voir TODO dans le README)
