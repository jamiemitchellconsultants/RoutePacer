# RoutePacer Public Handoff Relay Design

**Date:** 2026-08-27

**Status:** Approved in chat

## 1. Goal

Let a rider create a timed GPX in a private RouteTimer deployment, scan a QR code on the phone used
for RoutePacer, and arrive at a ready-to-start route without making RouteTimer publicly reachable.

RouteTimer uploads outbound over HTTPS to a public, same-origin RoutePacer relay. RoutePacer verifies
the signed invocation, consumes the relay payload once, imports it through the existing GPX pipeline,
and retains the imported route in IndexedDB. Manual GPX/FIT import, the offline application shell,
the route library, ride recording, and active-screen tracking remain independent of the relay.

The authoritative coordinating design is RouteTimer's
`docs/superpowers/specs/2026-08-27-open-in-pacetracker-design.md`. This design freezes the RoutePacer
side of that cross-repository contract.

## 2. Selected Approach

Use one .NET 10 ASP.NET Core container to serve both the Blazor WebAssembly PWA and the relay API,
matching RouteTimer's deployment pattern. A dedicated containerized PostgreSQL database stores
short-lived relay payloads. Shared Caddy terminates public TLS and proxies the complete
`pacetracking.tqaentry.com` origin to ASP.NET Core.

This boundary keeps same-origin routing, SPA fallback, sensitive request filtering, health checks,
and feature configuration in one host. The PWA is still a standalone offline client after it has
downloaded its application shell.

### 2.1 Alternatives not selected

**Separate PWA and API containers behind Caddy** would allow independent scaling, but would split
path routing, header policy, deployment ordering, and sensitive logging controls without a current
need.

**Caddy serving the PWA directly and proxying only the relay API** would avoid serving static files
through ASP.NET Core, but would divide responsibility for `/open`, SPA fallback, service-worker cache
policy, and query-string redaction between two layers.

**A public RouteTimer payload endpoint** is invalid because RouteTimer remains private and the phone
must never need to call it. Public tunnels, VPNs, LAN URLs, inline GPX QR data, and public RouteTimer
ingress are not prerequisites or fallbacks for Contract v1.

**End-to-end encrypted relay content** is deferred. Contract v1 deliberately permits readable GPX
in the relay for at most ten minutes and documents the privacy consequence.

## 3. Architecture and Deployment Topology

The planned solution has these principal projects:

- `RoutePacer.Core` owns route parsing, normalization, matching, pacing, and domain contracts.
- `RoutePacer.App` is the Blazor WebAssembly PWA. It owns browser invocation handling, public-key
  verification, bounded payload download, IndexedDB import, manual import, and tracking UI.
- `RoutePacer.Server` serves the published PWA, relay endpoints, health endpoints, rate limiting,
  safe request logging, configuration, and SPA fallback.
- `RoutePacer.Persistence` owns the PostgreSQL model, migrations, atomic relay operations, and expiry
  cleanup.

Production Docker Compose contains `routepacer` and `routepacer-db`. PostgreSQL publishes no host
port and joins only an internal RoutePacer network. The application joins that network and the
existing external Caddy network. Neither service publishes a host port. The firewall/router forwards
public ports 80 and 443 to Caddy, and Caddy proxies the entire `pacetracking.tqaentry.com` site to
`routepacer:8080`.

PostgreSQL uses a named volume. The volume preserves an unconsumed handoff across an application or
database-container restart and gives every future application replica the same store. It is not a
business-data archive and is never backed up.

`/health/live` reports process liveness without probing dependencies. `/health/ready` reports healthy
only after PostgreSQL is reachable and the current migration has completed. Startup migrations use a
PostgreSQL advisory lock so multiple application replicas cannot migrate concurrently.

## 4. Relay Creation Contract

The production creation request is frozen as:

```http
POST /api/handoffs
Authorization: Bearer <configured-relay-upload-key>
Content-Type: application/gpx+xml
Cache-Control: no-store

<raw timed GPX bytes>
```

