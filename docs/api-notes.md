# edtime employee API (reverse-engineered)

Source: the employee web app at `https://app.edtime.de/stempelmedien/` (bundle `main.*.js`, v4.25.0)
and live read-only calls, 2026-10-08. Unofficial, so it can change without notice.

Base URL: `https://app.edtime.de/api`

## Auth
`POST /api/v3/auth/stempelmedien/login?scope=UserAuthResponse`
```json
{ "username": "<email or username>", "password": "<password or PIN>", "endpointType": "smartphone" }
```
`endpointType` is `"smartphone"` or `"tablet"`. Response (top level, no `data` wrapper):
```json
{ "token": "<64 hex>", "expires": 1793546010, "role": "employee", "accessToken": "...",
  "userAuth": { "employeeId": 12345, "forcePinChange": false, "hasBrowserEmployeeView": true } }
```
- Every later call sends `Authorization: Bearer <token>`. No cookies are needed. The web app also sends `X-App-Version: 4.25.0`.
- `expires` is a unix timestamp; the token observed was valid for about 24 days.
- 401 or 403 (body `Authentication failed`, not JSON) means the token is bad, so log in again.

## Work status
`GET /api/employees/{eid}/work/status` returns `{ status: {message, exceptions}, data: {...} }`.

Relevant `data` fields:
| field | meaning |
|---|---|
| `state` | `0` not working, `1` working, `2` break, `3` smoker break |
| `total` | net work seconds today, as of the response (breaks excluded) |
| `workinghour.start` / `.end` | start and end of the work day |
| `pause[]` | `{start, end, duration, smoker}`; the running break has `end: null` |
| `pausetotal`, `breakduration` | break seconds so far |
| `activeGroup.id` | group id, needed for the POSTs below |

Dates come as `{ "date": "2026-10-08 08:44:00.000000", "timezone_type": 1, "timezone": "+02:00" }`.

## Stamping
JSON body `{ "groupId": <activeGroup.id>, "timeTypeId": null }`:
- `POST /api/employees/{eid}/work/start` and `/work/end`
- `POST /api/employees/{eid}/pause/start` and `/pause/end`
- `POST /api/employees/{eid}/pause/smoker` starts a smoker break

The response body is the new work-status payload. 409 means a conflict, i.e. the state changed elsewhere.
