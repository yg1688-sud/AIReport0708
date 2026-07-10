using AIExport.Api.Data;
using AIExport.Api.Infrastructure;
using AIExport.Api.Models.Dtos;
using AIExport.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Services;

/// <summary>
/// 认证服务：验证用户凭据并签发 JWT Token
/// </summary>
public class AuthService
{
    private readonly AppDbContext _db;
    private readonly JwtConfig _jwt;

    public AuthService(AppDbContext db, JwtConfig jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    /// <summary>
    /// 验证用户名密码，成功返回 JWT Token 和用户信息
    /// </summary>
    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

        if (user is null)
            throw new UnauthorizedAccessException("用户名或密码错误");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("账户已被禁用");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new UnauthorizedAccessException("用户名或密码错误");

        // 更新最近登录时间
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _jwt.GenerateToken(user.Id, user.Username, user.Role.ToString().ToLower());
        var roleName = user.Role == UserRole.Admin ? "admin" : "user";

        return new LoginResponse(
            Token: token,
            ExpiresAt: DateTime.UtcNow.AddHours(_jwt.ExpiryHours),
            User: new UserInfo(user.Id, user.Username, roleName)
        );
    }
}
