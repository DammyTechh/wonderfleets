using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Domain.Services;

namespace WonderFleet.Infrastructure.Persistence;

/// Human-readable codes from PostgreSQL sequences (gap-tolerant, concurrency-safe).
internal sealed class CodeGenerator(ApplicationDbContext db) : ICodeGenerator
{
    public async Task<string> NextAdminCodeAsync(CancellationToken ct) => Codes.Admin(await NextAsync("admin_code_seq", ct));
    public async Task<string> NextPartnerCodeAsync(CancellationToken ct) => Codes.LogisticsPartner(await NextAsync("partner_code_seq", ct));
    public async Task<string> NextProcessorCodeAsync(CancellationToken ct) => Codes.AgroProcessor(await NextAsync("processor_code_seq", ct));
    public async Task<string> NextVehicleCodeAsync(CancellationToken ct) => Codes.Vehicle(await NextAsync("vehicle_code_seq", ct));
    public async Task<string> NextTripCodeAsync(CancellationToken ct) => Codes.Trip(await NextAsync("trip_code_seq", ct));

    /// Preview for the wizard ("TRK-1288 · auto-generated"); the real code is assigned on save.
    public async Task<string> PeekVehicleCodeAsync(CancellationToken ct)
    {
        var value = await db.Database
            .SqlQueryRaw<long>("SELECT CASE WHEN is_called THEN last_value + 1 ELSE last_value END AS \"Value\" FROM vehicle_code_seq")
            .ToListAsync(ct);
        return Codes.Vehicle(value[0]);
    }

    private async Task<long> NextAsync(string sequence, CancellationToken ct)
    {
        // SqlQuery (not SqlQueryRaw) turns the interpolation into a real bind parameter;
        // nextval takes regclass, and a text parameter casts to it cleanly.
        var value = await db.Database.SqlQuery<long>($"SELECT nextval({sequence}::regclass) AS \"Value\"").ToListAsync(ct);
        return value[0];
    }
}
