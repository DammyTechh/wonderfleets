# WonderFleet

**AI-enabled, heat-aware monitoring for agricultural produce in transit.**
Built for **Ofemini Global Limited** (brand: *OfeminiAgricTech*).

WonderFleet pairs the OfeminiAgricTech sensor hardware (master unit + sub-unit: temperature, humidity,
GPS, GSM, battery backup, IPX4 casing) with a web platform that watches every shipment in real time,
raises heat-spoilage alerts before produce is lost, notifies stakeholders by email and SMS, and produces
compliance and emission reports.

---

## The three interfaces

| Interface | Who uses it | How they get in |
|---|---|---|
| **Admin console** | OfeminiAgricTech operations | Email + password. There is **no registration**: the administrator is seeded from configuration. |
| **Logistics partner portal** | The transporter moving the goods | A **share link**. No account, no password. Sees fleet management and live tracking. |
| **Agro-processor portal** | The owner of the produce | A **share link**. No account, no password. Sees live tracking **with cargo conditions**. |

Sharing a shipment generates a temporary link. The link dies when the trip ends, when it expires, or
when an administrator revokes it — and the two audiences get genuinely different dashboards, enforced
server-side (a transporter never receives cargo temperature or humidity, not even over the realtime hub).

---

## Where the data lives

WonderFleet uses **two** data stores, each for what it is good at:

| Store | Holds | Who writes it |
|---|---|---|
| **Firebase Realtime Database** | Live device state: temperature, humidity, position, battery, and the cargo limits each unit alarms on | The hardware writes `vehicles/{key}`; WonderFleet reads it and writes back `settings/{key}` |
| **PostgreSQL** | The platform: trips, partners, processors, drivers, devices, telemetry history, alerts, share links, fuel plans, audit trail | WonderFleet only |

Firebase is the fastest way to get a reading off a truck and the natural fit for the firmware. It
cannot do the joins, aggregates and time-series queries that compliance reports, analytics and fuel
planning need — so every reading is ingested into PostgreSQL, which is the system of record.
Postgres can be Render's managed instance or Supabase; see below.

## Repository layout

```
wonderfleet/
├── backend/                     .NET 10 solution — everything server-side
│   ├── WonderFleet.sln
│   ├── Directory.Build.props        shared compiler and analyzer settings
│   ├── Directory.Packages.props     central package versions
│   ├── src/
│   │   ├── WonderFleet.Domain          entities, invariants, pure rules (no dependencies at all)
│   │   ├── WonderFleet.Application     use cases, DTOs, validators, port interfaces
│   │   ├── WonderFleet.Infrastructure  PostgreSQL, Firebase, Google, OpenAI, Resend, Termii, SignalR, QuestPDF
│   │   └── WonderFleet.Api             minimal-API endpoints, auth, rate limiting, Swagger UI
│   ├── tests/WonderFleet.UnitTests     domain and rule tests
│   └── db/migrations/                  V001 schema, V002 fuel planning
├── frontend/                    React + TypeScript — admin console and both portals
│   ├── src/{components,lib,pages}
│   └── package.json
├── docs/                        architecture notes, runbook, hand-written OpenAPI
│   ├── architecture.md
│   ├── runbook.md
│   └── swagger/                     openapi.yaml + paths/ + components/
└── deploy/                      Dockerfile, render.yaml, docker-compose, .env.example
```

`docs/` and `deploy/` sit at the root deliberately: the OpenAPI documents describe the contract
**between** the two halves, and the deployment blueprint provisions both.

## Architecture

Clean Architecture, four projects, dependencies point inwards:

```
Api ──► Infrastructure ──► Application ──► Domain
```

The Domain project has no package references at all, so the rules it holds — threshold evaluation,
trip transitions, fuel consumption, emissions — can be tested without a database or a network.

The database schema is owned by **plain SQL migrations** in `backend/db/migrations`, not EF migrations.
A checksummed runner applies them once, inside a transaction, behind a PostgreSQL advisory lock, so
several instances can boot together.

### Data flow

