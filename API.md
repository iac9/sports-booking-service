# Sports Booking Service — API Reference

This is the canonical source of truth for the contract between `sports-booking-ui` and
`sports-booking-service`. Whenever an endpoint's path, verb, request shape, or response
shape changes, update this file in the same change.

## Conventions

- **Auth**: every endpoint except `GET /health` and `GET /health/db` requires `Authorization: Bearer <token>`,
  where `<token>` is a Clerk session JWT. The token is verified server-side against Clerk's
  JWKS (`{Clerk:FrontendApiUrl}/.well-known/jwks.json`); requests with a missing or invalid
  token get `401`. The token's `sub` claim is the caller's durable Clerk user id, stored on
  `Members.ClerkUserId`. `Members.Id` is a server-generated `Guid` — a person gets a fresh
  `Member` row (and `Id`) per group they join, unified across groups only via `ClerkUserId`.
  Endpoints that used to accept a client-supplied id for "who is acting" (creating a group,
  joining, leaving, RSVPing, confirming, voting) now resolve the acting member server-side
  from the verified token instead. `AddExpense`'s `PaidByMemberId` is the exception — it names
  an arbitrary existing group member ("who paid"), not necessarily the caller, so it's still
  client-supplied.
- **JSON casing**: request and response bodies use `camelCase` field names (ASP.NET Core's
  default `System.Text.Json` naming policy). The tables below use the C# record property
  names (PascalCase); translate to `camelCase` on the wire (e.g. `CreatorName` → `creatorName`).
- **Dates**: `Date` fields are `yyyy-MM-dd` strings (`DateOnly`). `CreatedAt` is round-trip
  ("o") ISO 8601. `StartTime`/`EndTime` on court bookings are free-form strings owned by the client.
- **Ids**: all resource ids are `Guid`.
- **Not found / validation**: 404 when the parent resource (group/event/location/member)
  doesn't exist; 400 for invalid references (e.g. a member not in the group); 409 for
  conflicting state (e.g. voting twice, joining twice).

## Users

