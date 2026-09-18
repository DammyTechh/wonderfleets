using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Models;
using WonderFleet.Domain.Entities;

namespace WonderFleet.Application.Features.Fleet;

public sealed record ProduceTypeRequest(string Name, decimal? DefaultMinTemperature, decimal? DefaultMaxTemperature, decimal? DefaultMinHumidity, decimal? DefaultMaxHumidity);

public sealed record ProduceTypeDto(Guid Id, string Name, decimal? DefaultMinTemperature, decimal? DefaultMaxTemperature, decimal? DefaultMinHumidity, decimal? DefaultMaxHumidity);

public sealed class ProduceTypeRequestValidator : AbstractValidator<ProduceTypeRequest>
{
    public ProduceTypeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).Matches(@"^[\p{L}0-9 '()&/-]+$").WithMessage("Use letters, digits and spaces only.");
        RuleFor(x => x.DefaultMinTemperature).InclusiveBetween(-30, 80);
        RuleFor(x => x.DefaultMaxTemperature).InclusiveBetween(-30, 80)
            .GreaterThan(x => x.DefaultMinTemperature).When(x => x.DefaultMinTemperature.HasValue && x.DefaultMaxTemperature.HasValue);
        RuleFor(x => x.DefaultMinHumidity).InclusiveBetween(0, 100);
        RuleFor(x => x.DefaultMaxHumidity).InclusiveBetween(0, 100)
            .GreaterThan(x => x.DefaultMinHumidity).When(x => x.DefaultMinHumidity.HasValue && x.DefaultMaxHumidity.HasValue);
    }
}

public interface IProduceService
{
    Task<IReadOnlyList<ProduceTypeDto>> ListAsync(CancellationToken ct);
    Task<IdResponse> CreateAsync(ProduceTypeRequest request, CancellationToken ct);
    Task<ProduceTypeDto> UpdateAsync(Guid id, ProduceTypeRequest request, CancellationToken ct);
}

internal sealed class ProduceService(IApplicationDbContext db, IAuditLogger audit) : IProduceService
{
    public async Task<IReadOnlyList<ProduceTypeDto>> ListAsync(CancellationToken ct) =>
        await db.ProduceTypes.AsNoTracking().OrderBy(p => p.Name)
            .Select(p => new ProduceTypeDto(p.Id, p.Name, p.DefaultMinTemperature, p.DefaultMaxTemperature, p.DefaultMinHumidity, p.DefaultMaxHumidity))
            .ToListAsync(ct);

    public async Task<IdResponse> CreateAsync(ProduceTypeRequest request, CancellationToken ct)
    {
        var name = Normalize(request.Name);
        if (await db.ProduceTypes.AnyAsync(p => p.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException($"{name} already exists.", "produce.duplicate");
        var produce = new ProduceType { Name = name };
        Apply(produce, request);
        db.ProduceTypes.Add(produce);
        audit.Record("produce.created", nameof(ProduceType), produce.Id, new { name });
        await db.SaveChangesAsync(ct);
        return new IdResponse(produce.Id);
    }

    public async Task<ProduceTypeDto> UpdateAsync(Guid id, ProduceTypeRequest request, CancellationToken ct)
    {
        var produce = await db.ProduceTypes.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Produce", id);
        var name = Normalize(request.Name);
        if (await db.ProduceTypes.AnyAsync(p => p.Id != id && p.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException($"{name} already exists.", "produce.duplicate");
        produce.Name = name;
        Apply(produce, request);
        audit.Record("produce.updated", nameof(ProduceType), id);
        await db.SaveChangesAsync(ct);
        return new ProduceTypeDto(produce.Id, produce.Name, produce.DefaultMinTemperature, produce.DefaultMaxTemperature, produce.DefaultMinHumidity, produce.DefaultMaxHumidity);
    }

    private static void Apply(ProduceType p, ProduceTypeRequest r)
    {
        p.DefaultMinTemperature = r.DefaultMinTemperature;
        p.DefaultMaxTemperature = r.DefaultMaxTemperature;
        p.DefaultMinHumidity = r.DefaultMinHumidity;
        p.DefaultMaxHumidity = r.DefaultMaxHumidity;
    }

    internal static string Normalize(string name)
    {
        var trimmed = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length == 0 ? trimmed : char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }
}
