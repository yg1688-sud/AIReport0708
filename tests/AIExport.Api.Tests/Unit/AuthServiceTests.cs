using System.IdentityModel.Tokens.Jwt;
using AIExport.Api.Infrastructure;
using AIExport.Api.Models.Dtos;
using AIExport.Api.Models.Entities;
using AIExport.Api.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using AIExport.Api.Data;

namespace AIExport.Api.Tests.Unit;

public class AuthServiceTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new AppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private JwtConfig CreateJwtConfig() => new("test-secret-key-at-least-32-characters-long!", "AIExport", "AIExport", 2);

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        // Arrange
        var db = CreateDbContext();
        var password = "test123";
        db.Users.Add(new User
        {
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRole.User,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, CreateJwtConfig());

        // Act
        var result = await service.LoginAsync("testuser", password);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Token);
        Assert.Equal("testuser", result.User.Username);
        Assert.Equal("user", result.User.Role);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_InvalidPassword_Throws()
    {
        var db = CreateDbContext();
        db.Users.Add(new User
        {
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct"),
            Role = UserRole.User,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, CreateJwtConfig());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync("testuser", "wrong"));
    }

    [Fact]
    public async Task Login_DisabledAccount_Throws()
    {
        var db = CreateDbContext();
        db.Users.Add(new User
        {
            Username = "disabled",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("test123"),
            Role = UserRole.User,
            IsActive = false
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, CreateJwtConfig());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync("disabled", "test123"));
    }

    [Fact]
    public async Task Login_NonexistentUser_Throws()
    {
        var db = CreateDbContext();
        var service = new AuthService(db, CreateJwtConfig());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync("nobody", "test123"));
    }

    [Fact]
    public async Task Login_EmptyInput_ThrowsValidation()
    {
        var db = CreateDbContext();
        var service = new AuthService(db, CreateJwtConfig());

        var validator = new LoginRequestValidator();
        var emptyUsername = await validator.ValidateAsync(new LoginRequest("", "test123"));
        var emptyPassword = await validator.ValidateAsync(new LoginRequest("user", ""));

        Assert.False(emptyUsername.IsValid);
        Assert.False(emptyPassword.IsValid);
    }

    [Fact]
    public async Task Login_Successful_UpdatesLastLogin()
    {
        var db = CreateDbContext();
        var password = "test123";
        db.Users.Add(new User
        {
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRole.User,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, CreateJwtConfig());
        var result = await service.LoginAsync("testuser", password);

        var user = await db.Users.FirstAsync(u => u.Username == "testuser");
        Assert.NotNull(user.LastLoginAt);
    }
}
