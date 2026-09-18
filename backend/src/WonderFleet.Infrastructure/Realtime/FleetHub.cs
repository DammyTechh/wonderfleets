using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Enums;
using WonderFleet.Infrastructure.Persistence;
using WonderFleet.Infrastructure.Security;

namespace WonderFleet.Infrastructure.Realtime;

/// Live telemetry and alerts. Admins join one group; portal visitors join a per-trip group
/// for their audience only, so a logistics partner can never receive cargo climate values.
[Authorize]
public sealed class FleetHub(IServiceScopeFactory scopeFactory) : Hub
{
    public const string AdminsGroup = "admins";

    public static string TripGroup(Guid tripId, ShareAudience audience) => $"trip:{tripId}:{audience}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var tokenType = user?.FindFirst(TokenClaims.TokenType)?.Value;

        if (tokenType == TokenClaims.AdminType)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }
        else if (tokenType == TokenClaims.PortalType
                 && Guid.TryParse(user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var shareLinkId)
                 && Enum.TryParse<ShareAudience>(user?.FindFirst(TokenClaims.Audience)?.Value, out var audience))
        {
            var ct = Context.ConnectionAborted;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

            var tripIds = await db.ShareLinkTrips.AsNoTracking()
                .Where(st => st.ShareLinkId == shareLinkId && st.ShareLink!.RevokedAt == null && st.ShareLink.ExpiresAt > now)
                .Select(st => st.TripId)
                .ToListAsync(ct);

            if (tripIds.Count == 0)
            {
                Context.Abort();
                return;
            }
            foreach (var tripId in tripIds)
                await Groups.AddToGroupAsync(Context.ConnectionId, TripGroup(tripId, audience), ct);
        }
        else
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }
}
