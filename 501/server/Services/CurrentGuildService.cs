namespace GuildDashboard.Server.Services;

/// <summary>
/// Fournit le guildId courant (configuré en dur tant que l'authentification Frontier n'est pas en place).
/// Sera remplacé par la résolution dynamique via token/session.
/// </summary>
public class CurrentGuildService
{
    private readonly IConfiguration _config;

    public CurrentGuildService(IConfiguration config) => _config = config;

    public int CurrentGuildId => _config.GetValue("Guild:CurrentGuildId", 1);
}
