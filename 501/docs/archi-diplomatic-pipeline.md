# Archi — Menaces Diplomatiques

Panneau de surveillance diplomatique. Identifie les systèmes où une faction adverse a une influence suffisante pour représenter une menace politique.

## Ce que ça fait

- Liste les systèmes où une faction non-alliée dépasse un seuil d'influence configuré
- Regroupe les menaces par faction adverse
- Affiche le niveau d'influence de la faction dans chaque système
- Permet de consulter les infos de la faction sur Inara (sync manuelle)

## Flux de données

```
GuildSystemsSyncService (systèmes déjà chargés)
       ↓
  DiplomaticPipelineService (backend)
       ↓
  GET /api/dashboard/diplomatic-pipeline
       ↓
  diplomatic-pipeline-panel (Angular)
```

Le panneau s'appuie sur les données de systèmes déjà synchronisées — il ne fait pas de requête réseau propre vers EliteBGS.

## Composants clés

**Frontend**
- `diplomatic-pipeline-panel` — affichage groupé par faction, expand/collapse
- Chaque groupe peut afficher les infos Inara de la faction (sync à la demande)

**Backend**
- `DiplomaticPipelineService` — calcul des menaces à partir des données BGS en base
- `InaraFactionService` — récupération des infos faction sur Inara

## Notes

- Les seuils d'influence qui définissent une "menace" sont configurés dans `InfluenceThresholds`
- Les infos Inara par faction sont fetchées à la demande (pas en cache permanent)
