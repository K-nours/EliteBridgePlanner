using EliteBridgePlanner.Server.DTOs;

namespace EliteBridgePlanner.Server.Services.Contracts;

/// <summary>
/// Contrat du service d'authentification.
/// L'interface permet de mocker facilement dans les tests NUnit.
/// </summary>
public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);
}
