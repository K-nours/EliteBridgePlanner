/**
 * `ng serve` (ex. :4200 / :4201) — base du GuildDashboard pour éviter 404 sur /api quand le proxy ne forward pas.
 * Doit correspondre au profil HTTP de `501/server/Properties/launchSettings.json` (http://localhost:5294).
 */
export const environment = {
  production: false,
  guildDashboardApiBase: 'http://localhost:5294',
};
