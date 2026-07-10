using System.Security.Claims;
using AIExport.Api.Data;
using AIExport.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIExport.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization();

        // 用户列表
        group.MapGet("/users", async (AppDbContext db, ClaimsPrincipal user) =>
        {
            if (!user.IsInRole("admin")) return Results.Forbid();
            var users = await db.Users.OrderByDescending(u => u.CreatedAt)
                .Select(u => new { u.Id, u.Username, Role = u.Role.ToString().ToLower(), u.IsActive, u.CreatedAt })
                .ToListAsync();
            return Results.Ok(new { users });
        });

        // 创建用户
        group.MapPost("/users", async (CreateUserRequest req, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (!user.IsInRole("admin")) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < 3)
                return Results.BadRequest(new { error = new { code = "INVALID", message = "用户名至少3个字符" } });
            if (await db.Users.AnyAsync(u => u.Username == req.Username))
                return Results.Conflict(new { error = new { code = "DUPLICATE", message = "用户名已存在" } });

            var newUser = new User
            {
                Username = req.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = req.Role?.ToLower() == "admin" ? UserRole.Admin : UserRole.User,
                IsActive = true
            };
            db.Users.Add(newUser);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/users/{newUser.Id}", new { newUser.Id, newUser.Username });
        });

        // 禁用/启用用户
        group.MapPut("/users/{userId}/toggle", async (Guid userId, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (!user.IsInRole("admin")) return Results.Forbid();
            var u = await db.Users.FindAsync(userId);
            if (u is null) return Results.NotFound();
            u.IsActive = !u.IsActive;
            await db.SaveChangesAsync();
            return Results.Ok(new { u.Id, u.Username, u.IsActive });
        });

        // 删除用户
        group.MapDelete("/users/{userId}", async (Guid userId, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (!user.IsInRole("admin")) return Results.Forbid();
            var u = await db.Users.FindAsync(userId);
            if (u is null) return Results.NotFound();
            if (u.Role == UserRole.Admin)
                return Results.BadRequest(new { error = new { code = "FORBIDDEN", message = "不能删除管理员账户" } });
            db.Users.Remove(u);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}

record CreateUserRequest(string Username, string Password, string? Role);
