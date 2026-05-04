using EliteBridgePlanner.Server.Controllers;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services.Contracts;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Controllers;

[TestFixture]
public class SystemsControllerTests
{
    private Mock<IBridgeService> _mockService = null!;
    private SystemsController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _mockService = new Mock<IBridgeService>();
        _controller = new SystemsController(_mockService.Object);
    }

    // ── Helper ────────────────────────────────────────────────────────────

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

    // ── Create ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Create_CallsServiceAndReturnsCreatedAtAction()
    {
        // Arrange
        var request = new CreateSystemRequest("Sol", "DEBUT", "PLANIFIE", 1, null, 1, 0, 0, 0);
        var expectedDto = SampleDto();
        _mockService
            .Setup(s => s.AddSystemAsync(request))
            .ReturnsAsync(expectedDto);

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.That(result, Is.TypeOf<CreatedAtActionResult>());
        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult?.Value, Is.EqualTo(expectedDto));
        _mockService.Verify(s => s.AddSystemAsync(request), Times.Once);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Update_WithValidId_ReturnOk()
    {
        // Arrange
        var request = new UpdateSystemRequest(1,"Sol Updated", null, "FINI", null, null, null, null);
        var expectedDto = SampleDto();
        _mockService
            .Setup(s => s.UpdateSystemAsync(1, request))
            .ReturnsAsync(expectedDto);

        // Act
        var result = await _controller.Update(1, request);

        // Assert
        Assert.That(result, Is.TypeOf<OkObjectResult>());
        var okResult = result as OkObjectResult;
        Assert.That(okResult?.Value, Is.EqualTo(expectedDto));
        _mockService.Verify(s => s.UpdateSystemAsync(1, request), Times.Once);
    }

    [Test]
    public async Task Update_WhenNotExists_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateSystemRequest(null, null, null, null, null, null, null,null);
        _mockService
            .Setup(s => s.UpdateSystemAsync(99, request))
            .ReturnsAsync((StarSystemDto?)null);

        // Act
        var result = await _controller.Update(99, request);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Test]
    public async Task Delete_WithValidId_ReturnsNoContent()
    {
        // Arrange
        _mockService
            .Setup(s => s.DeleteSystemAsync(1))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.Delete(1);

        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>());
        _mockService.Verify(s => s.DeleteSystemAsync(1), Times.Once);
    }

    [Test]
    public async Task Delete_WhenNotExists_ReturnsNotFound()
    {
        // Arrange
        _mockService
            .Setup(s => s.DeleteSystemAsync(999))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.Delete(999);

        // Assert
        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    
}

