# Sports Booking Service

REST API backend for Sports Booking, a badminton booking app for small groups of friends. Built with ASP.NET Core 10 / C# 13, backed by PostgreSQL (hosted on [Neon](https://neon.tech)) via EF Core.

See [`API.md`](API.md) for the full endpoint reference — it's the canonical source of truth for the UI↔service contract.

## Tech stack

- ASP.NET Core 10 (.NET 10), controllers + action pattern
- EF Core with `Npgsql.EntityFrameworkCore.PostgreSQL`
- **Database hosting**: [Neon](https://neon.tech) — serverless Postgres, standard wire protocol (no code changes needed vs. self-hosted Postgres), scale-to-zero on idle, and instant copy-on-write branching (handy for spinning up a per-PR/per-environment DB branch)
- Scalar for OpenAPI docs (development only)
- xUnit, coverlet, `Microsoft.NET.Test.Sdk` for testing

## Project layout

```
src/SportsBookingService.Api/
  Controllers/   REST endpoints (Groups, Locations, Events, Users)
  Models/        EF Core entities
  DTOs/          Request/response records
  Services/      CostService (cost splitting), EventMapper
  Data/          AppDbContext
  Migrations/    EF Core migrations
tests/SportsBookingService.Api.Tests/
```

## Local development

Requires the .NET 10 SDK and a PostgreSQL database — either a local instance or a [Neon](https://neon.tech) project/branch.

### First-time Neon setup

If you don't already have a Neon project (e.g. you're pulling this repo fresh and don't want to run Postgres locally):

1. Sign up / log in at [console.neon.tech](https://console.neon.tech).
2. Create a new project (pick any region) — Neon provisions a default database and branch for you.
3. On the project dashboard, open **Connection Details** and copy the connection string. Select the **Npgsql** or **.NET** snippet format if offered, or use the standard `postgres://` URI and convert it to key-value form:
   ```
   Host=<project>.<region>.aws.neon.tech;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
   ```
4. Use that string in step 1 below. Migrations run automatically on `dotnet run`, so no manual schema setup is needed — the first run against a fresh Neon database creates all tables.

Free-tier Neon projects scale to zero after a period of inactivity; the next request just triggers a cold start (a second or two) rather than failing.

1. Configure both connection strings via user secrets (do not commit them to `appsettings.json`) — you can keep both stored at once and pick which one to use at run time:
   ```bash
   # Local Postgres
   dotnet user-secrets set "ConnectionStrings:Local" "Host=localhost;Database=sports_booking;Username=postgres;Password=postgres" --project src/SportsBookingService.Api

   # Neon (get the connection string from the Neon console — requires SSL)
   dotnet user-secrets set "ConnectionStrings:Neon" "Host=<project>.neon.tech;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true" --project src/SportsBookingService.Api
   ```
2. Run the API — migrations are applied automatically on startup. Pick which database to target with the `UseNeon` flag (defaults to `false`, i.e. local):
   ```bash
   # Local Postgres (default)
   dotnet run --project src/SportsBookingService.Api

   # Neon
   dotnet run --project src/SportsBookingService.Api --launch-profile Neon
   ```
   The two launch profiles (`Local Postgres` / `Neon`) are also available as run configurations in your IDE.
3. In development, OpenAPI docs are available via Scalar at `/scalar`.

The API allows CORS from `http://localhost:5173` (the UI's dev server) and `https://sports-booking-ui.vercel.app`.

## Testing

```bash
dotnet test
```

## Docker

```bash
docker build -t sports-booking-service .
docker run -e PORT=8080 -e ConnectionStrings__DefaultConnection="..." -p 8080:8080 sports-booking-service
```

## Health check

`GET /health` returns `200 OK` with no body — unauthenticated liveness check.
