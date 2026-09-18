# WonderFleet — backend

.NET 10, Clean Architecture. See the [root README](../README.md) for the whole system and the
[architecture notes](../docs/architecture.md) for the reasoning behind the layering.

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/WonderFleet.Api     # http://localhost:8080, Swagger UI at /docs
```

## Projects

| Project | Depends on | Holds |
|---|---|---|
| `src/WonderFleet.Domain` | nothing | Entities with their invariants, enums, and pure rule services: threshold evaluation, haversine geometry, fuel consumption, emissions, spoilage |
| `src/WonderFleet.Application` | Domain | One folder per feature (models, validators, service) plus the port interfaces in `Common/Interfaces` |
| `src/WonderFleet.Infrastructure` | Application | EF Core over PostgreSQL, the SQL migration runner, Firebase, Google, OpenAI, Resend, Termii, SignalR, QuestPDF, background workers |
| `src/WonderFleet.Api` | Infrastructure | Minimal-API endpoints grouped by feature, the two JWT schemes, rate limiting, ProblemDetails, Swagger UI |
| `tests/WonderFleet.UnitTests` | all | Rules that carry risk: trip transitions, thresholds, geometry, fuel, emissions |

## Database

`db/migrations/V{n}__{name}.sql` is the single source of truth for the schema. The runner applies
each file once, inside a transaction, behind an advisory lock, and checksums what it applied —
**editing an applied migration stops the application on the next start.** Add a new file instead.

```
V001__initial_schema.sql   24 tables, sequences, indexes, seed alert rules and produce types
V002__fuel_planning.sql    fuel type on vehicles, planned/actual fuel on trips, prices, estimates
```

EF Core maps this schema; it never generates it. Entity configurations live in
`src/WonderFleet.Infrastructure/Persistence/Configurations`.

## Conventions

- Endpoint documentation lives in `../docs/swagger`, never in attributes or XML comments on the code.
- Every outbound integration sits behind an interface owned by the Application layer, and every call
  is fail-soft: a provider outage degrades a feature, it does not fail a request.
- Warnings are errors, and the analyzer set is `latest-recommended`. Keep it clean.
