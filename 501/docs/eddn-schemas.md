# Schémas EDDN reçus

Tu es abonné à **tout** le flux EDDN (`SubscribeToAnyTopic`). Voici ce qui arrive.

---

## Structure commune de chaque message

```json
{
  "$schemaRef": "https://eddn.edcd.io/schemas/<schema>/<version>",
  "header": {
    "uploaderID": "hash anonymisé du joueur",
    "softwareName": "EDDiscovery / EDMC / ...",
    "softwareVersion": "x.y.z",
    "gatewayTimestamp": "2026-04-28T12:00:00Z"
  },
  "message": { ... }
}
```

---

## Schémas actifs

### `journal/1` ⭐ BGS
**Le plus volumineux. Contient des événements du journal de bord E:D.**

Événements BGS pertinents dans `message.event` :

| Événement | Contient |
|---|---|
| `FSDJump` | `Factions[]` avec `Influence`, `FactionState`, `ActiveStates[]`, `PendingStates[]`, `RecoveringStates[]` + `SystemFaction` (faction contrôlante) |
| `Location` | Idem FSDJump, déclenché au login ou changement de système |
| `Docked` | `StationFaction`, `StationGovernment`, `StationAllegiance` |
| `CarrierJump` | Idem FSDJump mais pour un Fleet Carrier |

Événements non BGS (aussi présents, bruit de fond) :

`Scan`, `FSSDiscoveryScan`, `FSSSignalFound`, `SAAScanComplete`, `SAASignalsFound`, `ApproachSettlement`, `NavBeaconScan`, `ScanBaryCentre`, `CodexEntry`

---

### `commodity/3`
Données de marché collectées via CAPI (Frontier API) ou EDMarketConnector.

```json
{
  "systemName": "Sol",
  "stationName": "Abraham Lincoln",
  "marketId": 128016480,
  "commodities": [
    { "name": "Gold", "buyPrice": 0, "sellPrice": 9248, "demand": 12000, "supply": 0 }
  ]
}
```

**BGS indirect** : le niveau d'activité commerciale d'une station peut indiquer l'état économique d'une faction, mais non exploitable directement.

---

### `outfitting/2`
Liste des modules disponibles à une station.

```json
{
  "systemName": "...",
  "stationName": "...",
  "marketId": 128016480,
  "modules": [ "128049327", "128049345", ... ]
}
```

Pas de valeur BGS.

---

### `shipyard/2`
Liste des vaisseaux vendus à une station.

```json
{
  "systemName": "...",
  "stationName": "...",
  "marketId": 128016480,
  "ships": [ "sidewinder", "cobra", ... ]
}
```

Pas de valeur BGS.

---

### `blackmarket/1`
Transactions de marché noir. Rare, peu de joueurs l'uploadent.

Pas de valeur BGS.

---

### `fcmaterials_journal/1` et `fcmaterials_capi/1`
Matériaux échangés sur les Fleet Carriers (Bartender).

Pas de valeur BGS.

---

### `navroute/1`
Route de navigation planifiée par le joueur.

```json
{
  "Route": [
    { "StarSystem": "Sol", "SystemAddress": 10477373803, "StarClass": "G" }
  ]
}
```

Pas de valeur BGS.

---

### `approachsettlement/1`
Approche d'un settlement planétaire.

Pas de valeur BGS directe.

---

### `fssdiscoveryscan/1`
Scan FSS d'un système (nombre de corps détectés).

Pas de valeur BGS.

---

### `fsssignaldiscovered/1`
Signal FSS détecté (USS, beacon, etc.).

Pas de valeur BGS.

---

### `navbeaconscan/1`
Scan d'un beacon de navigation.

Pas de valeur BGS.

---

### `scanbarycentre/1`
Scan d'un barycentre orbital.

Pas de valeur BGS.

---

### `codexentry/1`
Entrée de codex découverte.

Pas de valeur BGS.

---

### `dockinggranted/1` et `dockingdenied/1`
Événements d'amarrage accordé/refusé. Volume faible.

Pas de valeur BGS.

---

## Résumé : ce qui compte pour le BGS

| Schéma | Événement | Utilité BGS |
|---|---|---|
| `journal/1` | `FSDJump` | ✅ Influence + états des factions par système |
| `journal/1` | `Location` | ✅ Idem au login |
| `journal/1` | `Docked` | ✅ Faction contrôlante de la station |
| `journal/1` | `CarrierJump` | ✅ Idem FSDJump |
| Tous les autres | — | ❌ Bruit de fond |

**Tout le reste (commodity, outfitting, shipyard, etc.) est du bruit pur pour le BGS.**

---

## Volume estimé

EDDN traite environ **5 000 à 15 000 messages/minute** aux heures de pointe (peak EU/US).
`journal/1` représente la majorité du volume.
`FSDJump` est l'événement le plus fréquent dans ce schéma.
