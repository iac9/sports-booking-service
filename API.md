# Sports Booking Service — API Reference

This is the canonical source of truth for the contract between `sports-booking-ui` and
`sports-booking-service`. Whenever an endpoint's path, verb, request shape, or response
shape changes, update this file in the same change.

## Conventions

- **Auth**: none. There is no login flow. The client generates a `Guid` (`crypto.randomUUID()`)
  once and persists it in `localStorage` — that GUID is the durable Member/User identity
  across all groups. Endpoints that create a member (`POST /api/groups`,
  `POST /api/groups/{id}/members`, `POST /api/groups/join`) accept this client-supplied id
  rather than generating one server-side.
- **JSON casing**: request and response bodies use `camelCase` field names (ASP.NET Core's
  default `System.Text.Json` naming policy). The tables below use the C# record property
  names (PascalCase); translate to `camelCase` on the wire (e.g. `CreatorId` → `creatorId`).
- **Dates**: `Date` fields are `yyyy-MM-dd` strings (`DateOnly`). `CreatedAt` is round-trip
  ("o") ISO 8601. `StartTime`/`EndTime` on court bookings are free-form strings owned by the client.
- **Ids**: all resource ids are `Guid`.
- **Not found / validation**: 404 when the parent resource (group/event/location/member)
  doesn't exist; 400 for invalid references (e.g. a member not in the group); 409 for
  conflicting state (e.g. voting twice, joining twice).

## Users

| Endpoint | Description | Response |
|---|---|---|
| `GET /api/users/{userId}/groups` | Returns every group the given member belongs to. | `200` → `GroupResponse[]` (see [Group shape](#group-shape)) |

## Groups

| Endpoint | Description | Request Body | Response |
|---|---|---|---|
| `GET /api/groups/code/{code}` | Look up a group by its 6-character join code (case-insensitive). | — | `200` → `GroupResponse` · `404` if no group has that code |
| `POST /api/groups` | Create a new group. The creator becomes its first member. | `Name` string<br>`CreatorId` guid<br>`CreatorName` string | `201 Created` → `GroupResponse` |
| `PATCH /api/groups/{id}` | Rename a group. | `{ Name: string }` | `200` → `GroupResponse` · `404` if group not found |
| `DELETE /api/groups/{id}` | Delete a group. | — | `204` · `404` if group not found |
| `GET /api/groups/{id}/members` | List a group's members. | — | `200` → `MemberResponse[]` |
| `POST /api/groups/{id}/members` | Add a member directly to a group by id (used for id-based joins; prefer `POST /api/groups/join` for code-based joins). | `MemberId` guid<br>`Name` string | `201 Created` → `MemberResponse` · `404` if group not found |
| `DELETE /api/groups/{id}/members/{memberId}` | Remove a member from a group (leave-group). | — | `204` · `404` if member not found in group |
| `POST /api/groups/join` | Join a group by its join code. Idempotent — if the member already belongs, no change is made. | `Code` string<br>`MemberId` guid<br>`MemberName` string | `200` → `GroupResponse` if the code matched, or `null` if no group has that code (never 404 — a missing code is a normal outcome the client checks for) |
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
| `POST /` | Create an event. `Status` is `"draft"` (still choosing a location, voting open) or `"confirmed"` (location and attendees locked in). When `Confirmed`, the creator is auto-added as an attendee; when `Draft`, `LocationOptions` become votable options. | `Title` string<br>`Date` string (`yyyy-MM-dd`)<br>`LocationId` guid?<br>`MaxAttendees` int<br>`CreatedByMemberId` guid<br>`Status` `"draft"` \| `"confirmed"`<br>`CourtBookings` `{ Court: string, StartTime: string, EndTime: string }[]`<br>`LocationOptions` `{ LocationName: string }[]`? | `201 Created` → `EventDetailResponse` · `404` if group not found · `400` if `CreatedByMemberId` isn't a member of the group |
| `PATCH /{id}` | Partially update an event. Only supplied fields are changed. Supplying `CourtBookings` or `LocationOptions` replaces the entire respective collection (existing votes on location options are preserved by matching on `LocationName`). | `Title` string?<br>`Date` string?<br>`LocationId` guid?<br>`MaxAttendees` int?<br>`CourtBookings` `{ Court: string, StartTime: string, EndTime: string }[]`?<br>`LocationOptions` `{ Id: guid?, LocationName: string }[]`? | `200` → `EventDetailResponse` · `404` if event not found in group |
| `DELETE /{id}` | Delete an event. | — | `204` · `404` if event not found in group |
| `POST /{id}/rsvp` | Join an event — added to `Attendees` if under `MaxAttendees`, otherwise to `Waitlist`. | `{ MemberId: guid }` | `200` → `EventDetailResponse` · `404` if event not found · `400` if member not in group · `409` if already joined or waitlisted |
| `DELETE /{id}/rsvp/{memberId}` | Leave an event. If the member was an attendee, the longest-waiting waitlisted member (if any) is promoted to fill the spot. | — | `200` → `EventDetailResponse` · `404` if event not found |
| `POST /{id}/expenses` | Record an expense against an event. | `Description` string<br>`Amount` decimal<br>`PaidByMemberId` guid | `201 Created` → `ExpenseResponse` · `404` if event not found · `400` if `PaidByMemberId` not in group |
| `POST /{id}/confirm` | Confirm a draft event: the location option with the most votes wins, all voters/attendees are merged into the final attendee list (plus the confirming member), and location options are cleared. | `{ MemberId: guid }` | `200` → `EventDetailResponse` · `404` if event not found or not in `Draft` status |
| `POST /{id}/location-options/{optionId}/votes` | Vote for a location option. | `{ MemberId: guid }` | `200` → `EventDetailResponse` · `404` if option not found · `409` if already voted |
| `DELETE /{id}/location-options/{optionId}/votes/{memberId}` | Remove a vote. | — | `200` → `EventDetailResponse` · `404` if no such vote exists |

## Health

| Endpoint | Description | Response |
|---|---|---|
| `GET /health` | Liveness check, unauthenticated. | `200 OK` (no body) |

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
  PrimaryLocationId: guid?
}
```
