# Archi — Chantiers de colonisation

Deux panneaux complémentaires pour le suivi des chantiers de colonisation : un panneau de liste des chantiers actifs, et un panneau de logistique des ressources à livrer.

## Ce que ça fait

**Chantiers en cours** (`chantiers-debug-panel`)
- Liste les chantiers actifs déclarés par les membres de l'escadron
- Regroupe par système, distingue "mes chantiers" / "chantiers des autres"
- Permet de déclarer un nouveau chantier depuis le jeu (nécessite d'être docké sur une station de construction)
- Permet de rafraîchir l'état d'un chantier via l'API Frontier

**Logistique Chantier** (`chantier-logistics-panel`)
- Affiche les ressources manquantes pour chaque chantier
- Compare l'inventaire du vaisseau (via CAPI Frontier) aux besoins du chantier
- Indique ce qui peut être livré immédiatement

## Flux de données

```
API Frontier (CAPI /market, /fleetcarrier, /journal)
       ↓
  FrontierChantiersInspectAnalyzer
  FrontierLogisticsInventoryService
       ↓
  DeclaredChantiersService → SQL Server
       ↓
  GET /api/dashboard/chantiers
       ↓
  ActiveChantiersStore (Angular signal store)
       ↓
  chantiers-debug-panel + chantier-logistics-panel
```

## Composants clés

**Frontend**
- `chantiers-debug-panel` — liste, déclaration, refresh par chantier
- `chantier-logistics-panel` — inventaire vs besoins, sync marché
- `ActiveChantiersStore` — état global des chantiers (signal store Angular)

**Backend**
- `DeclaredChantiersService` — CRUD des chantiers déclarés
- `FrontierChantiersInspectAnalyzer` — analyse de l'état d'avancement via le journal Frontier
- `FrontierLogisticsInventoryService` — calcul des besoins vs inventaire
- `FrontierMarketBusinessParser` — parsing des données marché Frontier

## Notes

- La déclaration d'un chantier nécessite d'être docké sur une station de construction et connecté via OAuth Frontier
- Les chantiers sont liés à un utilisateur Frontier (FrontierUser) et à un système
