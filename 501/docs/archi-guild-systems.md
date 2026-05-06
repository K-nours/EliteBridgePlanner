# Archi — Systèmes Faction

Panneau principal du dashboard. Affiche les systèmes contrôlés par l'escadron, leur influence BGS et leur état de santé.

## Ce que ça fait

- Liste les systèmes par catégorie : Origine, QG, Surveillance, Conflits, Critique, Bas, Sains, Autres
- Affiche l'influence et la tendance sur 72h (delta calculé via EDSM)
- Permet de filtrer par catégorie ou par recherche textuelle
- Permet de marquer un système comme QG (déclaratif)
- Détecte les systèmes sans signal Inara récent

## Flux de données

```
EliteBGS API + EDSM API
       ↓
  BgsSyncService (backend)
       ↓
  SQL Server (ControlledSystems)
       ↓
  GET /api/guild/systems
       ↓
  GuildSystemsSyncService (Angular)
       ↓
  guild-systems-panel
```

## Composants clés

**Frontend**
- `guild-systems-panel` — affichage, filtres, scroll, hover
- `GuildSystemsSyncService` — état global des systèmes (signal Angular)

**Backend**
- `GuildSystemsService` — lecture DB
- `BgsSyncService` — sync depuis EliteBGS + enrichissement EDSM
- `EdsmDeltaEnrichmentService` — calcul tendance 72h
- `GuildController` — endpoints REST

## Notes

- Les systèmes peuvent apparaître dans plusieurs catégories (ex : un système en conflit peut aussi être critique)
- Le seed (`guild-systems.seed.json`) permet de pré-charger des systèmes sans sync réseau
- L'indicateur "seed / cached" est affiché dans le panneau pour signaler la fraîcheur des données
