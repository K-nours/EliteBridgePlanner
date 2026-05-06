# Guild Dashboard 501

Dashboard Elite Dangerous pour le suivi BGS, les chantiers de colonisation et la diplomatie de l'escadron.

---

## TODO

- [ ] Remplacer l'`InaraSquadronId` statique par une résolution dynamique de l'escadron via l'authentification Frontier.
- [ ] L'identité Frontier (ID de compte, journal de vol brut) est actuellement stockée côté serveur (`server/Data/frontier-journal/<id>/`). Refactorer pour que seules les données explicitement partagées par le CMDR remontent au serveur — l'identité Frontier doit rester côté client.

---

## Architecture

Stack : **Angular 18** (frontend standalone) + **ASP.NET Core / .NET 10** (backend API) + **SQL Server** (EF Core).

| Bloc | Description | Détails |
|------|-------------|---------|
| Systèmes Faction | Suivi BGS des systèmes contrôlés | [archi-guild-systems.md](docs/archi-guild-systems.md) |
| Menaces Diplomatiques | Systèmes où une faction adverse dépasse un seuil d'influence | [archi-diplomatic-pipeline.md](docs/archi-diplomatic-pipeline.md) |
| CMDRs de l'escadron | Roster des membres via Inara | [archi-cmdrs.md](docs/archi-cmdrs.md) |
| Chantiers | Suivi des chantiers de colonisation actifs | [archi-chantiers.md](docs/archi-chantiers.md) |
| Opération Réveil | Systèmes sans mise à jour Inara depuis ≥ 5 jours | [archi-reveil.md](docs/archi-reveil.md) |
| CMDR Connecté & Auth Frontier | Authentification OAuth Frontier, identité du CMDR connecté | [archi-frontier-auth.md](docs/archi-frontier-auth.md) |
| Backend & API | Contrôleurs ASP.NET, services, syncs | [archi-backend.md](docs/archi-backend.md) |
| Base de données | SQL Server, EF Core, migrations, seed | [archi-database.md](docs/archi-database.md) |

---

## Démarrage

### Prérequis

- .NET 10
- Node.js 18+
- SQL Server

SQL Server via Docker (port **1434**, fichier `docker-compose.yml`) :

```bash
docker compose up -d
```

### Backend

```bash
cd server
dotnet run
```

→ API sur https://localhost:7294

### Frontend

```bash
cd client
npm install
npm start
```

→ App sur http://localhost:4200. Le proxy redirige `/api` vers `https://localhost:7294`.

### Base de données

Au premier lancement, `MigrateAsync()` applique les migrations automatiquement. Le seed injecte 1 Guild + les systèmes de base.

### Configuration minimale

Dans `server/appsettings.Development.json` :

```json
"Squadron": {
  "InaraSquadronId": 4926
}
```

La clé API Inara (INAPI) se configure depuis l'app (**Paramètres → Inara**) — elle est stockée dans `Data/inara-api-user.json` (non versionné).

---

## Archives

### Panneau CMDRs (Inara — suspendu)

> **Intégration roster Inara suspendue.** Voir [docs/INTEGRATION-INARA.md](docs/INTEGRATION-INARA.md) pour l'état, les limites et les alternatives.

- **Flux** : Inara roster (page HTML publique) → POST sync → DB cache → GET commanders → panneau
- **Endpoints** : `GET /api/dashboard/commanders` | `POST /api/sync/inara/commanders` | `GET /api/sync/inara/roster-diagnostic`
- **Source** : live si sync récente réussie, cached sinon. Si roster Inara est privé → aucun membre (pas un bug frontend)

#### ⚠️ Configuration statique temporaire

`InaraSquadronId` est lu statiquement depuis `appsettings.Development.json`. À terme, cette valeur devra être résolue dynamiquement via l'authentification Frontier. (→ voir TODO)

#### Roster Inara : limites

Le roster n'est **pas** disponible via l'API Inara officielle. Il est obtenu par scraping de la page HTML publique `https://inara.cz/elite/squadron-roster/{id}`. Si le roster est privé, zéro membre est renvoyé — ce n'est pas un bug frontend.

Diagnostic disponible :
```
GET /api/sync/inara/roster-diagnostic?guildId=1
```

### Panneau Guild Systems (référence technique)

- **Flux** : DB → API backend → panneau Angular
- **Endpoints** : `GET /api/guild/current` | `GET /api/guild/systems` | `POST /api/guild/systems/sync` | `POST /api/guild/systems/{id}/toggle-headquarter`
- **Tables** : Guilds, GuildSystems, ControlledSystems
- **Indicateur** : seed (démo) | cached (sync) — jamais live sans sync fraîche vérifiée
