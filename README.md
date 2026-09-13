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
  Controllers/   REST endpoints (Groups, Locations, Events, Users, Pending Bookings, Webhooks)
  Auth/          Clerk JWKS retrieval, inbound-email webhook Basic auth
  Models/        EF Core entities
  DTOs/          Request/response records
  Services/      CostService (cost splitting), EventMapper, email ingestion + booking extraction
  Data/          AppDbContext
  Migrations/    EF Core migrations
tests/SportsBookingService.Api.Tests/
```

## Local development

Requires the .NET 10 SDK and a PostgreSQL database — either a local instance or a [Neon](https://neon.tech) project/branch. In `Development`, which one is used is controlled by the `Database:UseNeon` flag in [appsettings.Development.json](src/SportsBookingService.Api/appsettings.Development.json) (defaults to `false`, i.e. local); outside `Development` (e.g. the hosted Docker image), the app reads a single `ConnectionStrings:DefaultConnection` instead.

### First-time Neon setup

If you don't already have a Neon project (e.g. you're pulling this repo fresh and don't want to run Postgres locally):

1. Sign up / log in at [console.neon.tech](https://console.neon.tech).
2. Create a new project (pick any region) — Neon provisions a default database and branch for you.
3. On the project dashboard, open **Connection Details** and copy the connection string. Select the **Npgsql** or **.NET** snippet format if offered, or use the standard `postgres://` URI and convert it to key-value form:
   ```
   Host=<project>.<region>.aws.neon.tech;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
   ```
4. Use that string as `ConnectionStrings:Neon` in step 1 below. Migrations run automatically on `dotnet run`, so no manual schema setup is needed — the first run against a fresh Neon database creates all tables.

Free-tier Neon projects scale to zero after a period of inactivity; the next request just triggers a cold start (a second or two) rather than failing.

1. Configure both connection strings via user secrets (do not commit them to `appsettings.json`) — you can keep both stored at once:
   ```bash
   # Local Postgres
   dotnet user-secrets set "ConnectionStrings:Local" "Host=localhost;Database=sports_booking;Username=postgres;Password=postgres" --project src/SportsBookingService.Api

   # Neon (get the connection string from the Neon console — requires SSL)
   dotnet user-secrets set "ConnectionStrings:Neon" "Host=<project>.neon.tech;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true" --project src/SportsBookingService.Api
   ```
2. Pick which one to run against by setting `Database:UseNeon` in [appsettings.Development.json](src/SportsBookingService.Api/appsettings.Development.json) (`false` = local, `true` = Neon), then run the API — migrations are applied automatically on startup:
   ```bash
   dotnet run --project src/SportsBookingService.Api
   ```
3. In development, OpenAPI docs are available via Scalar at `/scalar`.

The API allows CORS from `http://localhost:5173` (the UI's dev server) and `https://sports-booking-ui.vercel.app`.

### Email ingestion setup (optional)

Booking-confirmation emails members auto-forward to the app land on `POST /api/webhooks/email`
and become rows in the review queue. Both services it depends on are free:

1. **[CloudMailin](https://www.cloudmailin.com)** (free plan: 10,000 inbound emails/month, no
   domain purchase needed) — create a target pointing at
   `https://<your-host>/api/webhooks/email`, format **JSON (Normalized)**. Add an
   `Authorization` header on the address page with the value
   `Basic <base64 of username:password>` — CloudMailin no longer supports credentials
   embedded in the target URL. Then copy the address it generates into `Email:InboundAddress`.
2. **[Google AI Studio](https://ai.google.dev)** — generate a free Gemini API key (no card
   required) for `Gemini:ApiKey`.

```bash
dotnet user-secrets set "Email:WebhookUsername" "<username>" --project src/SportsBookingService.Api
dotnet user-secrets set "Email:WebhookPassword" "<password>" --project src/SportsBookingService.Api
dotnet user-secrets set "Email:InboundAddress" "<yourhash>@cloudmailin.net" --project src/SportsBookingService.Api
dotnet user-secrets set "Gemini:ApiKey" "<key>" --project src/SportsBookingService.Api
```

Once configured, forwarded bookings land in a review queue by default. A group's admin can
optionally enable auto-accept (Settings → Booking emails, or
`PUT /api/groups/{id}/booking-automation`), which creates the event without review — but only
when the venue exactly matches a saved location, the name on the booking matches one member, the
date is upcoming, the cost is under a configured ceiling, and no event exists that day. See
[API.md](API.md#auto-accept) for the full list.

In production these are environment variables using the `Section__Key` convention
(`Email__WebhookUsername`, `Gemini__ApiKey`, …). `Gemini:Model` defaults to
`gemini-flash-lite-latest` in `appsettings.json` and can be overridden per environment.

Without this configuration the rest of the API works normally; only the webhook and the
Settings page's forwarding address are unavailable. You can exercise the webhook locally with
placeholder credentials and no CloudMailin account — see [CLAUDE.md](CLAUDE.md) for a ready-made
`curl` command.

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
