# Archi — Backend & API

API ASP.NET Core (.NET 10) qui sert de couche intermédiaire entre le frontend Angular, la base de données et les API externes (EliteBGS, EDSM, Inara, Frontier CAPI).

## Structure

```
server/
├── Controllers/     — endpoints REST
├── Services/        — logique métier, appels API externes
├── DTOs/            — objets de transfert (entrée/sortie API)
├── Models/          — entités EF Core
├── Data/            — DbContext, migrations, seed
├── Integrations/    — EDDN (listener temps réel)
└── Program.cs       — bootstrap, DI, middleware
```

## Contrôleurs

| Contrôleur | Rôle |
|------------|------|
| `DashboardController` | Données agrégées du dashboard (commanders, pipeline diplomatique) |
| `GuildController` | Gestion de la guilde et de ses systèmes |
| `SyncController` | Déclenchement des syncs (Inara, BGS) |
| `FrontierController` | Auth OAuth Frontier + profil CMDR |
| `FrontierJournalController` | Import/export du journal de vol Frontier |
| `UserController` | Paramètres utilisateur (clé Inara, etc.) |
| `EddnController` | Statut du listener EDDN |
| `InaraCommodityController` | Commodités Inara (logistique chantiers) |

## Services principaux

| Service | Rôle |
|---------|------|
| `BgsSyncService` | Sync BGS depuis EliteBGS API |
| `DiplomaticPipelineService` | Calcul des menaces diplomatiques |
| `GuildSystemsService` | Lecture/écriture des systèmes en DB |
| `CommandersService` | Roster des membres |
| `DeclaredChantiersService` | CRUD chantiers de colonisation |
| `FrontierAuthService` | Flow OAuth Frontier |
| `FrontierLogisticsInventoryService` | Calcul inventaire vs besoins chantier |
| `EdsmDeltaEnrichmentService` | Tendance d'influence 72h via EDSM |
| `InaraApiService` | Appels à l'API Inara |
| `EddnListenerService` | Écoute du flux EDDN (WebSocket) |

## API externes consommées

| Source | Usage |
|--------|-------|
| **EliteBGS** | Influence, états BGS des factions par système |
| **EDSM** | Coordonnées systèmes, delta d'influence 72h |
| **Inara** | Roster squadron (scraping HTML), infos factions, avatars |
| **Frontier CAPI** | Profil CMDR, inventaire, marché, journal de vol |
| **EDDN** | Flux temps réel des événements de jeu (WebSocket) |
