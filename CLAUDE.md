# CLAUDE.md — Sports Booking Service

This file is the source of truth for Claude working on this project. Update it whenever a feature, model, or endpoint changes.

---

## Project Summary

The REST API backend for Sports Booking, a badminton booking app for small groups of friends. No auth — a client-generated `Guid` (`crypto.randomUUID()`, persisted in the UI's `localStorage`) is the durable Member identity across all groups.

See [API.md](./API.md) for the full endpoint reference — it's the canonical source of truth for the UI↔service contract. See the workspace-level `agents.md` for cross-repo context.

---

## Tech Stack

- **Framework**: ASP.NET Core 10 (.NET 10), C# 13
- **Database**: PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`, hosted on [Neon](https://neon.tech) (serverless Postgres — standard wire protocol, scale-to-zero, instant branching)
- **API pattern**: Controllers + actions (no minimal API endpoints, except `/health`)
- **Docs**: Scalar (OpenAPI UI), development only, at `/scalar`
- **Testing**: xUnit, coverlet, `Microsoft.NET.Test.Sdk`

---

## Project Structure

```
src/SportsBookingService.Api/
├── Controllers/   EventsController, GroupsController, LocationsController, UsersController
├── Models/        EF Core entities (see Data Model below)
├── DTOs/          Requests.cs, Responses.cs — request/response records
├── Services/       CostService (cost splitting), EventMapper (entity → response DTO mapping)
├── Data/          AppDbContext
└── Migrations/    EF Core migrations (applied automatically on startup via db.Database.Migrate())
tests/SportsBookingService.Api.Tests/
```

---

## Data Model

- **Group** — has a unique 6-char join `Code`, owns `Members`, `Locations`, `Events`, and an optional `PrimaryLocationId`.
- **Member** — belongs to exactly one `Group`; the `Id` is client-supplied (not server-generated) so it matches the UI's `localStorage` identity.
- **Location** — a saved venue belonging to a `Group`.
- **Event** — belongs to a `Group`; has a `Status` of `Draft` or `Confirmed`, `CourtBookings`, `Attendees`, `Waitlist`, `Expenses`, and (when `Draft`) `LocationOptions`.
- **LocationOption** / **LocationVote** — only relevant while an `Event` is `Draft`; members vote on proposed locations before confirming.
- **Attendee** / **WaitlistEntry** — join tables with a `JoinedAt` timestamp, used to order waitlist promotion and cost-balance display.
- **Expense** — recorded against an `Event`, paid by one `Member`.

---

## Key Conventions

- **Ids**: all resource ids are `Guid`, server-generated.
- **JSON casing**: `camelCase` on the wire (ASP.NET Core default); model/DTO properties are PascalCase in C#.
- **Dates**: `Date` fields are `DateOnly` (`yyyy-MM-dd` strings on the wire). `CreatedAt` is round-trip ISO 8601. `StartTime`/`EndTime` on court bookings are free-form client-owned strings, not `DateTime`.
- **Status codes**: `404` when a parent resource doesn't exist, `400` for invalid references (e.g. a member not in the group), `409` for conflicting state (double-vote, double-join). `POST /api/groups/join` is the one exception — a missing code returns `200` with `null`, not `404`, since it's a normal outcome the UI checks for.
- **Nullable reference types** are enabled — no `#nullable disable` workarounds.
- **Response mapping**: controllers never return EF entities directly — always map through `EventMapper` / DTO constructors in `Responses.cs`.

---

## Cost Splitting (CRITICAL)

Always round the per-attendee cost **up** to the nearest cent, but round each member's net balance normally:

```csharp
var costPerAttendee = Math.Ceiling(totalCost / attendeeCount * 100m) / 100m;
var net = Math.Round(paid - costPerAttendee, 2);
```

This logic lives in `Services/CostService.cs`. Never replicate it elsewhere — the UI has an equivalent rule in `sports-booking-ui/src/utils/costUtils.ts` that must stay in sync.

---

## Local Development

Requires the .NET 10 SDK and a PostgreSQL database — either local, or a [Neon](https://neon.tech) project/branch (the production/hosted database).

In `Development`, the `Database:UseNeon` flag in `appsettings.Development.json` picks between two user-secret keys, `ConnectionStrings:Local` and `ConnectionStrings:Neon` (see `Program.cs`). Outside `Development` (e.g. the Docker image), the app reads a single `ConnectionStrings:DefaultConnection` instead — set via `ConnectionStrings__DefaultConnection` env var.

```bash
dotnet user-secrets set "ConnectionStrings:Local" "Host=localhost;Database=sports_booking;Username=postgres;Password=postgres" --project src/SportsBookingService.Api
dotnet user-secrets set "ConnectionStrings:Neon" "Host=<project>.neon.tech;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true" --project src/SportsBookingService.Api
dotnet run --project src/SportsBookingService.Api
```

Since Neon speaks standard Postgres wire protocol, no EF Core / Npgsql code changes are needed to target it — just swap which connection string is active.

Migrations apply automatically on startup. CORS is restricted to `http://localhost:5173` and `https://sports-booking-ui.vercel.app`.

---

## Tests

```bash
dotnet test
```

Use `WebApplicationFactory<Program>` for integration tests against controllers.

---

## Instructions for Claude

**These steps apply to every code change, no matter how small.**

When an API contract changes (new endpoint, changed request/response shape, changed verb/path):
1. Update `API.md` in the same change — it is the canonical UI↔service contract reference.
2. Update this file (`CLAUDE.md`) if the change affects data model, conventions, or project structure described above.
3. Update `README.md` if setup, tech stack, or run instructions changed.
4. Add or update an EF Core migration if the model changed (`dotnet ef migrations add <Name> --project src/SportsBookingService.Api`).
5. Run `dotnet test` — fix any failures before considering the task done.
6. Add a test when new logic has non-trivial edge cases (e.g. cost splitting, waitlist promotion, vote tallying). Skip tests for straightforward CRUD that just delegates to EF Core.

**Never:**
- Generate a Member id server-side — it must always come from the client.
- Return EF entities directly from a controller — always map to a DTO.
- Duplicate the cost-splitting formula outside `CostService`.
