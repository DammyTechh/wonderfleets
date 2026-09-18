# WonderFleet — architecture notes

## Why Clean Architecture here

The hardware feed, the notification providers and the mapping/AI providers are all likely to change during
and after the prototype (the Firebase feed may become an MQTT broker; Termii may become Twilio). Keeping
every one of them behind an interface owned by the Application layer means those swaps never touch business
rules. The Domain project has **no package references at all**.

```
backend/src/
  Api ──► Infrastructure ──► Application ──► Domain
   └───────────────────────────────┘ (composition root wires the ports)
```

## Layer responsibilities

**Domain.** Entities with invariants enforced in methods (`Trip.Start`, `Trip.Complete`, `Alert.Escalate`),
plus pure rule services: `ThresholdEvaluator` (warning vs critical margins), `GeoMath` (haversine and the
0,0 "no fix" rejection), `EmissionCalculator`, `SpoilageEstimator`, `Codes`.

**Application.** One folder per feature, each holding models, validators and a service. Ports live in
`Common/Interfaces`: `IApplicationDbContext`, `IDeviceCloudGateway`, `IMapsService`, `IWeatherService`,
`IRouteAdvisor`, `IEmailSender`, `ISmsSender`, `IRealtimePublisher`, `IReportRenderer`, `IFileStorage`,
`IAnalyticsReadStore`, `IClock`, `ICurrentActor`.

**Infrastructure.** EF Core with a snake_case convention over the SQL-owned schema, the SQL migration runner,
hand-written analytics SQL (BRIN-friendly, never loading raw telemetry into memory), the integrations, and
three background workers.

**Api.** Minimal APIs grouped per feature, two JWT schemes, policies, rate limits, ProblemDetails, SignalR.

## Key decisions

**SQL migrations, not EF migrations.** One reviewable file per change, checksummed after application, applied
under an advisory lock. The EF model only has to *match* the schema.

**Telemetry ingestion is idempotent-ish by payload hash.** The firmware rewrites the same node continuously
with no timestamp, so the ingester stores a reading when the payload changes and otherwise stores a periodic
heartbeat (`Telemetry:HeartbeatSeconds`). Heartbeats keep uptime and charts continuous for a parked truck but
never flip a device online, so alerts cannot flap.

**Alerts escalate instead of spamming.** A partial unique index (`ux_alerts_open_device_type`) allows only one
open alert per device and type; repeats bump `occurrence_count` and can raise severity.

**Notifications go through an outbox.** Email and SMS rows are written in the same transaction as the alert,
then dispatched by a worker using `FOR UPDATE SKIP LOCKED` with exponential backoff (30 s → 8 min, 6 attempts),
so a provider outage never loses an alert and never sends it twice.

**Concurrency.** `xmin` row versions on vehicles and trips, plus partial unique indexes guaranteeing one active
trip per vehicle and per device.

**Route AI is advisory, never load-bearing.** Google Routes supplies alternatives, the forecast is sampled
along each polyline, and OpenAI ranks them under a strict JSON schema. Every field is validated after the
call; if anything is off — or the key is missing — a deterministic heuristic answers and says so in `model`.

**Portal isolation is server-side.** The portal token carries the audience; queries are scoped to the share
link's trips, and the realtime publisher redacts cargo climate for the transporter audience. There is no
client-side filtering of privileged data.

**Fuel is modelled, not guessed.** `FuelEstimator` is a pure domain service: given the vehicle, the load,
the route, the traffic ratio, the forecast and the cooling set point, it returns litres decomposed by cause.
The application service is only the data-gathering half — route, traffic and weather — and every one of those
calls is fail-soft. That split means the model can be unit-tested exhaustively without touching a network, and
it is: consumption scenarios, component decomposition, the dispatch margin and the confidence grading all have
tests. Prices live in `fuel_prices` and every estimate copies the price it used, so history stays truthful when
the pump price moves.

## Data model highlights

26 tables. Soft deletes on the entities that carry history; `timestamptz` everywhere; enums stored as text with
CHECK constraints (readable in psql, cheap to extend); BRIN index on `sensor_readings.recorded_at` because it is
append-only and time-ordered; trigram indexes for partner and vehicle search; sequences for the human-readable
codes (ADM-001, PRT-001, AGR-001, TRK-1287, SHP-004821).

## What I would add next for production

1. Partition `sensor_readings` by month once the fleet grows, with a retention job.
2. Move the notification outbox to a queue if volume exceeds a few thousand messages a day.
3. Replace the polling worker with Firebase streaming (or MQTT) to cut latency below the poll interval.
4. Ask the hardware team for a server timestamp on each device document: offline detection is currently
   inferred from change detection rather than measured.
