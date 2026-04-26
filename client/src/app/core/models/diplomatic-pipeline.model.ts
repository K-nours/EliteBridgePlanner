/** GET /api/guild/systems/diplomatic-pipeline */
export interface DiplomaticPipelineEntryDto {
  systemName: string;
  /** Influence de la guilde dans ce système (< 5% → critique). */
  guildInfluencePercent: number;
  /** Faction contrôlante selon EDSM. Null si EDSM n'a pas renvoyé ce système. */
  dominantFaction: string | null;
  /** État de la faction dominante (ex. War). Null si non disponible. */
  dominantFactionState: string | null;
}

export interface DiplomaticPipelineDto {
  /** Systèmes critiques triés par influence croissante. */
  entries: DiplomaticPipelineEntryDto[];
  fetchedAtUtc: string;
  /** False si EDSM était indisponible lors de l'appel. */
  edsmAvailable: boolean;
}

/** GET /api/guild/faction-info — infos faction dominante scrapées depuis Inara */
export interface InaraFactionInfoDto {
  factionName: string | null;
  factionInaraUrl: string | null;
  allegiance: string | null;
  government: string | null;
  origin: string | null;
  isPlayerFaction: boolean | null;
  squadronName: string | null;
  squadronInaraUrl: string | null;
  squadronLanguage: string | null;
  squadronTimezone: string | null;
  squadronMembersCount: number | null;
  /** Message d'erreur si le scraping a échoué. */
  error: string | null;
}
