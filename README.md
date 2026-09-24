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
See *Firebase: the hardware feed* below for the exact paths and credentials, and
*Migrations* for the Postgres schema.

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

Everything runs from the **repository root** (`wonderfleets/`). `deploy/` lives at the
root, so `-f deploy/docker-compose.yml` only resolves from there — running it inside
`backend/` is the most common mistake.

You need: **Docker Desktop**, the **.NET 10 SDK**, and **Node 20+**.

### Step 1 — configuration

```bash
cp deploy/.env.example .env
```

One file configures everything: `docker compose` reads it, and so does the API when you
`dotnet run` (it looks for `.env` in the repository root on startup). It already contains
working local defaults, so you can start with it unchanged — every external service is
optional (see *What works without API keys* below).

Values already set in your real environment always win over `.env`, and blank lines like
`Google__ApiKey=` mean "not set". Without any `.env` the API still starts in Development
using `appsettings.Development.json`.

**Signing keys (optional locally, required in production).** With the four keys under
`# ---- secrets` blank, Development generates temporary ones on each start — which means
restarting the API signs you out. To stay signed in across restarts, generate four keys
and paste one into each:

```powershell
# Windows PowerShell — run four times
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```
```bash
# macOS / Linux
openssl rand -base64 48
```

### Step 2 — database

```bash
docker compose -f deploy/docker-compose.yml up -d db
```

Postgres is published on **host port 5433**, not 5432. This is deliberate: 5432 is the
default, so whatever else you run in Docker has usually claimed it already. If 5433 is
also taken, change `DB_PORT` in `.env` and the matching `Port=` in
`ConnectionStrings__Default` — the two must agree — and add `--env-file .env` to the
compose command. Compose otherwise looks for `.env` beside the compose file in `deploy/`,
not in the repository root, and would silently keep using 5433.

The compose project is explicitly named `wonderfleet`, so the container is
`wonderfleet-db-1` and its data lives in its own volume that no other project can share.

Check it came up:

```bash
docker compose -f deploy/docker-compose.yml ps
```

You want `wonderfleet-db-1` showing `healthy`.

### Step 3 — backend

```bash
dotnet build backend/WonderFleet.sln
dotnet run --project backend/src/WonderFleet.Api
```

Build the whole solution first: it compiles the API **and** the tests, so you find out
about any problem in one pass instead of one project at a time.

On start the API prints which configuration it used — look for
`Environment: Development. Configuration file: …\.env`. It then applies the SQL migrations,
seeds the administrator, and listens on `http://localhost:8080`. **There is no separate
migration command to run** — see *Migrations* below.

A healthy first start ends with lines like:

```
Seeded administrator ADM-001 (ofeminiagrictech@gmail.com)
Now listening on: http://localhost:8080
```

A yellow `Telemetry polling is OFF` warning is expected until you add a Firebase
credential; everything else works without it.

### Step 4 — frontend

In a second terminal:

```bash
npm --prefix frontend install
npm --prefix frontend run dev
```

| | |
|---|---|
| Admin app | `http://localhost:5173` |
| API | `http://localhost:8080` |
| Swagger UI | `http://localhost:8080/docs` |
| Health check | `http://localhost:8080/health/ready` (checks the database too) |

Sign in with `Seed__AdminEmail` / `Seed__AdminPassword` from `.env`. The account is created
with **must-change-password** set, so change it right after the first sign-in.

**Logs.** Development shows one line per request plus warnings and WonderFleet's own
messages. To see every SQL statement while debugging, add to `.env` and restart:
`Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore=Information`.

### Or: the whole stack in Docker

```bash
docker compose -f deploy/docker-compose.yml up --build
```

The API container talks to Postgres over the internal network on 5432 and ignores the
connection string in `.env`, so the host port never matters here.

### What works without API keys

The app runs with every third-party key blank. Email and SMS are written to the API's
console instead of being sent — even if `Email__Provider` names a provider, it is only used
once its key is filled in — weather falls back to keyless Open-Meteo, and Route AI falls back
to a transparent heuristic that reports itself as `wonderfleet-heuristic-v1`. Fill the
keys in when you have them; nothing needs to change in code.

---

## Migrations

Migrations are **plain SQL files** in `backend/db/migrations/`, named `V001__name.sql`,
`V002__name.sql` and so on. They are **applied automatically** every time the API starts,
in version order, inside a transaction guarded by a Postgres advisory lock — so two
instances starting at once cannot both apply them.

You do not run a migration command. Start the API and it migrates.

```
backend/db/migrations/V001__initial_schema.sql
backend/db/migrations/V002__fuel_planning.sql
backend/db/migrations/V003__device_thresholds.sql
```

Applied versions are recorded in the `schema_migrations` table with a checksum; editing a
file that has already been applied is detected and rejected rather than silently ignored.
To add a schema change, add `V003__your_change.sql` and restart the API.

To turn the automatic run off (for example in a production deploy where migrations are a
separate gated step), set `Database__RunMigrationsOnStartup=false`.

