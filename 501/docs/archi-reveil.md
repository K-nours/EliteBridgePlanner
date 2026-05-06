# Archi — Opération Réveil

Panneau de suivi des systèmes qui n'ont pas reçu de mise à jour BGS récente sur Inara. Son rôle est de rappeler aux membres quels systèmes ont besoin d'une visite pour soumettre des nouvelles.

## Ce que ça fait

- Liste tous les systèmes contrôlés dont la dernière mise à jour Inara date de ≥ 5 jours (ou est inconnue)
- Trie par ancienneté décroissante (les plus "endormis" en premier, les inconnus en tête)
- Colore l'âge selon le niveau d'urgence : normal → jaune (6j+) → orange (15j+) → rouge (30j+)
- Permet de copier le nom du système dans le presse-papier
- Permet d'ouvrir le système directement sur Inara

## Flux de données

```
GuildSystemsSyncService (systèmes déjà chargés)
       ↓
  computed() dans DashboardComponent
       ↓
  reveilSystems (signal Angular)
       ↓
  reveil-panel
```

Le panneau ne fait aucun appel réseau propre. Il dérive ses données des systèmes déjà synchronisés par le panneau Systèmes Faction.

## Règle de filtrage

```typescript
.filter(item => item.ageDays === null || item.ageDays >= 5)
```

- `ageDays === null` : date de dernière mise à jour inconnue (jamais mis à jour ou donnée absente)
- `ageDays >= 5` : 5 jours ou plus sans nouvelle soumission sur Inara

Les systèmes seed (`isFromSeed`) et les doublons de nom sont exclus.

## Composants clés

**Frontend**
- `reveil-panel` — liste scrollable, badge d'âge coloré, copie nom, lien Inara
- Calcul dans `DashboardComponent` via `computed()` (signal dérivé)

**Backend**
- Pas de service dédié — utilise `lastUpdated` déjà présent sur les systèmes BGS
- `inara-freshness.util.ts` — calcul de l'âge en jours depuis `lastUpdated`
