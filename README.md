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

Requires the .NET 10 SDK and a PostgreSQL database — either a local instance or a [Neon](https://neon.tech) project/branch. The app reads a single connection string, `ConnectionStrings:DefaultConnection`, regardless of which one you point it at.

### First-time Neon setup

If you don't already have a Neon project (e.g. you're pulling this repo fresh and don't want to run Postgres locally):

1. Sign up / log in at [console.neon.tech](https://console.neon.tech).
2. Create a new project (pick any region) — Neon provisions a default database and branch for you.
3. On the project dashboard, open **Connection Details** and copy the connection string. Select the **Npgsql** or **.NET** snippet format if offered, or use the standard `postgres://` URI and convert it to key-value form:
   ```
   Host=<project>.<region>.aws.neon.tech;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
   ```
4. Use that string as `DefaultConnection` in step 1 below. Migrations run automatically on `dotnet run`, so no manual schema setup is needed — the first run against a fresh Neon database creates all tables.

Free-tier Neon projects scale to zero after a period of inactivity; the next request just triggers a cold start (a second or two) rather than failing.

1. Configure the connection string via user secrets (do not commit it to `appsettings.json`):
   ```bash
   # Local Postgres
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=sports_booking;Username=postgres;Password=postgres" --project src/SportsBookingService.Api

   # Neon (get the connection string from the Neon console — requires SSL)
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=<project>.neon.tech;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true" --project src/SportsBookingService.Api
   ```
   Switching between local and Neon means re-running the `user-secrets set` command with the other value — only one `DefaultConnection` is active at a time.
2. Run the API — migrations are applied automatically on startup:
   ```bash
   dotnet run --project src/SportsBookingService.Api
   ```
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
