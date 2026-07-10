# Sports Booking Service

REST API backend for Sports Booking, a badminton booking app for small groups of friends. Built with ASP.NET Core 10 / C# 13, backed by PostgreSQL via EF Core.

See [`API.md`](API.md) for the full endpoint reference — it's the canonical source of truth for the UI↔service contract.

## Tech stack

- ASP.NET Core 10 (.NET 10), controllers + action pattern
- EF Core with `Npgsql.EntityFrameworkCore.PostgreSQL`
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

Requires the .NET 10 SDK and a PostgreSQL database.

1. Configure the connection string via user secrets (do not commit it to `appsettings.json`):
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=sports_booking;Username=postgres;Password=postgres" --project src/SportsBookingService.Api
   ```
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
