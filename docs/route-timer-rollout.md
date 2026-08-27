# Coordinated RouteTimer rollout

RoutePacer and RouteTimer are deployed separately and must be enabled in a fixed order. Both handoff
controls ship **disabled**, so every step below is safe to stop at. There is no rollback, no relay backup,
and no restore procedure: failures are corrected forward and redeployed, and the disposable handoff
database may be recreated.

## Order of operations

1. **Publish RoutePacer with both controls off.** Deploy per `deploy/README.md` with
   `ROUTEPACER_RELAY_UPLOADS_ENABLED=false` and `ROUTEPACER_ROUTE_TIMER_INTAKE_ENABLED=false`.
   Confirm `https://pacetracking.tqaentry.com/health/ready` returns `200` and the PWA installs and starts
   offline after a first load.
2. **Provision shared secrets outside source control.** Generate the relay upload credential and a P-256
   key pair for RouteTimer. RoutePacer receives **only** the public JWK; RouteTimer holds the private key
   and the upload credential.
3. **Configure RoutePacer with the public JWK**, still disabled. Restart and confirm
   `GET /api/config/route-timer-invocation` still returns `{"enabled":false}`.
4. **Configure private RouteTimer** with the upload credential and private key, its handoff still disabled.
5. **Run the shared fixtures.** Verify `docs/contracts/fixtures/route-timer-contract-v1.json` is
   byte-identical in both repositories and that both suites pass against it.
6. **Run the production-like flow** from a private RouteTimer to the public RoutePacer origin.
7. **Enable RoutePacer intake, then uploads.** Restart and confirm the config endpoint reports
   `{"enabled":true,...}` with the public JWK only.
8. **Enable RouteTimer's handoff last.**

## Smoke steps

Record the result of each of these against the live origin.

| Step | Expectation |
|---|---|
| Readiness | `/health/ready` returns `200`; `/health/live` returns `200` |
| Upload | authenticated `POST /api/handoffs` with `application/gpx+xml` returns `201`, an absolute payload URL on the production origin, and an expiry exactly 10 minutes ahead |
| First fetch | `GET` of the payload URL returns the exact GPX bytes with `no-store`, `no-cache`, and `nosniff` |
| Row deletion | the `handoffs` table has zero rows for that token immediately after the first fetch |
| Second fetch | an immediate second `GET` returns the same empty `404` as an unknown token |
| Expiry | an unconsumed row is gone within one cleanup interval of its 10-minute expiry |
| Real phone QR | scanning RouteTimer's QR on a physical phone imports the route and reaches `Start ride`, and the address bar shows a bare `/open` |
| Manual fallback | a tampered or expired link shows the recovery copy and offers manual GPX import |
| Log safety | no credential, token, payload URL, invocation query, signature, route name, or GPX byte appears in application or ingress logs |

## Disablement

Disabling is the reverse order: RouteTimer's handoff first, then RoutePacer uploads, then RoutePacer
intake. Disabling intake leaves already-imported routes on riders' devices untouched, because imported
routes never leave the phone.
