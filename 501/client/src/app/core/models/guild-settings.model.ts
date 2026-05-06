export interface GuildSettingsDto {
  inaraFactionPresenceUrl: string | null;
  inaraSquadronUrl: string | null;
  inaraCmdrUrl: string | null;
  lastSystemsImportAt: string | null;
  lastCommandersSyncAt: string | null;
  lastAvatarImportAt: string | null;
}

export interface GuildSettingsUpdateDto {
  inaraFactionPresenceUrl?: string | null;
  inaraSquadronUrl?: string | null;
  inaraCmdrUrl?: string | null;
}
