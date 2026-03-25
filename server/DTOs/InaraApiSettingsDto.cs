namespace GuildDashboard.Server.DTOs;

public sealed class InaraApiClientSettingsDto
{
    public bool ApiKeyConfigured { get; set; }
}

public sealed class InaraApiSettingsWriteDto
{
    public string? ApiKey { get; set; }
}