Successful creation returns:

```http
HTTP/1.1 201 Created
Content-Type: application/json
Cache-Control: no-store

{
  "payloadUrl": "https://pacetracking.tqaentry.com/api/handoffs/<43-character-token>",
  "expiresAt": "<UTC ISO-8601 instant exactly ten minutes after creation>"
}
```

Creation obeys these rules:

1. The body must be non-empty and no larger than 52,428,800 bytes.
2. The media type must be exactly `application/gpx+xml`; media-type parameters are not accepted.
3. The server generates 32 cryptographically random token bytes and encodes them as 43-character
   unpadded base64url.
4. PostgreSQL stores only `SHA-256(token)`, never the token or payload URL.
5. The server fixes expiry at exactly ten minutes after its injected `TimeProvider` creation time.
   The caller cannot supply or extend a lifetime.
6. Missing or invalid upload credentials return `401`, oversized bodies return `413`, an invalid
   media type returns `415`, and exceeded upload rate limits return `429`.
7. The presented and configured bearer credentials are independently hashed from their UTF-8 bytes
   with SHA-256, then their fixed-size digests are compared with a constant-time primitive. This
   avoids a credential-length-dependent comparison path.
8. `Authorization` and sensitive request data are removed before application and ingress logging.
9. Request and response caching is forbidden with `Cache-Control: no-store`.

The server applies the body limit while streaming and does not allocate an unbounded request buffer.
Authentication, media-type validation, and rate limiting occur before payload persistence. A failed
creation does not leave a database row.

## 5. Relay Storage and Consumption

The handoff table contains only:

- the fixed-size token SHA-256 digest, used as the primary lookup key;
- the exact GPX bytes;
- the creation instant; and
- the expiry instant.

There is no plaintext token, payload URL, route name, source metadata, consumption timestamp,
tombstone, or handoff audit record.

Consumption is frozen as:

```http
GET /api/handoffs/<43-character-token>
```

The endpoint accepts only the exact 43-character unpadded base64url token shape. It hashes the token
and executes one PostgreSQL `DELETE ... RETURNING` statement whose predicate also requires the expiry
to be later than the database transaction time. The successful transaction returns the exact GPX
bytes while deleting the row immediately.

The first request before expiry returns:

- `200 OK`;
- the exact uploaded bytes;
- `Content-Type: application/gpx+xml`;
- the exact `Content-Length`;
- `Cache-Control: no-store`;
- `Pragma: no-cache`; and
- `X-Content-Type-Options: nosniff`.

Malformed, unknown, expired, and already-consumed tokens all return the same `404` status, headers,
and safe empty response shape. Two concurrent consumers race on the same atomic deletion, producing
exactly one `200` and one `404`. There is no automatic redirect and no metadata disclosure.

A hosted worker deletes expired unconsumed rows periodically. Creation and consumption may also run
bounded opportunistic expiry deletion so cleanup does not depend exclusively on the worker schedule.
No consumed-row cleanup exists because successful consumption deletes immediately.

The dedicated PostgreSQL database and named volume are excluded from all backup jobs. No dump,
snapshot, replica archive, write-ahead-log archive, or other retention mechanism may preserve GPX
content beyond the contract lifetime.

## 6. RouteTimer Contract v1

RouteTimer displays a locally generated QR containing a signed RoutePacer URL with these query keys:

```text
https://pacetracking.tqaentry.com/open
  ?src=rt
  &v=1
  &payload=<absolute-https-relay-payload-url>
  &name=<route-name>
  &ts=<unix-milliseconds>
  &sig=<base64url-signature>
```

Every key is required exactly once, including `name`, whose value may be empty. Additional,
duplicate, or missing keys are invalid. RoutePacer requires `src=rt` and `v=1`.

The canonical signature bytes are UTF-8 with a single line feed between fields and no trailing line
feed:

```text
rt\n1\n{payload-absolute-uri}\n{name-or-empty}\n{unix-milliseconds}
```

