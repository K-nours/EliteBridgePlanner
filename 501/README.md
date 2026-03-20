# Guild Dashboard 501

Dashboard Elite Dangerous pour le contrôle galactique et le BGS.

## Panneau Guild Systems (branché)

Le panneau **Guild Systems** est le premier branché sur de vraies données :

- **Flux** : DB → API backend → panneau Angular
- **Endpoint** : `GET /api/guild/systems?guildId=1`
- **Tables** : Guilds, GuildSystems, ControlledSystems
- **Indicateur** : live (donnée API) | seed (DB vide) | mock (API indisponible)

Les autres panneaux (Sync Status, Missions, CMDRs, etc.) restent non branchés.

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

Au premier lancement, `EnsureCreated()` crée la base et le seed injecte 1 Guild + 6 ControlledSystems de test.
