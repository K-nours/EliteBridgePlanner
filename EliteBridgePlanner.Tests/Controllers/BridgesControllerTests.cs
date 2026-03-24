using System.Security.Claims;
using EliteBridgePlanner.Server.Controllers;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services.Contracts;
using EliteBridgePlanner.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Controllers;

/// <summary>
/// Tests unitaires de BridgesController.
/// IBridgeService est mocké avec Moq — le controller est testé en isolation totale.
/// </summary>
[TestFixture]
public class BridgesControllerTests
{
    private Mock<IBridgeService> _mockService = null!;
    private BridgesController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockService = new Mock<IBridgeService>();
        _controller  = new BridgesController(_mockService.Object);

        // Simuler un utilisateur authentifié avec un claim NameIdentifier
        SetAuthenticatedUser("user-1");
    }

    private void SetAuthenticatedUser(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    // ── GetAll ────────────────────────────────────────────────────────────

    [Test]
    public async Task GetAll_ReturnsOkWithBridgeList()
    {
        // Arrange
        var expected = new List<BridgeDto>
        {
            new(1, "Pont A", null, "CMDR_TEST", [], 0, DateTime.UtcNow),
            new(2, "Pont B", null, "CMDR_TEST", [], 100, DateTime.UtcNow)
        };
        _mockService.Setup(s => s.GetAllBridgesAsync()).ReturnsAsync(expected);

        // Act
        var result = await _controller.GetAll();

        // Assert
        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok!.StatusCode, Is.EqualTo(200));
        _mockService.Verify(s => s.GetAllBridgesAsync(), Times.Once);
    }

    // ── GetById ───────────────────────────────────────────────────────────

    [Test]
    public async Task GetById_WhenFound_ReturnsOk()
    {
        // Arrange
        var dto = new BridgeDto(1, "Pont Test", null, null, [], 0, DateTime.UtcNow);
        _mockService.Setup(s => s.GetBridgeByIdAsync(1)).ReturnsAsync(dto);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        var ok = result as OkObjectResult;
        Assert.That(ok?.StatusCode, Is.EqualTo(200));
        Assert.That((ok!.Value as BridgeDto)!.Id, Is.EqualTo(1));
    }

    [Test]
    public async Task GetById_WhenNotFound_Returns404()
    {
        // Arrange
        _mockService.Setup(s => s.GetBridgeByIdAsync(99)).ReturnsAsync((BridgeDto?)null);

        // Act
        var result = await _controller.GetById(99);

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    // ── Create ────────────────────────────────────────────────────────────

    [Test]
    public async Task Create_ValidRequest_Returns201()
    {
        // Arrange
        var request = new CreateBridgeRequest("Nouveau Pont", null);
        var dto = new BridgeDto(5, "Nouveau Pont", null, "CMDR_TEST", [], 0, DateTime.UtcNow);
        _mockService.Setup(s => s.CreateBridgeAsync(request, "user-1")).ReturnsAsync(dto);

        // Act
        var result = await _controller.Create(request);

        // Assert
        var created = result as CreatedAtActionResult;
        Assert.That(created?.StatusCode, Is.EqualTo(201));
        _mockService.Verify(s => s.CreateBridgeAsync(request, "user-1"), Times.Once);
    }

    [Test]
    public async Task Create_WithInvalidModel_Returns400()
    {
        // Arrange — simuler une erreur de validation
        _controller.ModelState.AddModelError("Name", "Requis");
        var request = new CreateBridgeRequest("", null);

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        _mockService.Verify(s => s.CreateBridgeAsync(It.IsAny<CreateBridgeRequest>(), It.IsAny<string>()),
            Times.Never);
    }
}