**These migrations are for PostgreSQL only.** Firebase has no schema and nothing to
migrate — see the next section.

---

## Firebase: the hardware feed

This trips people up, so to be explicit: **Firebase and PostgreSQL are not alternatives
to each other here.** They do different jobs.

| | Firebase Realtime Database | PostgreSQL |
|---|---|---|
| Holds | The *current* reading from each unit, and the limits each unit alarms on | Everything else: trips, partners, processors, drivers, devices, reading history, alerts, share links, reports, audit trail |
| Written by | The firmware (and WonderFleet, for the limits) | WonderFleet only |
| Schema | None | The SQL migrations above |
| Why | It is what the hardware engineer already writes to, and it is the fastest path off a truck over GSM | Joins, aggregates and time-series — what compliance reports, analytics and fuel planning need |

Every reading WonderFleet pulls out of Firebase is written into Postgres, which is the
system of record. Firebase is the doorway, not the filing cabinet.

### What to set in `.env`

```bash
Firebase__DatabaseUrl=https://wonderfleet-12693-default-rtdb.europe-west1.firebasedatabase.app
Firebase__ServiceAccountJson=
Firebase__AllowUnauthenticated=false
Telemetry__PollingEnabled=true
Telemetry__PollIntervalSeconds=10
```

`Firebase__DatabaseUrl` is the URL shown at the top of the Realtime Database page in the
Firebase console — the one already filled in above matches your `wonderFleet` project.
No trailing slash, no `/vehicles` on the end.

**For the credential, pick one of three:**

1. **Service account (use this for anything real).** Firebase console → gear icon →
   *Project settings* → *Service accounts* → *Generate new private key*. That downloads a
   JSON file. Put its **entire contents** into `Firebase__ServiceAccountJson`. Because the
   JSON contains newlines that `.env` files mangle, base64-encode it first — the gateway
   accepts either form:
   ```bash
   # macOS / Linux
   base64 -w0 wonderfleet-service-account.json
   # Windows PowerShell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("wonderfleet-service-account.json"))
   ```
   Never commit that file or paste it into chat. If it leaks, revoke the key in the same
   console screen.

2. **Open prototype rules (quickest, dev only).** If your database rules still allow public
   reads and writes, set `Firebase__AllowUnauthenticated=true` and leave the service account
   empty. Do not ship this — anyone with the URL can read and rewrite your truck data.

3. **Legacy database secret.** `Firebase__DatabaseSecret=<secret>`. Deprecated by Google;
   only worth it if the firmware already uses one.

If none of the three is set, the API tells you so on the first telemetry poll rather than
failing silently.

### The paths the hardware uses

The gateway is already wired to the structure in your console:

| Path | Direction | Fields |
|---|---|---|
| `vehicles/{key}` | firmware → WonderFleet | `temp`, `hum`, `lat`, `lng`, `isActive`, plus optional `battery` and `ts` |
| `settings/{key}` | WonderFleet → firmware | `setLowTemp`, `setHighTemp`, `setLowHum`, `setHighHum` |

`{key}` is the unit's id — `TRK-0001` in your current data. Register the same string as the
device's Firebase key in the admin console and the two sides line up.

Field names are matched case-insensitively and with aliases (`temperature`/`t` for `temp`,
`longitude`/`lon`/`long` for `lng`, and so on), so a firmware rename will not immediately
break ingestion. Thresholds are written as whole numbers where possible because some
firmware SDK calls read them with `getInt()` and reject fractions. The engineer's scratch
node (`test`) is explicitly ignored and never treated as a device.

Concretely, the API reads
`https://wonderfleet-12693-default-rtdb.europe-west1.firebasedatabase.app/vehicles.json`
every `Telemetry__PollIntervalSeconds`, and writes limits to
`…/settings/TRK-0001.json` whenever you change them for a trip.

### Getting a unit's data on screen

Three things must all be true before a node in Firebase shows up in the app. If data is
missing, check them in this order — the dashboard shows a notice for the first two.

1. **Polling is running.** Needs a credential (above). If it is off, the dashboard says
   exactly which setting is missing.
2. **The node is registered as a device.** WonderFleet never adds a Firebase node on its
   own, so the engineer's `test` node or a stray write can never become a truck. Nodes that
   are transmitting but unregistered appear on the dashboard and in *Add a Fleet → Monitoring
   device* with a **Register** button.
3. **The node holds real readings.** The firmware writes `-1` for a sensor that returned
   nothing and `0, 0` for no GPS fix. WonderFleet treats those as *no reading* rather than
   showing `-1.0 °C` or a truck in the Gulf of Guinea. A unit whose last write is older than
   the offline window (10 minutes by default) is shown as offline even if it says
   `isActive: true`, because nothing has updated it since.

### Where a unit's readings appear

**Devices** (in the sidebar) lists every registered unit with what it is reporting right
now — temperature, humidity, position, when it last wrote — whether or not it is carrying a
shipment. It is also where each unit's **limits** are set: the four numbers the firmware
alarms on. Saving them writes to `settings/<key>` in Firebase immediately, and **Send**
re-sends them if the unit missed the write.

