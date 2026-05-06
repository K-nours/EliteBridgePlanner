# Archi — Auth Frontier & CMDR Connecté

Gestion de l'authentification OAuth Frontier (CAPI) et affichage du CMDR actuellement connecté au dashboard.

## Ce que ça fait

- Permet à un CMDR de se connecter via son compte Frontier (OAuth 2.0 PKCE)
- Stocke la session OAuth en base pour la réhydrater au redémarrage du serveur
- Expose l'identité du CMDR connecté (nom, ship, crédits...) dans le panneau `frontier-cmdr-panel`
- Donne accès aux endpoints CAPI protégés (inventaire, marché, journal de vol)

## Flux OAuth

```
1. Frontend → GET /api/frontier/auth/start
2. Redirect → Frontier OAuth (login Frontier)
3. Callback → GET /api/frontier/auth/callback
4. Backend échange le code → access_token + refresh_token
5. Tokens stockés (FrontierTokenStore, session en DB)
6. Frontend poll → GET /api/frontier/auth/status
```

## Flux de données CMDR

```
Frontier CAPI (/profile)
       ↓
  FrontierUserService
       ↓
  SQL Server (FrontierUsers, FrontierProfiles)
       ↓
  GET /api/frontier/me
       ↓
  frontier-cmdr-panel (Angular)
```

## Composants clés

**Frontend**
- `frontier-cmdr-panel` — affichage nom CMDR, ship, statut de connexion, bouton login/logout

**Backend**
- `FrontierAuthService` — orchestration du flow OAuth
- `FrontierOAuthSessionService` — persistance des sessions en DB
- `FrontierOAuthRehydrationHostedService` — rechargement des sessions au démarrage
- `FrontierTokenStore` — accès aux tokens en mémoire
- `FrontierUserService` — profil CMDR (nom, ship, etc.)
- `FrontierController` — endpoints auth + profil

## Notes

> ⚠️ L'ID de compte Frontier est actuellement persisté côté serveur dans le chemin `server/Data/frontier-journal/<id>/`. Voir TODO dans le README — cette donnée devrait rester côté client.

- Les tokens sont rechargés en mémoire au démarrage via `FrontierOAuthRehydrationHostedService` pour éviter de re-demander la connexion à chaque restart
- Le flow utilise PKCE (pas de client_secret exposé)