RouteTimer signs the unescaped values before percent-encoding each query value exactly once.
RoutePacer percent-decodes once, reconstructs the same canonical value, and verifies ECDSA P-256 with
SHA-256. Signature bytes are fixed-width IEEE-P1363 `r || s` encoded as unpadded base64url.

RouteTimer owns the private signing key. RoutePacer publishes only the configured public JWK. The
relay neither signs nor verifies invocation URLs.

Before any fetch, RoutePacer must:

1. parse all fields strictly and without logging the request target;
2. reject timestamps more than ten minutes old or more than sixty seconds in the future;
3. require `payload` to be an absolute HTTPS URL on the exact
   `https://pacetracking.tqaentry.com` origin;
4. require no user information, password, query, or fragment in the payload URL;
5. require the path `/api/handoffs/{43-character-base64url-token}` exactly; and
6. verify the signature against the configured public JWK.

Origin comparison uses normalized URI scheme, host, and effective port. It does not use suffix,
substring, DNS, or configurable-host matching in the browser.

## 7. `/open` Intake and Import Flow

The `/open` page has explicit validating, downloading, importing, ready, and safe-failure states.
Signature and timestamp validation complete before a payload request starts.

After validation, the client issues one same-origin GET. It requires a successful response with
media type exactly `application/gpx+xml`. It rejects a declared `Content-Length` greater than
52,428,800 bytes and independently wraps the response in a counting stream that fails as soon as the
same limit is exceeded. The service worker does not cache or intercept the request as application
content.

The exact bytes enter the same GPX parser, normalization service, and transactional IndexedDB route
repository used by manual file import. The optional signed `name` supplies the proposed imported
route name; it never influences parsing or HTML rendering as markup. Persistence completes before the
page presents the route as ready to start.

There is no automatic second GET. Retry is offered only when the client can prove payload consumption
was never attempted, such as an invalid local public-key configuration or a confirmed offline state
before sending. Once the GET begins, every subsequent failure is terminal because the server may
already have deleted the row. The safe failure state explains that a new code may be required and
offers manual GPX import.

After terminal success or failure, the client calls `history.replaceState` to remove the complete
query string from browser history. The parsed request is kept in memory only as long as needed for the
current page state. Refresh cannot silently repeat the import.

The PWA's app shell remains offline-capable. Manual GPX/FIT imports, IndexedDB route reuse, active ride
tracking, ride history, and deletion remain fully functional without the relay or network after the
app has been installed or loaded successfully once.

## 8. Feature Controls and Configuration

Relay uploads and RouteTimer intake are independently disableable:

- a server-side relay-upload flag controls authenticated `POST /api/handoffs` creation;
- a public PWA intake flag controls whether `/open` processes RouteTimer invocations.

Disabling new uploads does not invalidate an outstanding anonymous GET. Existing handoffs may be
consumed or expire. Disabling intake prevents the PWA from validating or fetching a handoff and shows
a safe manual-import path.

Tracked production configuration keeps both controls disabled. When relay uploads are enabled, server
startup requires a non-empty upload credential and valid PostgreSQL settings. When intake is enabled,
the client configuration must contain a valid P-256 public JWK. Invalid enabled configuration fails
closed rather than starting a partially configured feature.

Production supplies these values outside source control:

- the relay upload credential to RoutePacer Server and private RouteTimer;
- PostgreSQL credentials to RoutePacer Server and the database container;
- RouteTimer's P-256 private key only to private RouteTimer; and
- RouteTimer's public JWK to the RoutePacer PWA configuration.

The public JWK is deliberately public. No symmetric invocation secret or private signing key is
compiled into or served under `wwwroot`.

## 9. Logging, Caching, and Privacy

RoutePacer's privacy documentation must no longer claim that all route data always remains on-device.
It states clearly:

