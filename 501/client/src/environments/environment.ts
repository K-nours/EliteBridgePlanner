/**
 * Build production par défaut — ajuster `bridgePlannerApiUrl` si BridgePlanner est servi ailleurs.
 */
export const environment = {
  production: true,
  /** API EliteBridgePlanner (route partagée carte 501). */
  bridgePlannerApiUrl: 'https://localhost:7293',
  /** En prod, laisser vide : URLs relatives /api derrière reverse-proxy ou même origine. */
  guildDashboardApiBase: '',
};
