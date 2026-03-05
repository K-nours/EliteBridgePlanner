using EliteBridgePlanner.Server.Controllers;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace EliteBridgePlanner.Tests.Controllers;

/// <summary>
/// Tests unitaires de SystemsController.
/// Tous les chemins (200, 201, 404) sont couverts.
/// </summary>
[TestFixture]
public class SystemsControllerTests
{
    private Mock<IBridgeService> _mockService = null!;
    private SystemsController _controller = null!;

    private static StarSystemDto SampleDto(int id = 1) => new(
        id, "Sol", "DEBUT", "PLANIFIE", 1,null, null, null, 1, DateTime.UtcNow);

    [SetUp]
    public void SetUp()
    {
        _mockService = new Mock<IBridgeService>();
        _controller  = new SystemsController(_mockService.Object);
    }

    // ── Create ────────────────────────────────────────────────────────────

    [Test]
    public async Task Create_ValidRequest_Returns201WithDto()
    {
        // Arrange
        var request = new CreateSystemRequest("Sol", "DEBUT", "PLANIFIE", 0, null, 1);
        _mockService.Setup(s => s.AddSystemAsync(request)).ReturnsAsync(SampleDto());

        // Act
        var result = await _controller.Create(request);

        // Assert
        var created = result as CreatedAtActionResult;
        Assert.That(created?.StatusCode, Is.EqualTo(201));
        Assert.That((created!.Value as StarSystemDto)!.Name, Is.EqualTo("Sol"));
    }

    // ── Update ────────────────────────────────────────────────────────────

    [Test]
    public async Task Update_WhenFound_Returns200()
    {
        // Arrange
        var request = new UpdateSystemRequest("Sol Updated", null, "FINI", null);
        var updated = SampleDto() with { Name = "Sol Updated", Status = "FINI" };
        _mockService.Setup(s => s.UpdateSystemAsync(1, request)).ReturnsAsync(updated);

        // Act
        var result = await _controller.Update(1, request);

        // Assert
        var ok = result as OkObjectResult;
        Assert.That(ok?.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task Update_WhenNotFound_Returns404()
    {
        // Arrange
        _mockService.Setup(s => s.UpdateSystemAsync(99, It.IsAny<UpdateSystemRequest>()))
                    .ReturnsAsync((StarSystemDto?)null);

        // Act
        var result = await _controller.Update(99, new UpdateSystemRequest(null, null, null, null));

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    // ── Reorder ───────────────────────────────────────────────────────────

    //[Test]
    //public async Task Reorder_WhenFound_Returns200()
    //{
    //    // Arrange
    //    var reordered = SampleDto() with { Order = 3 };
    //    _mockService.Setup(s => s.ReorderSystemAsync(1, 3)).ReturnsAsync(reordered);

    //    // Act
    //    var result = await _controller.Reorder(1, new ReorderSystemRequest(3));

    //    // Assert
    //    var ok = result as OkObjectResult;
    //    Assert.That(ok?.StatusCode, Is.EqualTo(200));
    //    Assert.That((ok!.Value as StarSystemDto)!.Order, Is.EqualTo(3));
    //}

    //[Test]
    //public async Task Reorder_WhenNotFound_Returns404()
    //{
    //    // Arrange
    //    _mockService.Setup(s => s.ReorderSystemAsync(99, It.IsAny<int>()))
    //                .ReturnsAsync((StarSystemDto?)null);

    //    // Act
    //    var result = await _controller.Reorder(99, new ReorderSystemRequest(1));

    //    // Assert
    //    Assert.That(result, Is.InstanceOf<NotFoundResult>());
    //}

    // ── Delete ────────────────────────────────────────────────────────────

    [Test]
    public async Task Delete_WhenFound_Returns204()
    {
        // Arrange
        _mockService.Setup(s => s.DeleteSystemAsync(1)).ReturnsAsync(true);

        // Act
        var result = await _controller.Delete(1);

        // Assert
        Assert.That(result, Is.InstanceOf<NoContentResult>());
    }

    [Test]
    public async Task Delete_WhenNotFound_Returns404()
    {
        // Arrange
        _mockService.Setup(s => s.DeleteSystemAsync(99)).ReturnsAsync(false);

        // Act
        var result = await _controller.Delete(99);

        // Assert
        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }
}