- manual GPX/FIT import, imported routes, rides, and live tracking data remain on-device;
- an explicit RouteTimer handoff sends readable GPX route/location data through the public relay;
- TLS protects the data in transit;
- the dedicated PostgreSQL store holds unconsumed plaintext for no more than ten minutes;
- successful consumption deletes the database row immediately;
- expired unconsumed data is deleted automatically; and
- relay data is never backed up.

Application logs, Caddy logs, ingress logs, traces, metrics dimensions, exception messages, health
responses, and error responses must never contain:

- upload credentials or `Authorization` values;
- payload tokens, token hashes, or payload URLs;
- `/open` invocation query strings or full invocation URLs;
- signatures or public-key-derived identifiers;
- route names; or
- GPX bytes or excerpts.

Sensitive request filtering happens before ordinary HTTP request logging. Access logging for
`/api/handoffs/*` paths and `/open` query strings is disabled or redacted in both ASP.NET Core and
Caddy. Aggregate response-code counts, expiry cleanup counts, payload byte totals, liveness, and
readiness are allowed only without per-handoff labels.

The service worker excludes `/api`, `/health`, and `/open` invocation requests from runtime caching.
Relay responses and creation responses use `no-store`; the browser must not turn them into offline
assets.

## 10. Failure Semantics

Relay failures expose stable status codes without sensitive detail. The creation endpoint uses the
frozen `401`, `413`, `415`, and `429` statuses and safe generic bodies. Persistence or availability
failures return a safe server error without revealing tokens, SQL, payload metadata, or configuration.

Consumption intentionally collapses every non-success case to `404`. Cleanup lag cannot make an
expired token consumable because expiry is part of the atomic deletion predicate.

The PWA maps parse, expiry, signature, origin, download, size, media-type, GPX parse, and IndexedDB
failures to a small safe error set. It does not echo rejected query values. A failure after GET begins
does not retry and directs the rider to create a new RouteTimer code or use manual import.

## 11. Deployment and Enablement

Deployment follows RouteTimer's `deploy/README.md` pattern. RoutePacer provides a production Compose
file, a Caddy fragment, an environment example containing placeholders only, and a concise deployment
runbook.

The production sequence is:

1. Build, test, scan, and publish an immutable RoutePacer container image.
2. Generate and provision the PostgreSQL password and relay upload credential outside the repository.
3. Generate RouteTimer's P-256 signing key outside both repositories, keep its private key in
   RouteTimer, and configure its public JWK in RoutePacer.
4. Keep RoutePacer relay uploads, RoutePacer intake, and RouteTimer handoff disabled.
5. Set the intended immutable `ROUTEPACER_IMAGE_TAG`.
6. Run `docker compose -f deploy/docker-compose.yml up -d --pull always --wait`.
7. Copy the RoutePacer Caddy fragment to the shared ingress configuration, validate the complete Caddy
   configuration, and reload Caddy without restarting it.
8. Confirm `https://pacetracking.tqaentry.com/health/ready` returns `200`.
9. Run fixed valid/tampered contract fixtures and the production-like relay test with controlled
   feature enablement.
10. Enable RoutePacer intake and relay uploads first, then enable private RouteTimer handoff.
11. Scan a real QR on the rider's phone and prove import, ready-to-start state, immediate row deletion,
    second-fetch `404`, expiry cleanup, private-only RouteTimer networking, and manual fallback.

There is no rollback plan, previous-image procedure, database restore path, or GPX backup. A failed
deployment remains disabled, is corrected forward, and is redeployed. The dedicated relay database
is disposable and may be recreated when schema recovery is necessary; losing outstanding handoffs is
acceptable because RouteTimer can create new ones.

## 12. Testing Strategy

### 12.1 Shared contract fixtures

Both repositories contain the same fixed valid and tampered Contract v1 fixtures. A fixture includes
the fixture version, public JWK, canonical UTF-8 text, relay payload URL, route name, timestamp,
fixed-width P1363 signature, and complete invocation URL. Test-only private material may exist only
under a test fixture directory and never under `wwwroot` or production configuration.

### 12.2 Unit and component tests

Unit tests cover:

