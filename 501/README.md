# Guild Dashboard 501

Dashboard Elite Dangerous pour le contrôle galactique et le BGS.

## Panneau CMDRs (Inara — suspendu)

> **Intégration roster Inara suspendue.** Voir [docs/INTEGRATION-INARA.md](docs/INTEGRATION-INARA.md) pour l'état, les limites et les alternatives.

Le panneau **CMDRs** affiche les membres du squadron depuis un cache DB alimenté par Inara :

- **Flux** : Inara roster (page HTML publique) → POST sync → DB cache → GET commanders → panneau
- **Endpoints** : `GET /api/dashboard/commanders` | `POST /api/sync/inara/commanders` | `GET /api/sync/inara/roster-diagnostic` (guildId optionnel, utilise `Guild:CurrentGuildId`)
- **Configuration temporaire** : `Squadron:InaraSquadronId` dans `appsettings.Development.json` ; clé INAPI via **Paramètres** (fichier `Data/inara-api-user.json`, hors Git) — voir [docs/INTEGRATION-INARA.md](docs/INTEGRATION-INARA.md)
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
- SQL Server

**SQL Server pour ce dashboard** — fichier `501/docker-compose.yml`, port hôte **1434** (mot de passe identique à `appsettings`) :

```bash
docker compose -f 501/docker-compose.yml up -d
```

Le conteneur `guild_dashboard_sqlserver` a `restart: unless-stopped`. **EliteBridgePlanner** utilise un SQL séparé : `docker compose up -d` **à la racine** du dépôt (port **1433**, conteneur `elitebridge_sqlserver`). **Un `git push` ne lance pas Docker** : après une mise en veille ou un reboot, relancer les deux commandes ci-dessus si besoin (ou ouvrir Docker Desktop puis attendre « healthy »).

Alternative manuelle pour le 501 : `docker run -e ACCEPT_EULA=Y -e SA_PASSWORD=YourStrong!Passw0rd -p 1434:1433 mcr.microsoft.com/mssql/server:2022-latest`

### Backend

```bash
cd 501/server
dotnet run
```

→ API sur https://localhost:7294 (et http://localhost:5294)

### Frontend

```bash
cd 501/client
npm install
npm start
```

→ App sur http://localhost:4200. Le proxy redirige `/api` vers `https://localhost:7294` (secure: false pour certificat auto-signé).

### Base de données

Au premier lancement, `MigrateAsync()` applique les migrations et le seed injecte 1 Guild + 6 ControlledSystems.

### Configuration CMDRs (Inara)

Dans `501/server/appsettings.Development.json`, définir au minimum le squadron :

```json
"Squadron": {
  "InaraSquadronId": 4926
}
```

- **InaraSquadronId** : ID du squadron (visible dans l'URL `inara.cz/elite/squadron/XXXX`)
- **Clé API Inara (INAPI)** : à saisir dans l’app (**Paramètres** → Inara). Elle est enregistrée sur le serveur dans `Data/inara-api-user.json` (non versionné). Déploiement : option `Inara__ApiKey` en variable d’environnement. Détails : [docs/INTEGRATION-INARA.md](docs/INTEGRATION-INARA.md).

### Roster Inara : limites et diagnostic

**Le roster du squadron n'est PAS disponible via l'API Inara.** L'API Inara (inapi/v1) expose `getCommanderProfile` et d'autres événements, mais **aucun endpoint roster**. La liste des membres est obtenue par récupération de la page HTML publique `https://inara.cz/elite/squadron-roster/{id}`.

**Points importants :**

1. Si le roster est **privé** sur Inara, la page renvoie du contenu "not allowed" → zéro membre parsé. **Ce n'est pas un bug frontend.**
2. La **clé INAPI** sert uniquement à l'API (getCommanderProfile pour les avatars) et aux essais roster avec en-tête ; elle ne donne pas d’accès garanti au roster privé via la page HTML seule.
3. Le système teste les deux modes (anonyme + clé API en header) et log les résultats pour chaque tentative.

**Diagnostic :**

```bash
GET /api/sync/inara/roster-diagnostic?guildId=1
```

Retourne pour chaque mode (anonyme, avec clé API) : URL, code HTTP, taille de la réponse, Content-Type, nombre de membres parsés. Permet de vérifier si le roster est accessible.

**Logs serveur** lors d'une sync : mode utilisé (anonyme / avec clé API), URL, code HTTP, taille, Content-Type, nombre de membres.