While a unit is on a trip, that shipment's limits take over and the unit's own are shown
read-only until the trip finishes. Readings taken off a trip still count towards the
temperature and humidity charts and the compliance figures on *Analytics*, measured against
whichever limits applied at the time. A reading with no limits at all is not counted, so the
compliance percentage always describes readings that had something to be measured against.

Once a unit is assigned to a trip, its readings also appear on the dashboard and the live map.

### How live updates reach the screen

Nothing on screen is hardcoded or sample data; every figure is computed from the database
when it is requested. Updates flow like this:

1. The firmware writes to Firebase.
2. The API reads Firebase every `Telemetry__PollIntervalSeconds` (default 10, minimum 5)
   and stores any change.
3. The moment a reading or alert is stored, the API pushes a message over SignalR
   (`/hubs/fleet`).
4. The admin app holds one live connection (`frontend/src/lib/live.ts`). Each message
   refreshes only what it affects — dashboard, live map, fleet list, the open trip, alerts,
   the bell — within about a second, without a reload. Bursts of readings are batched into
   one refresh per second.

So a reading appears **within roughly the poll interval plus a second**. The **Live** dot
beside the bell shows the connection state; if it drops, the app reconnects on its own and
catches up, and every screen still refreshes each minute in the meantime. Partner tracking
links get the same live connection, limited to the trips on their link.

For updates faster than the poll interval, use the webhook below so the firmware pushes
straight to the API.

### Optional: push instead of poll

If you would rather the hardware post directly to the API than have the API poll Firebase,
set `Telemetry__WebhookSecret` and the firmware can `POST /telemetry` with an
`X-WonderFleet-Signature: sha256=<hmac>` header over the raw body. With the secret unset
the endpoint returns 404 and does not exist. Polling and pushing can both run — readings are
de-duplicated.

---

## When something fails

| What you see | What it means | Fix |
|---|---|---|
| `password authentication failed for user "postgres"` | An older volume, or a different Postgres, is answering on the port — Postgres keeps the password its volume was created with | The API now prints the exact fix. In short: `docker compose -f deploy/docker-compose.yml down -v`, then `up -d db` (deletes local data only) |
| `No database connection string` | The API found neither `.env` nor Development settings | Run from the repository root, and check `.env` exists there (`cp deploy/.env.example .env`) |
| API starts on port 5000 instead of 8080 | Started some way other than `dotnet run --project …` | Use the command above; it reads `Properties/launchSettings.json`, which sets 8080 |
| Signed out every time the API restarts | The four signing keys are blank, so temporary ones are generated | Generate keys as shown in Step 1 |
| `Bind for 0.0.0.0:5433 failed: port is already allocated` | Something already owns 5433 — often an older `deploy-db-1` container | `docker rm -f deploy-db-1`, or pick another `DB_PORT` and use `--env-file .env` |
| `open …\backend\deploy\docker-compose.yml: The system cannot find the path` | You ran compose from `backend/` | Run it from the repository root |
| `NU1902` / `CS0618` / `EF1002` / `CA…` as **warnings** | Advisories, obsolete-API notices and analyzer suggestions | These no longer stop the build locally. Fix them when convenient; `dotnet build -p:WonderFleetStrict=true` shows them as errors |
| `error CS…` | A genuine compile error | Paste the full `dotnet build` output. Each project compiles only after the ones it depends on succeed, so a failure in one project hides errors in the projects above it |
| API starts then exits with `Migrations folder not found` | Running from an unexpected working directory | Use `dotnet run --project backend/src/WonderFleet.Api` from the root |
| `Firebase credentials are missing` | No service account, secret, or `AllowUnauthenticated` | Set one of the three above |
| Dashboard shows devices offline | Polling is off, or the key does not match | Check `Telemetry__PollingEnabled=true` and that the device's Firebase key equals the node name in `vehicles/` |

**Other containers on your machine are irrelevant.** If `docker ps` shows Supabase or
Navtrack containers, those belong to your other projects. WonderFleet does not use
Supabase; it only needs `wonderfleet-db-1`. The only way they interfere is by holding a port,
which is what 5433 avoids.

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

## Hosting Postgres somewhere else

Nothing above is tied to the local container. `ConnectionStrings__Default` accepts either a
Npgsql keyword string or a `postgres://` URL, which is converted automatically — so Render,
Neon, Supabase or a plain VM all work the same way. For a managed provider set
`Database__SslMode=Require`. If the provider puts you behind a *transaction* pooler (pgbouncer
in transaction mode, commonly port 6543), also set `Database__TransactionPooling=true`, which
turns off prepared statements and session reset — without it you will see errors about
prepared statements already existing.

---

## Deployment

Render blueprint in [`deploy/render.yaml`](deploy/render.yaml): Docker web service + static frontend +
managed PostgreSQL 16 + a persistent disk for uploads. See [`docs/runbook.md`](docs/runbook.md) for
day-two operations.

---

© Ofemini Global Limited. Proprietary.
