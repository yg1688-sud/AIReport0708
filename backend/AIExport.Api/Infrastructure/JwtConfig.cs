using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AIExport.Api.Infrastructure;

public class JwtConfig
{
    public string Secret { get; }
    public string Issuer { get; }
    public string Audience { get; }
    public int ExpiryHours { get; }

    public JwtConfig(string secret, string issuer, string audience, int expiryHours)
    {
        Secret = secret;
        Issuer = issuer;
        Audience = audience;
        ExpiryHours = expiryHours;
    }

    public string GenerateToken(Guid userId, string username, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(ExpiryHours),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
