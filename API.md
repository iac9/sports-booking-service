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
  joining, leaving, RSVPing) now resolve the acting member server-side
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
| `GET /api/groups/code/{code}` | Look up a group by its 6-character join code (case-insensitive), with the full event graph populated. This is the endpoint the frontend uses to lazy-load full group detail after `GET /api/users/me/groups` returned only a summary. Caller must be a member of the group — since the code is a brute-forceable 6 characters, "no such code" and "code exists but you're not a member" are intentionally indistinguishable (both `404`). Rate-limited to 20 requests/minute per caller — `429` if exceeded. | — | `200` → `GroupResponse` · `404` if no group has that code, or the caller isn't a member · `429` if rate-limited |
| `POST /api/groups` | Create a new group. The creator (the caller) becomes its first member. | `Name` string<br>`CreatorName` string | `201 Created` → `GroupResponse` |
| `PATCH /api/groups/{id}` | Rename a group. Admin-only. | `{ Name: string }` | `200` → `GroupResponse` · `404` if group not found · `403` if caller isn't the group's admin |
| `DELETE /api/groups/{id}` | Delete a group. Admin-only. | — | `204` · `404` if group not found · `403` if caller isn't the group's admin |
| `GET /api/groups/{id}/members` | List a group's members. Caller must be a member. | — | `200` → `MemberResponse[]` · `404` if group not found or caller isn't a member |
| `DELETE /api/groups/{id}/members/me` | Remove the caller from a group (leave-group). If the caller is the group's admin and is the sole member, the entire group (and all its data) is deleted instead. | — | `204` · `404` if caller isn't a member of the group · `409` if the caller is the group's admin and other members remain (must transfer the admin role first, via `PUT /api/groups/{id}/admin`) |
| `PUT /api/groups/{id}/admin` | Transfer the group's admin role to another member. Admin-only. | `{ MemberId: guid }` | `200` → `GroupResponse` · `404` if group not found · `403` if caller isn't the group's admin · `400` if `MemberId` isn't a member of this group |
| `POST /api/groups/join` | Join a group by its join code, as the caller. Idempotent — if the caller already belongs, no change is made. Rate-limited to 20 requests/minute per caller — `429` if exceeded. | `Code` string<br>`MemberName` string | `200` → `GroupResponse` if the code matched, or `null` if no group has that code (never 404 — a missing code is a normal outcome the client checks for) · `429` if rate-limited |
| `PUT /api/groups/{id}/primary-location` | Set (or clear) the group's default/primary location. Caller must be a member. | `{ LocationId: guid \| null }` | `204` · `404` if group not found · `400` if caller isn't a member, or `LocationId` is supplied but isn't one of the group's locations |

## Locations

Base path: `/api/groups/{groupId}/locations`