| Endpoint | Description | Response |
|---|---|---|
| `GET /api/users/me/groups` | Returns every group the caller (resolved from the token's `sub` claim) belongs to, as lightweight summaries — `Members`, `Locations`, and `Events` are omitted entirely; only a `MemberCount` is included. Use `GET /api/groups/code/{code}` to fetch full detail (members, locations, and events) for a specific group. | `200` → `GroupSummaryResponse[]` (see [Group shape](#group-shape)) |

## Groups

| Endpoint | Description | Request Body | Response |
|---|---|---|---|
| `GET /api/groups/code/{code}` | Look up a group by its 6-character join code (case-insensitive), with the full event graph populated. This is the endpoint the frontend uses to lazy-load full group detail after `GET /api/users/me/groups` returned only a summary. | — | `200` → `GroupResponse` · `404` if no group has that code |
| `POST /api/groups` | Create a new group. The creator (the caller) becomes its first member. | `Name` string<br>`CreatorName` string | `201 Created` → `GroupResponse` |
| `PATCH /api/groups/{id}` | Rename a group. Admin-only. | `{ Name: string }` | `200` → `GroupResponse` · `404` if group not found · `403` if caller isn't the group's admin |
| `DELETE /api/groups/{id}` | Delete a group. Admin-only. | — | `204` · `404` if group not found · `403` if caller isn't the group's admin |
| `GET /api/groups/{id}/members` | List a group's members. | — | `200` → `MemberResponse[]` |
| `DELETE /api/groups/{id}/members/me` | Remove the caller from a group (leave-group). If the caller is the group's admin and is the sole member, the entire group (and all its data) is deleted instead. | — | `204` · `404` if caller isn't a member of the group · `409` if the caller is the group's admin and other members remain (must transfer the admin role first, via `PUT /api/groups/{id}/admin`) |
| `PUT /api/groups/{id}/admin` | Transfer the group's admin role to another member. Admin-only. | `{ MemberId: guid }` | `200` → `GroupResponse` · `404` if group not found · `403` if caller isn't the group's admin · `400` if `MemberId` isn't a member of this group |
| `POST /api/groups/join` | Join a group by its join code, as the caller. Idempotent — if the caller already belongs, no change is made. | `Code` string<br>`MemberName` string | `200` → `GroupResponse` if the code matched, or `null` if no group has that code (never 404 — a missing code is a normal outcome the client checks for) |
| `PUT /api/groups/{id}/primary-location` | Set (or clear) the group's default/primary location. | `{ LocationId: guid \| null }` | `204` · `404` if group not found · `400` if `LocationId` is supplied but isn't one of the group's locations |

## Locations

Base path: `/api/groups/{groupId}/locations`

| Endpoint | Description | Request Body | Response |
|---|---|---|---|
| `GET /` | List a group's locations, ordered by name. | — | `200` → `LocationResponse[]` |
| `POST /` | Add a location to a group. | `{ Name: string, Address: string }` | `201 Created` → `LocationResponse` · `404` if group not found |
| `PATCH /{id}` | Partially update a location — only supplied fields are changed. | `{ Name?: string, Address?: string }` | `200` → `LocationResponse` · `404` if location not found in group |
| `DELETE /{id}` | Delete a location. | — | `204` · `404` if location not found in group |

## Events

Base path: `/api/groups/{groupId}/events`

| Endpoint | Description | Request Body | Response |
|---|---|---|---|
| `GET /?status=draft\|confirmed` | List a group's events (optionally filtered by status), ordered by date. | — | `200` → `EventSummaryResponse[]` |
| `GET /{id}` | Get a single event. | — | `200` → `EventDetailResponse` · `404` if event not found in group |
| `POST /` | Create an event, as the caller. `Status` is `"draft"` (still choosing a location, voting open) or `"confirmed"` (location and attendees locked in). When `Confirmed`, the creator is auto-added as an attendee; when `Draft`, `LocationOptions` become votable options. | `Title` string<br>`Date` string (`yyyy-MM-dd`)<br>`LocationId` guid?<br>`MaxAttendees` int<br>`Status` `"draft"` \| `"confirmed"`<br>`CourtBookings` `{ Court: string, StartTime: string, EndTime: string }[]`<br>`LocationOptions` `{ LocationName: string }[]`? | `201 Created` → `EventDetailResponse` · `404` if group not found · `400` if the caller isn't a member of the group |
| `PATCH /{id}` | Partially update an event. Only supplied fields are changed. Supplying `CourtBookings` or `LocationOptions` replaces the entire respective collection (existing votes on location options are preserved by matching on `LocationName`). | `Title` string?<br>`Date` string?<br>`LocationId` guid?<br>`MaxAttendees` int?<br>`CourtBookings` `{ Court: string, StartTime: string, EndTime: string }[]`?<br>`LocationOptions` `{ Id: guid?, LocationName: string }[]`? | `200` → `EventDetailResponse` · `404` if event not found in group |
| `DELETE /{id}` | Delete an event. | — | `204` · `404` if event not found in group |
| `POST /{id}/rsvp` | Join an event as the caller — added to `Attendees` if under `MaxAttendees`, otherwise to `Waitlist`. | — | `200` → `EventDetailResponse` · `404` if event not found · `400` if caller not in group · `409` if already joined or waitlisted |
| `DELETE /{id}/rsvp/me` | Leave an event as the caller. If the caller was an attendee, the longest-waiting waitlisted member (if any) is promoted to fill the spot. | — | `200` → `EventDetailResponse` · `404` if event not found |
| `POST /{id}/expenses` | Record an expense against an event. `PaidByMemberId` names an arbitrary existing group member ("who paid") — not necessarily the caller. | `Description` string<br>`Amount` decimal<br>`PaidByMemberId` guid | `201 Created` → `ExpenseResponse` · `404` if event not found · `400` if `PaidByMemberId` not in group |
| `POST /{id}/confirm` | Confirm a draft event as the caller: the location option with the most votes wins, all voters/attendees are merged into the final attendee list (plus the confirming caller), and location options are cleared. | — | `200` → `EventDetailResponse` · `404` if event not found, caller not a member, or event not in `Draft` status |
| `POST /{id}/location-options/{optionId}/votes` | Vote for a location option as the caller. | — | `200` → `EventDetailResponse` · `404` if option not found · `409` if already voted |
| `DELETE /{id}/location-options/{optionId}/votes/me` | Remove the caller's vote. | — | `200` → `EventDetailResponse` · `404` if no such vote exists |

## Health

| Endpoint | Description | Response |
|---|---|---|
| `GET /health` | Liveness check. Doesn't touch the database. | `200 OK` (no body) |
| `GET /health/db` | Readiness check — verifies the database connection is reachable. | `200 OK` if reachable, `503 Service Unavailable` otherwise (no body) |

## Response shapes

### Group shape
```
MemberResponse       { Id: guid, Name: string }
LocationResponse     { Id: guid, Name: string, Address: string }
CourtBookingResponse { Id: guid, Court: string, StartTime: string, EndTime: string }
LocationOptionResponse { Id: guid, LocationName: string, Votes: MemberResponse[] }
BalanceResponse      { Member: MemberResponse, Balance: decimal }
CostBreakdownResponse { TotalCost: decimal, CostPerAttendee: decimal, Balances: BalanceResponse[] }

EventSummaryResponse {
  Id: guid, Title: string, Date: string, Status: "draft" | "confirmed",
  AttendeeCount: int, MaxAttendees: int, LocationName: string?
}

EventDetailResponse {
  Id: guid, Title: string, Date: string,
  CourtBookings: CourtBookingResponse[],
  LocationId: guid?, LocationName: string?,
  MaxAttendees: int,
  Attendees: MemberResponse[], Waitlist: MemberResponse[],
  Expenses: ExpenseResponse[],
  CreatedBy: MemberResponse,
  Status: "draft" | "confirmed",
  LocationOptions: LocationOptionResponse[]?,
  CostBreakdown: CostBreakdownResponse
}

ExpenseResponse { Id: guid, Description: string, Amount: decimal, PaidBy: MemberResponse, CreatedAt: string }

GroupResponse {
  Id: guid, Name: string, Code: string,
  Members: MemberResponse[],
  Events: EventDetailResponse[],
  Locations: LocationResponse[],
  PrimaryLocationId: guid?,
  CurrentMemberId: guid?,  // the caller's own Member.Id in this group; null if not a member
  AdminMemberId: guid  // the Member.Id of the group's admin — the member who created it; only the admin may rename or delete the group
}

GroupSummaryResponse {
  Id: guid, Name: string, Code: string,
  MemberCount: int
}
```
