using EliteBridgePlanner.Server.Controllers;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Controllers;

[TestFixture]
public class AuthControllerTests
{
    private Mock<IAuthService> _mockAuth = null!;
    private AuthController _controller = null!;

    private static AuthResponse SampleAuth() => new(
        "fake.jwt.token",
        "CMDR_TEST",
        "cmdr@test.local",
        DateTime.UtcNow.AddDays(7)
    );

    [SetUp]
    public void SetUp()
    {
        _mockAuth   = new Mock<IAuthService>();
        _controller = new AuthController(_mockAuth.Object);
    }

    // ── Login ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        // Arrange
        var request = new LoginRequest("cmdr@test.local", "Password1!");
        _mockAuth.Setup(a => a.LoginAsync(request)).ReturnsAsync(SampleAuth());

        // Act
        var result = await _controller.Login(request);

        // Assert
        var ok = result as OkObjectResult;
        Assert.That(ok?.StatusCode, Is.EqualTo(200));
        Assert.That((ok!.Value as AuthResponse)!.CommanderName, Is.EqualTo("CMDR_TEST"));
    }

    [Test]
    public async Task Login_InvalidCredentials_Returns401()
    {
        // Arrange
        var request = new LoginRequest("bad@email.com", "WrongPass1");
        _mockAuth.Setup(a => a.LoginAsync(request)).ReturnsAsync((AuthResponse?)null);

        // Act
        var result = await _controller.Login(request);

        // Assert
        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
    }

    // ── Register ──────────────────────────────────────────────────────────

    [Test]
    public async Task Register_NewUser_Returns201()
    {
        // Arrange
        var request = new RegisterRequest("new@cmdr.local", "CMDR_NEW", "Password1!");
        _mockAuth.Setup(a => a.RegisterAsync(request)).ReturnsAsync(SampleAuth());

        // Act
        var result = await _controller.Register(request);

        // Assert
        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
    }

    [Test]
    public async Task Register_DuplicateEmail_Returns400()
    {
        // Arrange
        var request = new RegisterRequest("existing@cmdr.local", "CMDR_DUP", "Password1!");
        _mockAuth.Setup(a => a.RegisterAsync(request)).ReturnsAsync((AuthResponse?)null);

        // Act
        var result = await _controller.Register(request);

        // Assert
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Register_InvalidModel_Returns400WithoutCallingService()
    {
        // Arrange — email invalide simulé par le ModelState
        _controller.ModelState.AddModelError("Email", "Format invalide");
        var request = new RegisterRequest("not-an-email", "CMDR", "Password1!");

        // Act
        var result = await _controller.Register(request);

        // Assert
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        _mockAuth.Verify(a => a.RegisterAsync(It.IsAny<RegisterRequest>()), Times.Never);
    }
}