All endpoints below require the caller to be a member of `groupId` (`400` if not).

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
| `GET /` | List a group's events, ordered by date. Caller must be a member. | — | `200` → `EventSummaryResponse[]` · `400` if caller isn't a member |
| `GET /{id}` | Get a single event. Caller must be a member. | — | `200` → `EventDetailResponse` · `404` if event not found in group · `400` if caller isn't a member |
| `POST /` | Create an event, as the caller. The creator is auto-added as an attendee. | `Title` string<br>`Date` string (`yyyy-MM-dd`)<br>`LocationId` guid?<br>`MaxAttendees` int? (`null` = unlimited attendance)<br>`CourtBookings` `{ Court: string, StartTime: string, EndTime: string }[]` | `201 Created` → `EventDetailResponse` · `404` if group not found · `400` if the caller isn't a member of the group |
| `PATCH /{id}` | Partially update an event. Caller must be a member. Only supplied fields are changed, **except `MaxAttendees`, which is always overwritten with whatever is supplied (including `null`, to clear an existing limit)** — this is the only field where a JSON `null` is distinguishable from "not supplied" in practice, since the frontend always sends it. Supplying `CourtBookings` replaces the entire collection. | `Title` string?<br>`Date` string?<br>`LocationId` guid?<br>`MaxAttendees` int? (`null` = unlimited)<br>`CourtBookings` `{ Court: string, StartTime: string, EndTime: string }[]`? | `200` → `EventDetailResponse` · `404` if event not found in group · `400` if caller isn't a member |
| `DELETE /{id}` | Delete an event. Caller must be a member. | — | `204` · `404` if event not found in group · `400` if caller isn't a member |
| `POST /{id}/rsvp` | Join an event as the caller — added to `Attendees` if under `MaxAttendees`, otherwise to `Waitlist`. If `MaxAttendees` is `null` (unlimited), always added to `Attendees`, never waitlisted. | — | `200` → `EventDetailResponse` · `404` if event not found · `400` if caller not in group · `409` if already joined or waitlisted |
| `DELETE /{id}/rsvp/me` | Leave an event as the caller. If the caller was an attendee, the longest-waiting waitlisted member (if any) is promoted to fill the spot. | — | `200` → `EventDetailResponse` · `404` if event not found |
| `POST /{id}/guests` | Add a guest to an event, hosted by the caller. The guest is a real `Member` (`IsGuest: true`, `AddedByMemberId` = caller's member id) added to `Attendees` if under `MaxAttendees`, otherwise `Waitlist` — same capacity rules as `/rsvp`. If `Name` is omitted/blank, it defaults to `"<caller's name> + n"`, where `n` counts the caller's other guests already on this event. Guests never appear in `GroupResponse.Members` or `GET /api/groups/{id}/members`. | `Name` string? | `200` → `EventDetailResponse` · `404` if event not found · `400` if caller not in group |
| `DELETE /{id}/guests/{guestId}` | Remove a guest from an event. Only the member who added the guest (`AddedByMemberId`) may remove them. If the guest was an attendee, the longest-waiting waitlisted member (if any) is promoted, same as `/rsvp/me`. The guest's `Member` row is deleted entirely. | — | `200` → `EventDetailResponse` · `404` if event or guest not found · `403` if caller didn't add this guest |
| `POST /{id}/expenses` | Record an expense against an event. `PaidByMemberId` names an arbitrary existing group member ("who paid") — not necessarily the caller, but the caller must be a member. | `Description` string<br>`Amount` decimal<br>`PaidByMemberId` guid | `201 Created` → `ExpenseResponse` · `404` if event not found · `400` if caller or `PaidByMemberId` not in group |
| `PATCH /{id}/expenses/{expenseId}` | Partially update an expense. Only supplied fields are changed. Any group member may edit any expense — there's no "added by" restriction. | `Description` string?<br>`Amount` decimal?<br>`PaidByMemberId` guid? | `200` → `ExpenseResponse` · `404` if event or expense not found · `400` if caller or `PaidByMemberId` not in group |
| `DELETE /{id}/expenses/{expenseId}` | Delete an expense. Any group member may delete any expense. | — | `204` · `404` if event or expense not found · `400` if caller not in group |

## Members

Base path: `/api/groups/{groupId}/members/{memberId}`

| Endpoint | Description | Request Body | Response |
|---|---|---|---|
| `GET /debts-owed` | List every unsettled debt owed *to* `memberId`, one row per event per debtor, computed by running the debt-simplification algorithm (greedy largest-creditor/largest-debtor matching) over each event's `CostBreakdown.Balances`. Ordered by event date descending. Settling a debt (see below) removes it from this list permanently — settled debts are excluded, not flagged. Caller must be a member of the group — not necessarily `memberId` itself, since balances are already visible group-wide. | — | `200` → `DebtResponse[]` · `404` if group not found · `400` if caller isn't a member of the group |
| `POST /debts-owed/settle` | Mark a debt as settled — only `memberId` (the caller, resolved from the token) may settle a debt owed to them; `403` otherwise. The settled `Amount` is always computed server-side from the current cost breakdown (the same debt-simplification run as `GET /debts-owed`) — the client does not, and cannot, supply it. Once settled, the debt is excluded from future `GET /debts-owed` calls for good, on both sides of the transaction. Idempotent — settling an already-settled `(EventId, FromMemberId)` pair returns the existing settlement unchanged. | `EventId` guid<br>`FromMemberId` guid | `200` → `DebtResponse` · `404` if group, event, or `FromMemberId` not found · `400` if caller isn't a member of the group, or there's no outstanding debt between the caller and `FromMemberId` for this event · `403` if caller isn't `memberId` |

## Health

| Endpoint | Description | Response |
|---|---|---|
| `GET /health` | Liveness check. Doesn't touch the database. | `200 OK` (no body) |
| `GET /health/db` | Readiness check — verifies the database connection is reachable. | `200 OK` if reachable, `503 Service Unavailable` otherwise (no body) |

## Response shapes

### Group shape
```
MemberResponse       { Id: guid, Name: string, IsGuest: bool, AddedByMemberId: guid? }
                     // IsGuest/AddedByMemberId are only ever true/set on entries inside an
                     // event's Attendees/Waitlist — guests are excluded from GroupResponse.Members
                     // and GET /api/groups/{id}/members entirely.
LocationResponse     { Id: guid, Name: string, Address: string }
CourtBookingResponse { Id: guid, Court: string, StartTime: string, EndTime: string }
BalanceResponse      { Member: MemberResponse, Balance: decimal }
CostBreakdownResponse { TotalCost: decimal, CostPerAttendee: decimal, Balances: BalanceResponse[] }
DebtResponse         { EventId: guid, EventTitle: string, Date: string, FromMember: MemberResponse, Amount: decimal, Settled: bool, SettledAt: string? }

EventSummaryResponse {
  Id: guid, Title: string, Date: string,
  AttendeeCount: int, MaxAttendees: int?, LocationName: string?
}

EventDetailResponse {
  Id: guid, Title: string, Date: string,
  CourtBookings: CourtBookingResponse[],
  LocationId: guid?, LocationName: string?,
  MaxAttendees: int?,  // null = unlimited attendance, no waitlist
  Attendees: MemberResponse[], Waitlist: MemberResponse[],
  Expenses: ExpenseResponse[],
  CreatedBy: MemberResponse,
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
