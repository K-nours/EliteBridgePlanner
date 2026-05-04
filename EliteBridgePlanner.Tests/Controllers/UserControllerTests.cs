using EliteBridgePlanner.Server.Controllers;
using EliteBridgePlanner.Server.DTOs;
using EliteBridgePlanner.Server.Models;
using EliteBridgePlanner.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Moq;
using NUnit.Framework;
using System.Security.Claims;

namespace EliteBridgePlanner.Tests.Controllers;

[TestFixture]
public class UserControllerTests
{
    private Mock<UserManager<AppUser>> _mockUserManager = null!;
    private UserController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        // Mock UserManager
        var store = new Mock<IUserStore<AppUser>>();
        _mockUserManager = new Mock<UserManager<AppUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!
        );

        _controller = new UserController(_mockUserManager.Object);
    }

    // ── GetProfile ────────────────────────────────────────────────────────

    [Test]
    public async Task GetProfile_AuthenticatedUser_ReturnsUserProfileDto()
    {
        // Arrange
        var user = TestData.CreateUser("user-1", "CMDR_ELITE");
        user.PreferredLanguage = "fr-FR";
        user.PreferredTimeZone = "Europe/Paris";

        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) },
            "test"
        ));
        _controller.ControllerContext.HttpContext = new DefaultHttpContext { User = principal };

        // Act
        var result = await _controller.GetProfile() as OkObjectResult;
        var dto = result?.Value as UserProfileDto;

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(200));
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Email, Is.EqualTo(user.Email));
        Assert.That(dto.PreferredLanguage, Is.EqualTo("fr-FR"));
        Assert.That(dto.PreferredTimeZone, Is.EqualTo("Europe/Paris"));
    }

    [Test]
    public async Task GetProfile_UnauthenticatedUser_ReturnsUnauthorized()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((AppUser?)null);

        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext = new DefaultHttpContext { User = principal };

        // Act
        var result = await _controller.GetProfile();

        // Assert
        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    // ── UpdatePreferences ─────────────────────────────────────────────────

    [Test]
    public async Task UpdatePreferences_ValidLanguage_UpdatesUser()
    {
        // Arrange
        var user = TestData.CreateUser("user-1", "CMDR_ELITE");
        user.PreferredLanguage = "en-GB";

        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.UpdateAsync(It.IsAny<AppUser>()))
            .ReturnsAsync(IdentityResult.Success);

        var request = new UpdateUserPreferencesRequest(
            PreferredLanguage: "fr-FR",
            PreferredTimeZone: null
        );

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) },
            "test"
        ));
        _controller.ControllerContext.HttpContext = new DefaultHttpContext { User = principal };

        // Act
        var result = await _controller.UpdatePreferences(request) as OkObjectResult;
        var dto = result?.Value as UserProfileDto;

        // Assert
        Assert.That(result?.StatusCode, Is.EqualTo(200));
        Assert.That(dto?.PreferredLanguage, Is.EqualTo("fr-FR"));
        _mockUserManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Test]
    public async Task UpdatePreferences_InvalidLanguage_ReturnsBadRequest()
    {
        // Arrange
        var user = TestData.CreateUser("user-1", "CMDR_ELITE");

        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var request = new UpdateUserPreferencesRequest(
            PreferredLanguage: "es-ES", // Non supportée
            PreferredTimeZone: null
        );

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) },
            "test"
        ));
        _controller.ControllerContext.HttpContext = new DefaultHttpContext { User = principal };

        // Act
        var result = await _controller.UpdatePreferences(request) as BadRequestObjectResult;

        // Assert
        Assert.That(result?.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task UpdatePreferences_InvalidTimeZone_ReturnsBadRequest()
    {
        // Arrange
        var user = TestData.CreateUser("user-1", "CMDR_ELITE");

        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var request = new UpdateUserPreferencesRequest(
            PreferredLanguage: null,
            PreferredTimeZone: "Invalid/TimeZone"
        );

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) },
            "test"
        ));
        _controller.ControllerContext.HttpContext = new DefaultHttpContext { User = principal };

        // Act
        var result = await _controller.UpdatePreferences(request) as BadRequestObjectResult;

        // Assert
        Assert.That(result?.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task UpdatePreferences_UnauthenticatedUser_ReturnsUnauthorized()
    {
        // Arrange
        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((AppUser?)null);

        var request = new UpdateUserPreferencesRequest(
            PreferredLanguage: "fr-FR",
            PreferredTimeZone: null
        );

        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        _controller.ControllerContext.HttpContext = new DefaultHttpContext { User = principal };

        // Act
        var result = await _controller.UpdatePreferences(request);

        // Assert
        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public async Task UpdatePreferences_ValidTimeZone_UpdatesUser()
    {
        // Arrange
        var user = TestData.CreateUser("user-1", "CMDR_ELITE");
        user.PreferredTimeZone = "UTC";

        _mockUserManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.UpdateAsync(It.IsAny<AppUser>()))
            .ReturnsAsync(IdentityResult.Success);

        var request = new UpdateUserPreferencesRequest(
            PreferredLanguage: null,
            PreferredTimeZone: "Europe/Paris"
        );

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) },
            "test"
        ));
        _controller.ControllerContext.HttpContext = new DefaultHttpContext { User = principal };

        // Act
        var result = await _controller.UpdatePreferences(request) as OkObjectResult;
        var dto = result?.Value as UserProfileDto;

        // Assert
        Assert.That(result?.StatusCode, Is.EqualTo(200));
        Assert.That(dto?.PreferredTimeZone, Is.EqualTo("Europe/Paris"));
        _mockUserManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }
}
