# Archi — Base de données

SQL Server via EF Core (Code First). Les migrations sont appliquées automatiquement au démarrage.

## Tables principales

| Table | Rôle |
|-------|------|
| `Guilds` | Escadron (nom, URLs Inara, config) |
| `GuildSystems` | Systèmes suivis par l'escadron (seed + sync) |
| `ControlledSystems` | Données BGS live (influence, états, delta) |
| `SquadronMembers` | Membres du roster Inara |
| `SquadronSnapshots` | Historique des syncs roster |
| `DeclaredChantiers` | Chantiers de colonisation déclarés |
| `FrontierUsers` | Utilisateurs connectés via OAuth Frontier |
| `FrontierProfiles` | Profils CMDR (nom, ship) |
| `FrontierOAuthSessions` | Sessions OAuth persistées (tokens chiffrés) |

## Migrations

Les migrations EF Core sont dans `server/Migrations/`. Appliquées automatiquement via `MigrateAsync()` dans `Program.cs`.

Pour générer une migration après modification d'un modèle :

```bash
cd server
dotnet ef migrations add <NomMigration>
```

## Seed

Au premier démarrage, `DataSeeder` injecte :
- 1 Guild (l'escadron 501)
- Les systèmes de base depuis `Data/guild-systems.seed.json`

Le seed ne s'exécute que si la table `Guilds` est vide.

## Docker

SQL Server 2022 via Docker, port hôte **1434** (pour ne pas conflicuer avec l'instance EliteBridgePlanner sur 1433) :

```bash
docker compose up -d
```

La chaîne de connexion est dans `appsettings.Development.json`.
