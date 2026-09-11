# CLAUDE.md — Sports Booking Service

This file is the source of truth for Claude working on this project. Update it whenever a feature, model, or endpoint changes.

---

## Project Summary

The REST API backend for Sports Booking, a badminton booking app for small groups of friends. Auth is real: every endpoint requires a Clerk-issued JWT bearer token (`RequireAuthorization()` globally), validated against Clerk's JWKS — with exactly one carve-out, `POST /api/webhooks/email`, which uses HTTP Basic instead (see Email Ingestion below). `CurrentMemberService` resolves the acting `Member` server-side from the verified token's subject claim — the client never supplies its own identity or member id.

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
├── Controllers/   EventsController, GroupsController, LocationsController, MembersController,
│                   PendingBookingsController, UsersController, WebhooksController
├── Auth/          ClerkJwksConfigurationRetriever, InboundEmailWebhookAuthFilter (Basic auth
│                   for the inbound-email webhook)
├── Models/        EF Core entities (see Data Model below)
├── DTOs/          Requests.cs, Responses.cs — request/response records
├── Services/       CostService (cost splitting), EventMapper (entity → response DTO mapping),
│                   CurrentMemberService (resolves caller from JWT, membership checks), DebtService
│                   (debts-owed/settle), DebtSimplifier (greedy debt-simplification algorithm),
│                   EmailIngestionService (webhook write path), PendingBookingService (review
│                   queue), GeminiBookingExtractionService (IBookingExtractionService — LLM
│                   extraction), LocationMatcher (pure venue-name matching)
├── Data/          AppDbContext
└── Migrations/    EF Core migrations (applied automatically on startup via db.Database.Migrate())
tests/SportsBookingService.Api.Tests/
```

---

## Data Model

- **Group** — has a unique 6-char join `Code`, owns `Members`, `Locations`, `Events`, and an optional `PrimaryLocationId`.
- **Member** — belongs to exactly one `Group`; has a server-generated `Id` and a `ClerkUserId` linking it to the authenticated caller. `CurrentMemberService` resolves a request's `Member` by looking up `ClerkUserId` against the verified JWT's subject claim.
- **Location** — a saved venue belonging to a `Group`.
- **Event** — belongs to a `Group`; has `CourtBookings`, `Attendees`, `Waitlist`, and `Expenses`.
- **Attendee** / **WaitlistEntry** — join tables with a `JoinedAt` timestamp, used to order waitlist promotion and cost-balance display.
- **Expense** — recorded against an `Event`, paid by one `Member`.
- **PendingBooking** — a forwarded booking-confirmation email awaiting human review. Holds the raw email verbatim (`RawEmailFrom/Subject/Text/Html`, captured *before* extraction so a failed parse loses nothing), the `Extracted*` fields the LLM produced, a best-guess `MatchedLocationId`/`MatchedEventId`, and the review outcome (`Status`, `CreatedEventId`, `ReviewedByMemberId`, `ReviewedAt`). `Status` is `Pending` / `Confirmed` / `Dismissed` / `Failed`.
- **Settlement** — records a settled debt between two `Member`s for a given `Event` (`FromMemberId`, `ToMemberId`, `Amount`, `SettledAt`); once recorded, `DebtService` excludes the corresponding simplified debt from `GetOwedToMemberAsync` results.

---

## Key Conventions

- **Ids**: all resource ids are `Guid`, server-generated.
- **Authorization**: every group-scoped action must verify the caller belongs to that group via `currentMemberService.FindMemberAsync(groupId)` before touching data — this is the fix for the A01 access-control gap (see `docs/reviews/security-assessment-2026-09-07.md` in the workspace root). Convention: `ServiceErrorKind.BadRequest` / inline `BadRequest(new { error = "Member not found in group." })` for GUID-keyed resources; `NotFound()` specifically for `GET /api/groups/code/{code}` so a non-member can't distinguish a bad code from a real one they're not in (prevents 6-char code enumeration).
- **JSON casing**: `camelCase` on the wire (ASP.NET Core default); model/DTO properties are PascalCase in C#.
- **Dates**: `Date` fields are `DateOnly` (`yyyy-MM-dd` strings on the wire). `CreatedAt` is round-trip ISO 8601. `StartTime`/`EndTime` on court bookings are free-form client-owned strings, not `DateTime`.
- **Status codes**: `404` when a parent resource doesn't exist, `400` for invalid references (e.g. a member not in the group), `409` for conflicting state (e.g. double-join). `POST /api/groups/join` is the one exception — a missing code returns `200` with `null`, not `404`, since it's a normal outcome the UI checks for.
- **Nullable reference types** are enabled — no `#nullable disable` workarounds.
- **Response mapping**: controllers never return EF entities directly — always map through `EventMapper` / DTO constructors in `Responses.cs`.

