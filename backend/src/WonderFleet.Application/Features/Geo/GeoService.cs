using WonderFleet.Application.Common.Exceptions;
using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Application.Features.Geo;

public interface IGeoService
{
    Task<GeocodeResult> GeocodeAsync(string address, CancellationToken ct);
    Task<IReadOnlyList<PlaceSuggestion>> AutocompleteAsync(string input, string? sessionToken, CancellationToken ct);
}

internal sealed class GeoService(IMapsService maps) : IGeoService
{
    public async Task<GeocodeResult> GeocodeAsync(string address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 300)
            throw RequestValidationException.For(nameof(address), "Enter an address of up to 300 characters.");
        return await maps.GeocodeAsync(address.Trim(), ct)
            ?? throw new BusinessRuleException("geo.not_found", "We could not locate that address. Add more detail (street, city, state).");
    }

    public Task<IReadOnlyList<PlaceSuggestion>> AutocompleteAsync(string input, string? sessionToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < 3)
            return Task.FromResult<IReadOnlyList<PlaceSuggestion>>([]);
        return maps.AutocompleteAsync(input.Trim()[..Math.Min(input.Trim().Length, 120)], sessionToken, ct);
    }
}
