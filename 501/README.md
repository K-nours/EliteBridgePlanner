# Guild Dashboard 501

Dashboard Elite Dangerous pour le contrôle galactique et le BGS.

## Panneau CMDRs (Inara — suspendu)

> **Intégration roster Inara suspendue.** Voir [docs/INTEGRATION-INARA.md](docs/INTEGRATION-INARA.md) pour l'état, les limites et les alternatives.

Le panneau **CMDRs** affiche les membres du squadron depuis un cache DB alimenté par Inara :

- **Flux** : Inara roster (page HTML publique) → POST sync → DB cache → GET commanders → panneau
- **Endpoints** : `GET /api/dashboard/commanders` | `POST /api/sync/inara/commanders` | `GET /api/sync/inara/roster-diagnostic` (guildId optionnel, utilise `Guild:CurrentGuildId`)
- **Configuration temporaire** : `Squadron:InaraSquadronId` + `Inara:ApiKey` dans `appsettings.Development.json`
- **Source** : live si sync récente réussie, cached sinon. Si roster Inara est privé → aucun membre (pas un bug frontend)

### ⚠️ Configuration statique temporaire

**Le panneau CMDRs utilise actuellement un `InaraSquadronId` statique de configuration.**

- **Où** : `appsettings.Development.json` → `Squadron.InaraSquadronId` (ex: `4926`)
- **Source** : valeur lue au démarrage, utilisée pour fetcher le roster Inara (page publique)
- **Cette approche est temporaire.** À terme, cette valeur devra être déterminée dynamiquement via :
  - l'authentification Frontier
  - l'identité guilde / configuration utilisateur

**TODO** : Replace static InaraSquadronId with dynamic squadron resolution once Frontier authentication is implemented.

## Panneau Guild Systems (branché)

- **Flux** : DB → API backend → panneau Angular
- **Endpoints** : `GET /api/guild/current` (guilde courante) | `GET /api/guild/systems` | `POST /api/guild/systems/sync` | `POST /api/guild/systems/{id}/toggle-headquarter` (guildId optionnel)
- **Tables** : Guilds, GuildSystems, ControlledSystems
- **Indicateur** : seed (démo) | cached (sync) — jamais live sans sync fraîche vérifiée
- **HQ** : choix déclaratif utilisateur (clic sur une ligne). Voir [docs/GUILD-SYSTEMS.md](docs/GUILD-SYSTEMS.md)

## Démarrage

### Prérequis

- .NET 10
- Node.js 18+
- SQL Server (ex. Docker : `docker run -e ACCEPT_EULA=Y -e SA_PASSWORD=YourStrong!Passw0rd -p 1434:1433 mcr.microsoft.com/mssql/server:2022-latest`)

### Backend

```bash
cd 501/server
dotnet run
```

→ API sur http://localhost:5294

### Frontend

```bash
cd 501/client
npm install
npm start
```

→ App sur http://localhost:4200 (proxy /api → backend)

### Base de données

Au premier lancement, `MigrateAsync()` applique les migrations et le seed injecte 1 Guild + 6 ControlledSystems.

### Configuration CMDRs (Inara)

Dans `501/server/appsettings.Development.json` :

```json
"Inara": {
  "ApiKey": "ta-clé-inara"
},
"Squadron": {
  "InaraSquadronId": 4926
}
```

- **InaraSquadronId** : ID du squadron (visible dans l'URL `inara.cz/elite/squadron/XXXX`)
- **Inara:ApiKey** : clé API Inara (utilisée pour `getCommanderProfile` — avatars, etc.)

### Roster Inara : limites et diagnostic

**Le roster du squadron n'est PAS disponible via l'API Inara.** L'API Inara (inapi/v1) expose `getCommanderProfile` et d'autres événements, mais **aucun endpoint roster**. La liste des membres est obtenue par récupération de la page HTML publique `https://inara.cz/elite/squadron-roster/{id}`.

**Points importants :**

1. Si le roster est **privé** sur Inara, la page renvoie du contenu "not allowed" → zéro membre parsé. **Ce n'est pas un bug frontend.**
2. **Inara:ApiKey** sert uniquement à l'API (getCommanderProfile pour les avatars). Elle ne donne pas accès au roster privé via la page HTML.
3. Le système teste les deux modes (anonyme + clé API en header) et log les résultats pour chaque tentative.

**Diagnostic :**

```bash
GET /api/sync/inara/roster-diagnostic?guildId=1
```

Retourne pour chaque mode (anonyme, avec clé API) : URL, code HTTP, taille de la réponse, Content-Type, nombre de membres parsés. Permet de vérifier si le roster est accessible.

**Logs serveur** lors d'une sync : mode utilisé (anonyme / avec clé API), URL, code HTTP, taille, Content-Type, nombre de membres.