---

## Email Ingestion

Booking confirmations from third-party sites (Yepbooking et al.) are auto-forwarded by each
member's own one-time mail rule into a single shared CloudMailin inbox. Groups are told apart
by a plus-address token, not by separate mailboxes.

Flow: `POST /api/webhooks/email` → `EmailIngestionService` → a `PendingBooking` row → a member
reviews it in the UI → `PendingBookingService.ConfirmAsync` → a real `Event` (+ optional
`Expense`). **Nothing is created automatically** — this is a review queue, not automation.

Non-obvious pieces:

- **`Group.InboundEmailToken`** — a 12-char `[a-z0-9]` token (`GroupService.GenerateInboundToken`),
  spliced into `Email:InboundAddress` as `{localpart}+{token}@{domain}`. Longer than the 6-char
  join `Code` because it's never typed by hand and is the only spam gate on the webhook.
- **The auth carve-out** — `WebhooksController` carries `[AllowAnonymous]` (which overrides the
  global `RequireAuthorization()`) plus `[ServiceFilter(typeof(InboundEmailWebhookAuthFilter))]`
  for HTTP Basic. Both are required: `[AllowAnonymous]` alone would leave it open.
- **Always return 200** — CloudMailin treats non-2xx as a bounce/retry, so an internal failure
  must surface as a `Failed` row, never as a retried email.
- **Persist before extracting** — the raw email is saved first, so a Gemini timeout or garbled
  response still leaves a row the member can transcribe by hand.
- **Extraction is one provider-agnostic LLM call** (`GeminiBookingExtractionService`), not a
  per-provider parser registry — a new booking site needs no code. The prompt states the email
  is untrusted data, since anyone who learns a forwarding address can send to it.
- `LocationMatcher` is a pure static function with no DB dependency; its confidence score is
  informational (it pre-selects a dropdown) and never gates creation.

Config (user-secrets locally, `Section__Key` env vars in prod): `Email:WebhookUsername`,
`Email:WebhookPassword`, `Email:InboundAddress`, `Gemini:ApiKey`. `Gemini:Model` has an
`appsettings.json` default.

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

Email ingestion additionally needs (any placeholder works for the webhook credentials locally;
`Gemini:ApiKey` must be a real free key from [ai.google.dev](https://ai.google.dev) for
extraction to run):

```bash
dotnet user-secrets set "Email:WebhookUsername" "local" --project src/SportsBookingService.Api
dotnet user-secrets set "Email:WebhookPassword" "local" --project src/SportsBookingService.Api
dotnet user-secrets set "Email:InboundAddress" "inbox@cloudmailin.net" --project src/SportsBookingService.Api
dotnet user-secrets set "Gemini:ApiKey" "<free key>" --project src/SportsBookingService.Api
```

Exercise the webhook without a CloudMailin account by POSTing a CloudMailin-shaped body
directly (`<token>` is a real `Groups.InboundEmailToken`):

```bash
curl -u local:local -X POST http://localhost:5224/api/webhooks/email   -H "Content-Type: application/json"   -d '{"envelope":{"to":"inbox+<token>@cloudmailin.net","from":"jane@gmail.com"},
       "headers":{"subject":"Booking confirmation"},
       "plain":"Court 3, Fri 2 Oct 2026, 6-8pm, Melbourne Sports Centre, $48.50"}'
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
6. Add a test when new logic has non-trivial edge cases (e.g. cost splitting, waitlist promotion). Skip tests for straightforward CRUD that just delegates to EF Core.

**Never:**
- Trust a client-supplied member id — always resolve the acting member from the verified JWT via `CurrentMemberService`.
- Add a group-scoped endpoint or service method without a `FindMemberAsync(groupId)` membership check.
- Return EF entities directly from a controller — always map to a DTO.
- Duplicate the cost-splitting formula outside `CostService`.
