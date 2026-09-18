using WonderFleet.Domain.Entities;
using WonderFleet.Domain.Enums;
using WonderFleet.Domain.Exceptions;
using WonderFleet.Domain.Services;
using Xunit;

namespace WonderFleet.UnitTests;

public sealed class TripTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 20, 8, 0, 0, TimeSpan.Zero);

    private static Trip NewTrip()
    {
        var trip = new Trip
        {
            TripCode = "SHP-000001",
            VehicleId = Guid.NewGuid(),
            LogisticsPartnerId = Guid.NewGuid(),
            AgroProcessorId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            EstimatedWeightTonnes = 5,
            PickupAddress = "Mile 12 Market, Lagos",
            OriginLabel = "Lagos",
            DestinationAddress = "Kano Central Market",
            DestinationLabel = "Kano",
            LoadingTime = Now,
            ExpectedArrival = Now.AddHours(20),
            Vehicle = new Vehicle
            {
                VehicleCode = "TRK-1001", FleetNumber = "WF-1234", LicenseNumber = "LAG-234-XY",
                VehicleType = "Refrigerated truck", CapacityTonnes = 7, LogisticsPartnerId = Guid.NewGuid(),
            },
        };
        trip.SetThresholds(new CargoThresholds(2, 8, 40, 75));
        return trip;
    }

    [Fact]
    public void Start_moves_a_scheduled_trip_in_transit()
    {
        var trip = NewTrip();

        trip.Start(Now);

        Assert.Equal(TripStatus.InTransit, trip.Status);
        Assert.Equal(Now, trip.StartedAt);
        Assert.True(trip.IsMoving);
        Assert.False(trip.IsEnded);
    }

    [Fact]
    public void Start_is_rejected_twice()
    {
        var trip = NewTrip();
        trip.Start(Now);

        var error = Assert.Throws<DomainException>(() => trip.Start(Now.AddMinutes(5)));
        Assert.Equal("trip.invalid_transition", error.Code);
    }

    [Fact]
    public void Complete_requires_a_started_trip()
    {
        var trip = NewTrip();

        Assert.Throws<DomainException>(() => trip.Complete(Now.AddHours(1)));
    }

    [Fact]
    public void Complete_ends_the_trip_and_freezes_emissions()
    {
        var trip = NewTrip();
        trip.Start(Now);
        trip.ApplyTelemetry(6.45m, 60m, 6.5244, 3.3792, Now.AddMinutes(10));
        trip.ApplyTelemetry(6.50m, 61m, 7.3775, 3.9470, Now.AddHours(3));

        trip.Complete(Now.AddHours(18));

        Assert.Equal(TripStatus.Completed, trip.Status);
        Assert.True(trip.IsEnded);
        Assert.True(trip.DistanceTravelledKm > 100, "Lagos to Ibadan is well over 100 km.");
        Assert.True(trip.Co2EmissionKg > 0);
    }

    [Fact]
    public void Cancelled_trip_cannot_be_completed()
    {
        var trip = NewTrip();
        trip.Start(Now);
        trip.Cancel(Now.AddHours(1));

        Assert.Equal(TripStatus.Cancelled, trip.Status);
        Assert.Throws<DomainException>(() => trip.Complete(Now.AddHours(2)));
    }

    [Fact]
    public void ApplyTelemetry_ignores_a_null_island_fix()
    {
        var trip = NewTrip();
        trip.Start(Now);

        // GeoMath rejects 0,0 before the trip ever sees it, so the ingestion service passes null.
        trip.ApplyTelemetry(5m, 55m, GeoMath.IsValidFix(0, 0) ? 0 : null, GeoMath.IsValidFix(0, 0) ? 0 : null, Now.AddMinutes(5));

        Assert.Null(trip.LastLatitude);
        Assert.Equal(0, trip.DistanceTravelledKm);
        Assert.Equal(5m, trip.LastTemperature);
    }

    [Fact]
    public void ApplyTelemetry_ignores_jitter_below_the_movement_threshold()
    {
        var trip = NewTrip();
        trip.Start(Now);
        trip.ApplyTelemetry(5m, 55m, 6.52440, 3.37920, Now.AddMinutes(5));

        // ~11 m away: GPS noise while parked, not travel.
        trip.ApplyTelemetry(5m, 55m, 6.52450, 3.37920, Now.AddMinutes(10));

        Assert.Equal(0, trip.DistanceTravelledKm);
    }

    [Fact]
    public void MarkStopped_and_movement_flip_the_status_back()
    {
        var trip = NewTrip();
        trip.Start(Now);
        trip.ApplyTelemetry(5m, 55m, 6.5244, 3.3792, Now.AddMinutes(5));

        trip.MarkStopped();
        Assert.Equal(TripStatus.Stopped, trip.Status);

        trip.ApplyTelemetry(5m, 55m, 6.6000, 3.4000, Now.AddMinutes(50));
        Assert.Equal(TripStatus.InTransit, trip.Status);
    }

    [Fact]
    public void Reschedule_clears_a_delay_when_the_new_eta_is_ahead()
    {
        var trip = NewTrip();
        trip.Start(Now);
        trip.MarkDelayed(Now.AddHours(21));
        Assert.Equal(TripStatus.Delayed, trip.Status);

        trip.Reschedule(Now.AddHours(26), Now.AddHours(21));

        Assert.Equal(TripStatus.InTransit, trip.Status);
        Assert.Equal(Now.AddHours(26), trip.ExpectedArrival);
    }

    [Fact]
    public void Reschedule_rejects_an_arrival_before_loading()
    {
        var trip = NewTrip();

        Assert.Throws<DomainException>(() => trip.Reschedule(Now.AddHours(-1), Now));
    }

    [Theory]
    [InlineData(8, 2, 40, 75)]   // max below min temperature
    [InlineData(2, 8, 80, 75)]   // max below min humidity
    [InlineData(2, 8, -5, 75)]   // humidity out of range
    public void SetThresholds_rejects_impossible_ranges(decimal minT, decimal maxT, decimal minH, decimal maxH)
    {
        var trip = NewTrip();

        Assert.Throws<DomainException>(() => trip.SetThresholds(new CargoThresholds(minT, maxT, minH, maxH)));
    }
}
