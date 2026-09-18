namespace WonderFleet.Domain.Services;

/// Human-facing identifiers shown in the UI (TRK-1288, PRT-001, AGR-001, ADM-001, SHP-000123).
public static class Codes
{
    public static string Vehicle(long n) => $"TRK-{n:0000}";
    public static string LogisticsPartner(long n) => $"PRT-{n:000}";
    public static string AgroProcessor(long n) => $"AGR-{n:000}";
    public static string Admin(long n) => $"ADM-{n:000}";
    public static string Trip(long n) => $"SHP-{n:000000}";
}