```
hardware ──► Firebase Realtime Database ──► telemetry poller ──► ingestion ──► PostgreSQL
                                                                     │
                                          alert engine ──────────────┤
                                                 │                   └──► SignalR (live dashboards)
                                                 ├──► notification outbox ──► Resend (email) / Termii (SMS)
                                                 └──► in-app notification feed
```

The API also writes **back** to Firebase: cargo limits are pushed to `settings/{deviceKey}` so a unit can
alarm locally even without GSM coverage.

---

## Running it locally

```bash
# 1. database
docker compose -f deploy/docker-compose.yml up -d db

# 2. configuration
cp deploy/.env.example .env            # then fill in the keys you have

# 3. backend  (applies the migrations and seeds the administrator on first run)
cd backend && dotnet run --project src/WonderFleet.Api

# 4. frontend  (in a second terminal)
cd frontend && npm install && npm run dev
```

Or the whole stack in Docker:

```bash
docker compose -f deploy/docker-compose.yml up --build
```

- API: `http://localhost:8080`
- **Swagger UI: `http://localhost:8080/docs`**
- Admin app: `http://localhost:5173`

Everything external is optional in development: with no Google, OpenAI, Resend or Termii keys the app still
runs — email and SMS are written to the log, weather falls back to keyless Open-Meteo, and Route AI falls
back to a transparent heuristic that reports itself as `wonderfleet-heuristic-v1`.

### First sign-in

The seeded administrator comes from `Seed:AdminEmail` / `Seed:AdminPassword`. The account is created with
**must-change-password** set, so rotate the password immediately after the first sign-in.

---

## API documentation

Endpoint documentation lives in [`docs/swagger/`](docs/swagger) as hand-written OpenAPI 3.0 documents —
deliberately **not** generated from attributes or XML comments in the code:

```
docs/swagger/openapi.yaml          root document: info, servers, tags, security, path index
docs/swagger/paths/*.yaml          one document per feature area (90 operations)
docs/swagger/components/*.yaml     shared schemas, parameters and error responses
```

The API serves them at `/openapi/openapi.yaml` and renders Swagger UI at `/docs`.

---

## Security

- Separate signing keys for admin and portal tokens, so a portal token can never be replayed as an admin token.
- Short-lived access tokens (15 min) with rotating refresh tokens; re-using a consumed refresh token revokes the whole family.
- A security stamp on every admin token: changing a password invalidates every existing session immediately.
- Portal tokens are re-checked against the share link on **every** request, so revocation is instant.
- BCrypt (enhanced, work factor 12) password hashing; share and refresh tokens stored only as HMAC hashes.
- Account lockout after 5 failed sign-ins, rate limiting on authentication, portal exchange and report generation.
- Uploaded files are served only through short-lived HMAC-signed URLs; storage paths are never public.
- ProblemDetails everywhere, with a stable machine-readable `code` and no internal detail leakage.

---

## Fuel planning

Dispatch needs to know how much fuel to send a truck out with. WonderFleet builds that figure from the
things that actually move consumption on Nigerian agri-corridors:

| Factor | How it is obtained | Effect |
|---|---|---|
| Vehicle class and payload | Capacity, fuel type and the partner's own consumption logs when recorded | Baseline L/100 km, plus up to +22% fully laden |
| Traffic | Google Routes returns both the traffic-aware and the free-flow duration; their ratio is the congestion signal | Up to +54% in severe stop-go |
| Road condition | Share of the corridor on rough surface (configurable, 25% by default) | Up to +15% |
| Heat | Forecast sampled at both ends and the midpoint at the hour the truck will be there | +1.2% per °C above 28 °C, capped at +10% |
| Rain | Precipitation probability along the route | +3% |
| Cooling unit | Runs on temperature lift and hours, **including while parked** | 1.2–4 L/h for chilled loads |
| Idling | Loading, unloading and checkpoints | 0.6–2 L/h by vehicle size |

The result is decomposed into litres per factor, priced at the current pump price, and published as a
**dispatch figure** (the estimate plus a 10% margin, rounded up to 5 L). Missing route or weather data
lowers the stated confidence and is listed in the assumptions rather than failing the request.

