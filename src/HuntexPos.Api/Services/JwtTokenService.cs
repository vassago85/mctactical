using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HuntexPos.Api.Domain;
using HuntexPos.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HuntexPos.Api.Services;

public class JwtTokenService
{
    private readonly JwtOptions _opt;

    public JwtTokenService(IOptions<JwtOptions> opt) => _opt = opt.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateToken(ApplicationUser user, IList<string> roles)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var minutes = ResolveExpiryMinutes(roles);
        var expires = DateTimeOffset.UtcNow.AddMinutes(minutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(ClaimTypes.NameIdentifier, user.Id)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (user.SupplierId.HasValue)
            claims.Add(new Claim("supplierId", user.SupplierId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private int ResolveExpiryMinutes(IList<string> roles)
    {
        var elevated = roles.Any(r =>
            r == Roles.Owner || r == Roles.Admin || r == Roles.Dev);
        if (elevated)
            return _opt.ExpiresMinutes;
        if (_opt.SalesExpiresMinutes > 0 && roles.Contains(Roles.Sales))
            return _opt.SalesExpiresMinutes;
        return _opt.ExpiresMinutes;
    }
}