- missing, duplicate, additional, and malformed query fields;
- canonical UTF-8 construction and percent-decoding exactly once;
- old and future timestamp boundaries;
- valid and tampered P-256 verification;
- exact-origin and exact-path payload allowlisting;
- media-type enforcement;
- declared and streamed body-size limits;
- safe retry classification; and
- URL cleanup after terminal outcomes.

bUnit tests cover `/open` loading, ready-to-start success, safe failure, pre-consumption retry,
terminal post-GET failure, shared import orchestration, URL cleanup, IndexedDB persistence, and manual
fallback.

### 12.3 PostgreSQL and API tests

Real PostgreSQL tests, using Testcontainers where appropriate, prove:

- only token hashes are persisted;
- expiry is enforced inside the atomic consume statement;
- expired rows are cleaned up;
- restart durability and shared access from multiple application instances;
- `DELETE ... RETURNING` removes a successful handoff immediately; and
- two concurrent consumers produce exactly one success and one indistinguishable miss.

API tests cover every creation and consumption status, exact headers, fixed ten-minute lifetime,
missing and invalid authentication, constant-time comparison boundaries, empty and oversized bodies,
strict GPX media type, rate limiting, no-store behavior, safe `404`s, independent feature controls,
liveness, and migration-gated readiness.

### 12.4 Browser, deployment, and privacy tests

Playwright covers real-browser signature verification, the `/open` state flow, one payload fetch,
ready-to-start navigation, URL cleanup, IndexedDB persistence, offline use after import, service-worker
exclusions, and preserved manual import.

Deployment tests validate both Compose files, internal-only PostgreSQL networking, the external Caddy
network, container health transitions, disabled-by-default configuration, secret injection, and the
Caddy fragment.

Repository and captured-log scans prove no private signing key or relay upload credential is served
under `wwwroot`, and no credential, token, payload URL, invocation query, signature, route name, or GPX
content appears in logs.

### 12.5 Production-like acceptance

RouteTimer is reachable only through a private address and performs outbound HTTPS to the public
relay. A phone-context RoutePacer page opens the signed URL and imports the exact timed GPX. The test
then proves that the database row no longer exists and a second GET returns the same safe `404` as an
invalid token.

## 13. Acceptance Criteria

The design is satisfied when:

1. RouteTimer remains private and exposes no public payload route.
2. RouteTimer uploads exact timed GPX bytes outbound to the frozen public relay contract.
3. The relay stores only a token hash and readable GPX for no more than ten minutes.
4. Successful consumption atomically returns the exact bytes and deletes the row immediately.
5. Concurrent or repeated consumption yields exactly one success and otherwise indistinguishable
   `404` responses.
6. RoutePacer verifies Contract v1 with RouteTimer's public P-256 JWK before fetching.
7. The phone fetches only from the exact RoutePacer origin, imports through the shared IndexedDB
   pipeline, cleans its URL, and offers the ready-to-start route.
8. Manual GPX/FIT import, offline startup, route reuse, ride persistence, and tracking are preserved.
9. Privacy documentation accurately discloses temporary plaintext relay processing and immediate or
   expiry-based deletion.
10. Secrets, tokens, payload URLs, invocation values, route names, and GPX content are absent from
    public assets and logs.
11. RoutePacer deploys as one ASP.NET Core container with a dedicated internal PostgreSQL container
    behind shared Caddy, following RouteTimer's deployment pattern.
12. The deployment is forward-only, has no rollback or backup procedure, and remains disabled until
    fixtures and production-like acceptance pass.

## 14. Out of Scope

- Public ingress to RouteTimer.
- A phone connection to RouteTimer, localhost, or a private LAN address.
- End-to-end encryption of Contract v1 relay content.
- Web Share Target intake as part of the relay MVP.
- A relay backup, restore, audit trail, consumed-handoff tombstone, or rollback plan.
- Background mobile tracking with the screen off.
- Changes to route matching, pacing semantics, manual import formats, or IndexedDB ownership after
  import.
