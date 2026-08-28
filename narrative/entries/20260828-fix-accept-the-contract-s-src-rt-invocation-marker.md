---
date: 2026-08-28
slug: fix-accept-the-contract-s-src-rt-invocation-marker
title: "fix: accept the contract's src=rt invocation marker"
summary: "Accept `src=rt` and reject everything else, including `RouteTimer`."
kind: product
status: accepted
sequence: 2026-08-28T03:50:06.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/10; merge commit 65998f4483a2d980daca6f7204a04f63e60964a2"
---

## Context

The cross-repository readiness gate, run while implementing RouteTimer's side, recorded step 1 as NO-GO:
RoutePacer rejected every well-formed handoff link. Three independent sources agree the source marker is
`rt`, and nothing supported `RouteTimer`:

- `docs/superpowers/specs/2026-08-27-routepacer-public-handoff-relay-design.md` shows `?src=rt` and
  states that RoutePacer requires `src=rt` and `v=1`.
- `InvocationCanonicalizer` signs `rt\n1\n{payload}\n{name}\n{ts}`. The parser contradicted the byte
  sequence it went on to verify against, so the value it demanded could never round-trip.
- RouteTimer emits `src=rt` and signs `rt`.

Two things let this ship. No test asserted the accepted `src` value against the contract — the parser
tests built every fixture with `src=RouteTimer` and so passed only by agreeing with the defect. And the
repository's contract vector was invented locally rather than mirrored from RouteTimer's frozen copy,
even though `docs/contracts/route-timer-invocation-v1.md` and the test project both already stated it had
to be byte-identical. A vector produced by the same misreading cannot contradict it.

## Decision

Accept `src=rt` and reject everything else, including `RouteTimer`. Tolerating both spellings was
rejected: a parser that accepts two spellings of a signed protocol field makes the drift permanent and
leaves the accepted value untied to the canonical bytes. `rt` is what those bytes commit to, so it is the
only value that can round-trip.

The contract itself is unchanged: the key set and order stay `src`, `v`, `payload`, `name`, `ts`, `sig`,
each exactly once; the canonical form stays `rt\n1\n{payload}\n{name}\n{unix-ms}` with no trailing line
feed; signatures stay 64-byte IEEE-P1363 unpadded base64url. No validation was relaxed — the token shape,
same-origin payload check, HTTPS requirement, percent-encoding strictness, and the ten-minute validity
window are untouched. The production diff is one string literal.

Two changes close the gap that hid the defect rather than just the defect:

- Regression coverage now asserts the accepted value against the contract — `src=rt` parses, `src=RouteTimer`
  is rejected, an absent, empty, or otherwise-spelled `src` is rejected, and the canonical bytes built from
  the parser's own output open with `rt\n1\n`. That last test ties parser and canonicalizer together so
  they cannot diverge silently again.
- `docs/contracts/fixtures/route-timer-contract-v1.json` is replaced by RouteTimer's frozen vector,
  mirrored byte-for-byte, and its `invocationUrl` is now parsed and verified end to end against its own
  published key, with each signed field mutated in turn to prove verification fails. The shared vector's
  `issuedUnixMilliseconds` is fixed, so those tests inject a clock relative to that instant rather than
  reading the wall clock.

## Consequences

Any link built with `src=RouteTimer` is now refused. Nothing deployed emits one — RouteTimer has always
signed and sent `rt` — so this breaks no existing caller, and the links it refuses could never have
verified anyway.

The mirrored vector carries RouteTimer's field names (`version`, `canonical`, `issuedUnixMilliseconds`),
so `ContractFixture` and the browser E2E tests read those instead of the locally invented ones, and the
contract document's property list is corrected to match. It also carries `privateKeyPem`, which the local
vector omitted: byte-identity is the point of a mirror and is what lets a future drift check be a hash
comparison, so the file is copied whole. That key pair is a published test key and must never be used in
any deployed environment; RoutePacer reads only `publicJwk`, and the existing assertion that the published
JWK carries no private `d` component still holds.

Step 1 of the cross-repository readiness gate can now be re-run. Steps 2 and 3 remain open — they need a
deployed relay and a real phone, which no test here can stand in for. Contract v1's origin is still pinned
as a code constant, so the browser E2E tests continue to prove only the halves a loopback host can prove:
real Web Crypto verification of the frozen vector, and `/open`'s rejection and cleanup path.