Once a trip is done, record the litres actually burnt: variance against plan is reported, and CO₂ for that
shipment is recomputed from the fuel — a far better emission figure than any per-kilometre average.

Worked example — Lagos to Kano, 7 t refrigerated truck carrying 5 t of tomatoes, moderate traffic, 31 °C:

```
total 296 L | dispatch 330 L | 29.6 L/100 km | CO2 794 kg | High confidence | ₦379,500
  Base consumption   183.5 L (60%)   1000 km at 18.4 L/100 km
  Cooling unit        49.0 L (16%)   runs while driving and while parked
  Payload             28.8 L  (9%)   5 t of 7 t capacity
  Traffic             23.9 L  (8%)   25% slower than free-flowing
  Road condition       8.9 L  (3%)   25% of the route on rough surface
  Heat                 8.8 L  (3%)   average 31 °C along the route
  Idling and loading   1.8 L  (1%)   2 h at 0.9 L/h
```

## Reports and analytics

Seven downloadable reports (PDF via QuestPDF, CSV via CsvHelper): temperature, humidity, CO₂ emission,
monthly compliance, sensor transmission log, route summary and fuel usage (planned versus actual, with
variance and fleet-average consumption). Analytics covers sensor uptime, temperature
and humidity compliance, estimated spoilage, average conditions against the safe band, trips by partner,
device health and weather intelligence along the corridor.

---

## Prototype values

Per the engineering concept note, all prototype set-points are treated as **ranges**, not single points
(a 5 °C target becomes a 4–10 °C band). Threshold suggestions intersect the safe ranges of every produce in
the load, and refuse combinations that cannot travel together.

---

## Using Supabase for the database

Supabase is managed PostgreSQL, so the schema, the migrations and Npgsql all work unchanged —
WonderFleet's own authentication, row ownership and share links stay exactly as they are
(Supabase Auth and RLS are not used; the API connects as a normal Postgres role).

1. Create the project, then open **Connect** and copy the **session pooler** URI
   (`aws-0-<region>.pooler.supabase.com:5432`, username `postgres.<project-ref>`).
   Do **not** use `db.<project-ref>.supabase.co` — that host is IPv6-only and most platforms,
   Render included, cannot reach it without the IPv4 add-on.
2. Set the connection string and require TLS:

   ```
   ConnectionStrings__Default=postgres://postgres.<ref>:<password>@aws-0-<region>.pooler.supabase.com:5432/postgres
   Database__SslMode=Require
   ```

3. Start the API. It applies `V001` and `V002` itself and seeds the administrator.

Three details WonderFleet already handles for you:

- **Extensions live outside `public`.** Supabase installs `pg_trgm` into the `extensions` schema,
  so the trigram search indexes would fail to build. The migration runner puts that schema on the
  search path, and `Database:SearchPath` keeps it there for normal queries.
- **Transaction pooling.** If you use the transaction pooler (port 6543) instead, set
  `Database__TransactionPooling=true`. Prepared statements and session reset are then switched off,
  because that pooler gives each transaction a different backend. Everything else still works: the
  migration runner and the background workers use *transaction*-scoped advisory locks, which are
  safe through a pooler — session-scoped locks would not be.
- **Connection budget.** `Database__MaxPoolSize` (20 by default) keeps the API inside Supabase's
  limit, remembering that the analytics queries, migrations and workers open their own connections.

If you hit a certificate error from your host, supply Supabase's root certificate rather than
reaching for `Database__TrustServerCertificate=true` — the option exists, but it turns off
verification of who you are talking to.

Free-tier projects pause after a week of inactivity; the first request afterwards will time out
while the database wakes.

## Deployment

Render blueprint in [`deploy/render.yaml`](deploy/render.yaml): Docker web service + static frontend +
managed PostgreSQL 16 + a persistent disk for uploads. See [`docs/runbook.md`](docs/runbook.md) for
day-two operations.

---

© Ofemini Global Limited. Proprietary.
