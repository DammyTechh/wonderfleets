using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Enums;
using WonderFleet.Infrastructure.Security;

namespace WonderFleet.Api.Setup;

/// Who is making the current request: an administrator, a share-link visitor, or the system.
internal sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public ActorType ActorType => TokenType switch
    {
        TokenClaims.AdminType => ActorType.Admin,
        TokenClaims.PortalType => ActorType.ShareLink,
        _ => ActorType.System,
    };

    public Guid? AdminId => TokenType == TokenClaims.AdminType ? Subject : null;

    public Guid? ShareLinkId => TokenType == TokenClaims.PortalType ? Subject : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    /// The audience a portal visitor belongs to (drives which portal dashboard they get).
    public ShareAudience? PortalAudience =>
        Enum.TryParse<ShareAudience>(User?.FindFirst(TokenClaims.Audience)?.Value, out var audience) ? audience : null;

    private string? TokenType => User?.FindFirst(TokenClaims.TokenType)?.Value;

    private Guid? Subject =>
        Guid.TryParse(User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : null;
}
