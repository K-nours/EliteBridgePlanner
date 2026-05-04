using EliteBridgePlanner.Server.Controllers;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services.Contracts;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Controllers;

[TestFixture]
public class AuthControllerTests
{
    private Mock<IAuthService> _mockAuth = null!;
    private AuthController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockAuth = new Mock<IAuthService>();
        _controller = new AuthController(_mockAuth.Object);
    }

    // ── Login ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Login_WithValidCredentials_ReturnsOkWithToken()
    {
        // Arrange
        var request = new LoginRequest("test@example.com", "Password123!");
        var authResponse = new AuthResponse(
            Token: "fake-jwt-token",
            CommanderName: "CMDR_TEST",
            Email: "test@example.com",
            PreferredLanguage: "en",
            PreferredTimeZone: "UTC+1",
            ExpiresAt: DateTime.UtcNow.AddHours(1)
        );
        _mockAuth
            .Setup(s => s.LoginAsync(request))
            .ReturnsAsync(authResponse);

        // Act
        var result = await _controller.Login(request);

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult?.Value, Is.EqualTo(authResponse));
        _mockAuth.Verify(s => s.LoginAsync(request), Times.Once);
    }

    [Test]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest("invalid@example.com", "WrongPassword");
        _mockAuth
            .Setup(s => s.LoginAsync(request))
            .ReturnsAsync((AuthResponse?)null);

        // Act
        var result = await _controller.Login(request);

        // Assert
        Assert.That(result, Is.TypeOf<UnauthorizedObjectResult>());
    }

    // ── Register ───────────────────────────────────────────────────────────

    [Test]
    public async Task Register_WithValidData_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = new RegisterRequest(
            Email: "newuser@example.com",
            CommanderName: "CMDR_NEW",
            Password: "SecurePassword123!"
        );
        var authResponse = new AuthResponse(
            Token: "fake-jwt-token",
            CommanderName: "CMDR_NEW",
            Email: "newuser@example.com",
            PreferredLanguage: "en",
            PreferredTimeZone: "UTC+1",
            ExpiresAt: DateTime.UtcNow.AddHours(1)
        );
        _mockAuth
            .Setup(s => s.RegisterAsync(request))
            .ReturnsAsync(authResponse);

        // Act
        var result = await _controller.Register(request);

        // Assert
        Assert.That(result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult?.Value, Is.EqualTo(authResponse));
        _mockAuth.Verify(s => s.RegisterAsync(request), Times.Once);
    }

    [Test]
    public async Task Register_WithExistingEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new RegisterRequest(
            Email: "existing@example.com",
            CommanderName: "CMDR_EXISTING",
            Password: "Password123!"
        );
        _mockAuth
            .Setup(s => s.RegisterAsync(request))
            .ReturnsAsync((AuthResponse?)null);

        // Act
        var result = await _controller.Register(request);

        // Assert
        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
    }
}

