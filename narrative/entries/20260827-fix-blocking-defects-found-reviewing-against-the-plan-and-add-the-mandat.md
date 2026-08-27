---
date: 2026-08-27
slug: fix-blocking-defects-found-reviewing-against-the-plan-and-add-the-mandat
title: "Fix blocking defects found reviewing against the plan, and add the mandated test coverage"
summary: "**Fix the defects and build the mandated coverage, rather than ship behind a passing-but-empty suite.** Each suite is written against the behaviour the plan specifies, and each defect above has a regression test."
kind: product
status: accepted
sequence: 2026-08-27T19:27:41.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/8; merge commit f29b133a34dd6e363f135ca25e15101d28c61aa4"
---

## Context

The branch built the full RoutePacer shape — Core, App, Server, Persistence, deployment — but shipped with
11 tests, most of them template stubs. `Directory.Packages.props` pinned neither bUnit, Testcontainers, nor
Playwright, so the component, real-PostgreSQL, and browser suites the plan gates its review on were not
merely unwritten but structurally impossible. Review Gates 1–5 could not have been satisfied as written.

That absence was not neutral. Reviewing the code by reading found nine defects that between them made the
rider flow unusable: `TimeProvider` was never registered in the WebAssembly container, so `/track/{id}` and
`/open` threw on service resolution; the segment-projection denominator used the distance from the fix to
the segment's end vertex instead of the squared segment length, degrading matching to nearest-vertex
snapping and corrupting every derived value; and `disabled="busy"` was a literal string, permanently
disabling the manual GPX picker.

Writing the missing tests then found seven more, five of them serious, none reachable by reading:

- The **published app never booted**. The hosted publish does not run the client's HTML placeholder
  substitution, so `index.html` shipped with a literal `#[.{fingerprint}]` and requested a path that 404s.
  The production build rendered nothing. Found by the first Playwright test that loaded real published output.
- **`docker build` had never succeeded** — `global.json` pinned SDK 10.0.302 against an image shipping 10.0.301.
- **EF discovered no migrations.** `CreateHandoffs` carried no `[Migration]` attribute, so `MigrateAsync`
  created only the history table and a fresh deployment came up with no `handoffs` table. Found by the
  first Testcontainers run.
- **GPX timestamps were silently dropped.** `ReadElementContentAsStringAsync` advances past its element and
  the loop read again, skipping the sibling after `<ele>` — where nearly every device writes `<time>`.
  Timed routes imported as distance-only, defeating the app's central feature.
- A defect in **this branch's own first fix**: the matcher tie-break introduced in de6eb69 tracked the
  running minimum separately from the selected candidate, so a route approached gradually never cleared the
  3 m threshold and matching returned `null`. Caught by the performance test.

The evidence is that the untested areas were exactly the broken ones, and that several defects were of a
kind only execution could surface.

## Decision

**Fix the defects and build the mandated coverage, rather than ship behind a passing-but-empty suite.** Each
suite is written against the behaviour the plan specifies, and each defect above has a regression test.

Four judgement calls are worth recording:

- **Asset fingerprinting is disabled for the PWA.** The hosted publish will not substitute the fingerprint
  placeholder, so the alternative was a page that 404s its own bootstrap. Per-asset cache busting is given
  up; invalidation already comes from the service worker's versioned cache prefix and generated asset
  manifest, which the plan specifies as the cache-upgrade mechanism.
- **The upload rate limiter is partitioned by credential.** The plan asked for a single global fixed window,
  but rate limiting is middleware and runs before the endpoint can authenticate, so ten anonymous POSTs per
  minute locked RouteTimer out. Authenticated and anonymous traffic now occupy separate 10/min partitions,
  which keeps the plan's intent and removes the denial-of-service path. The rejection status is also now
  429 rather than the framework default 503, which was indistinguishable from uploads-disabled.
- **The matcher's tie-break is a backward-penalty score**, not a tolerance band. A candidate behind the
  previous match is scored `cross + 3 m`, which keeps the out-and-back crossing behaviour the plan wants
  while leaving a clearly closer segment free to win and — unlike the band — never blocking a gradual approach.
- **The plan text was corrected to match `GpsSpikeFilter`, not the reverse.** Task 15 Step 3 required
  rejecting a fast fix when the browser speed *agrees*, which discards genuine descents while keeping
  uncorroborated jumps. The code implements the correct inverse. The step now states the implemented rule
  with a dated note recording what it originally said, rather than reading as though it had always been right.

## Consequences

The rider flow works end to end and is proven by execution: a route imported online is still listed after
going offline, the app shell relaunches from cache with the network off, a mocked-geolocation ride records
and survives a reload, and racing consumers of one relay token produce exactly one winner against real
PostgreSQL. Log safety is enforced by canaries rather than asserted — a test drives success and failure
paths with credential, route-name and GPX markers and proves none reach the captured log.

Trade-offs accepted:

- No per-asset cache busting, as above. A stale asset now depends on the service worker version changing.
- The E2E and Persistence suites require Docker, and the browser suites require a one-off Playwright browser
  install. `README.md` documents the differing prerequisites per suite.
- WebAssembly start-up dominates the browser suites; their timeouts are deliberately generous.

Deliberately left open:

- **The full signed RouteTimer flow is not browser-tested.** Contract v1 pins the origin as a code constant
  and the plan forbids making it configurable, so a loopback host can never satisfy the parser. The browser
  suite covers what it can genuinely prove — real Web Crypto verification of the frozen fixture, and
  `/open`'s rejection, URL cleanup and manual fallback — and the end-to-end signed path stays in
  `docs/manual-validation.md`, with the reason recorded in the suite itself.
- **The device matrix is unfilled.** iOS and Android installed-PWA behaviour, wake-lock revoke/reacquire on
  real hardware, the 250,000-point import on a phone, and the real-QR handoff need hardware and the
  production origin. Every row has an evidence column and none is ticked.
- **`docs/contracts/fixtures/route-timer-contract-v1.json` must be copied byte-identical into RouteTimer.**
  It carries a test-only P-256 key pair with a fixed P1363 signature, self-verified at generation. Nothing
  yet enforces that the two repositories agree.
- Both handoff controls remain disabled by default, and the relay still has no backup, restore, or rollback
  path — unchanged by this PR and restated in `docs/route-timer-rollout.md`.
