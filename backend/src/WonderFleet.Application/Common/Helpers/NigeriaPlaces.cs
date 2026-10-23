using WonderFleet.Application.Common.Interfaces;

namespace WonderFleet.Application.Common.Helpers;

/// <summary>
/// A last-resort lookup for Nigerian places, used when Google geocoding is unavailable.
///
/// It is deliberately coarse: it resolves an address to the town or state it names, not to a
/// street. That is enough for the two things WonderFleet needs coordinates for — an approximate
/// corridor to plan against, and a distance estimate — and it means a missing or rejected
/// Google key degrades the answer instead of stopping the work. Anything resolved this way is
/// reported as an estimate so nobody mistakes it for a surveyed position.
///
/// Longest name wins, so "Port Harcourt" is not matched as "Harcourt" and a request for
/// "Kano Market" resolves to Kano rather than to a state that merely shares a letter.
/// </summary>
public static class NigeriaPlaces
{
    private static readonly (string Name, double Lat, double Lng)[] Places =
    [
        ("abuja", 9.0765, 7.3986), ("federal capital territory", 9.0765, 7.3986), ("fct", 9.0765, 7.3986),
        ("lugbe", 8.9500, 7.3700), ("gwagwalada", 8.9470, 7.0800), ("kubwa", 9.1530, 7.3300),
        ("nyanya", 9.0000, 7.5700), ("garki", 9.0330, 7.4900), ("wuse", 9.0760, 7.4700),
        ("lagos", 6.5244, 3.3792), ("ikeja", 6.6018, 3.3515), ("apapa", 6.4489, 3.3597),
        ("mile 12", 6.6000, 3.3900), ("mushin", 6.5270, 3.3500), ("badagry", 6.4150, 2.8810),
        ("ibadan", 7.3775, 3.9470), ("oyo", 7.8500, 3.9330), ("ogbomoso", 8.1330, 4.2500),
        ("abeokuta", 7.1475, 3.3619), ("ijebu ode", 6.8200, 3.9170), ("sagamu", 6.8400, 3.6500),
        ("osogbo", 7.7670, 4.5670), ("ile ife", 7.4824, 4.5600), ("ilesa", 7.6200, 4.7200),
        ("akure", 7.2500, 5.1950), ("ondo", 7.1000, 4.8400), ("owo", 7.1960, 5.5870),
        ("ado ekiti", 7.6210, 5.2210), ("ikere", 7.5000, 5.2330),
        ("ilorin", 8.4966, 4.5421), ("offa", 8.1500, 4.7200), ("jebba", 9.1330, 4.8330),
        ("lokoja", 7.8023, 6.7333), ("okene", 7.5510, 6.2360), ("idah", 7.1100, 6.7300),
        ("minna", 9.6140, 6.5570), ("suleja", 9.1800, 7.1800), ("bida", 9.0830, 6.0160),
        ("kaduna", 10.5222, 7.4383), ("zaria", 11.0670, 7.7000), ("kafanchan", 9.5830, 8.3000),
        ("kano", 12.0022, 8.5920), ("wudil", 11.8100, 8.8400), ("dawanau", 12.0600, 8.4500),
        ("katsina", 12.9900, 7.6000), ("funtua", 11.5240, 7.3110), ("daura", 13.0300, 8.3200),
        ("sokoto", 13.0059, 5.2476), ("kebbi", 12.4500, 4.1990), ("birnin kebbi", 12.4500, 4.1990),
        ("zamfara", 12.1700, 6.6600), ("gusau", 12.1700, 6.6600),
        ("jos", 9.8965, 8.8583), ("bukuru", 9.7960, 8.8600), ("pankshin", 9.3240, 9.4400),
        ("bauchi", 10.3100, 9.8440), ("azare", 11.6760, 10.1900),
        ("gombe", 10.2890, 11.1670), ("yola", 9.2000, 12.4800), ("mubi", 10.2700, 13.2700),
        ("maiduguri", 11.8333, 13.1500), ("damaturu", 11.7480, 11.9660),
        ("jalingo", 8.8900, 11.3700), ("wukari", 7.8500, 9.7800),
        ("makurdi", 7.7320, 8.5390), ("gboko", 7.3200, 9.0000), ("otukpo", 7.1900, 8.1300),
        ("lafia", 8.4900, 8.5200), ("keffi", 8.8470, 7.8730), ("akwanga", 8.9100, 8.4100),
        ("enugu", 6.4584, 7.5464), ("nsukka", 6.8560, 7.3950), ("awka", 6.2100, 7.0700),
        ("onitsha", 6.1400, 6.7900), ("nnewi", 6.0100, 6.9100),
        ("owerri", 5.4830, 7.0330), ("aba", 5.1070, 7.3670), ("umuahia", 5.5250, 7.4950),
        ("abakaliki", 6.3250, 8.1130),
        ("port harcourt", 4.8156, 7.0498), ("bonny", 4.4500, 7.1700),
        ("yenagoa", 4.9250, 6.2640), ("warri", 5.5160, 5.7500), ("asaba", 6.2000, 6.7300),
        ("benin city", 6.3350, 5.6037), ("auchi", 7.0700, 6.2700), ("ekpoma", 6.7400, 6.1300),
        ("uyo", 5.0380, 7.9090), ("eket", 4.6400, 7.9300), ("calabar", 4.9590, 8.3270),
    ];

    /// <summary>The best match in the address, or null when nothing recognisable is named.</summary>
    public static GeoPoint? Locate(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        // Padded so matching is on whole words: without it "aba" would match inside
        // "Abakaliki" and send a Kano-bound load to Abia State.
        var text = $" {Normalise(address)} ";

        (string Name, double Lat, double Lng)? best = null;
        foreach (var place in Places)
        {
            if (!text.Contains($" {place.Name} ", StringComparison.Ordinal)) continue;
            if (best is null || place.Name.Length > best.Value.Name.Length) best = place;
        }
        return best is null ? null : new GeoPoint(best.Value.Lat, best.Value.Lng);
    }

    /// Lower-cases and strips punctuation so "Aco Estate, Airport Road, Lugbe." still finds Lugbe.
    private static string Normalise(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
