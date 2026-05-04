using EliteBridgePlanner.Server.Controllers;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services.Contracts;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Controllers;

[TestFixture]
public class BridgesControllerTests
{
    private Mock<IBridgeService> _mockService = null!;
    private BridgesController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockService = new Mock<IBridgeService>();
        _controller = new BridgesController(_mockService.Object);
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static BridgeDto SampleBridgeDto(int id = 1) => new(
        Id: id,
        Name: "Pont Test",
        Description: "Test bridge",
        CreatedByName: "CMDR_TEST",
        Systems: [],
        CompletionPercent: 0,
        CreatedAt: DateTime.UtcNow
    );

    private static StarSystemDto SampleDto(int id = 1) => new(
    Id: id,
    Name: "Sol",
    Type: "DEBUT",
    Status: "PLANIFIE",
    Order: 1,
    PreviousSystemId: null,
    ArchitectId: null,
    ArchitectName: null,
    BridgeId: 1,
    X: 0,
    Y: 0,
    Z: 0,
    UpdatedAt: DateTime.UtcNow
);

    // ── GetAll ─────────────────────────────────────────────────────────────

    [Test]
    public async Task GetAll_ReturnsOkWithBridges()
    {
        // Arrange
        var bridges = new[] { SampleBridgeDto(1), SampleBridgeDto(2) };
        _mockService
            .Setup(s => s.GetAllBridgesAsync())
            .ReturnsAsync(bridges);

        // Act
        var result = await _controller.GetAll();

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        var data = okResult?.Value as IEnumerable<BridgeDto>;
        Assert.That(data?.Count(), Is.EqualTo(2));
        _mockService.Verify(s => s.GetAllBridgesAsync(), Times.Once);
    }

    // ── GetById ────────────────────────────────────────────────────────────

    [Test]
    public async Task GetById_WithValidId_ReturnsOkWithBridge()
    {
        // Arrange
        var bridge = SampleBridgeDto();
        _mockService
            .Setup(s => s.GetBridgeByIdAsync(1))
            .ReturnsAsync(bridge);

        // Act
        var result = await _controller.GetById(1);

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult?.Value, Is.EqualTo(bridge));
        _mockService.Verify(s => s.GetBridgeByIdAsync(1), Times.Once);
    }

    [Test]
    public async Task GetById_WhenNotExists_ReturnsNotFound()
    {
        // Arrange
        _mockService
            .Setup(s => s.GetBridgeByIdAsync(999))
            .ReturnsAsync((BridgeDto?)null);

        // Act
        var result = await _controller.GetById(999);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    // ── Create ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Create_WithValidData_ReturnsCreatedAtAction()
    {
        // Arrange
        var request = new CreateBridgeRequest("Nouveau Pont", "Description");
        var createdBridge = SampleBridgeDto();
        _mockService
            .Setup(s => s.CreateBridgeAsync(It.IsAny<CreateBridgeRequest>(), It.IsAny<string>()))
            .ReturnsAsync(createdBridge);

        // Mock the User principal
        var mockUser = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "user-1") }
            )
        );
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = mockUser }
        };

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult?.Value, Is.EqualTo(createdBridge));
        _mockService.Verify(s => s.CreateBridgeAsync(request, "user-1"), Times.Once);
    }

    [Test]
    public async Task Create_WithoutUser_ReturnsUnauthorized()
    {
        // Arrange
        var request = new CreateBridgeRequest("Nouveau Pont", "Description");
        var mockUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = mockUser }
        };

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result, Is.TypeOf<UnauthorizedResult>());
    }

    // ── Move ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Move_CallsServiceAndReturnsOk()
    {
        // Arrange

        var request = new MoveSystemRequest(StarSystemId: 1, InsertAtIndex: 2);
        var expectedDto = SampleDto();
        _mockService
            .Setup(s => s.MoveSystemAsync(1,1, request.InsertAtIndex))
            .ReturnsAsync(expectedDto);

        // Act
        var result = await _controller.Move(1, request);

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult?.Value, Is.EqualTo(expectedDto));
        _mockService.Verify(s => s.MoveSystemAsync(1, 1, request.InsertAtIndex), Times.Once);
    }

    [Test]
    public async Task Move_WhenNotExists_ReturnsNotFound()
    {
        // Arrange
        var request = new MoveSystemRequest(999,InsertAtIndex: 2);
        _mockService
            .Setup(s => s.MoveSystemAsync(1,999, request.InsertAtIndex))
            .ReturnsAsync((StarSystemDto?)null);

        // Act
        var result = await _controller.Move(999, request);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }
}

