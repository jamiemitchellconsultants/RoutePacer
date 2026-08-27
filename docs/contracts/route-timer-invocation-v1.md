# RouteTimer invocation Contract v1

RouteTimer hands a timed GPX route to RoutePacer by uploading it to the relay and then opening a signed
HTTPS deep link on the rider's phone. This document freezes that link. Both applications validate against
the same fixture, `fixtures/route-timer-contract-v1.json`, which must stay **byte-identical** in the two
repositories; copy the file, never regenerate it independently.

## Invocation URL

The URL is always `https://pacetracking.tqaentry.com/open` with a query carrying exactly one each of:

| Key | Value |
|---|---|
| `src` | the literal `RouteTimer` |
| `v` | the literal `1` |
| `payload` | absolute same-origin handoff URL, `https://pacetracking.tqaentry.com/api/handoffs/{token}` |
| `name` | route name; may be empty, percent-encoded UTF-8 |
| `ts` | issue time in Unix milliseconds, digits only |
| `sig` | unpadded base64url signature, 86 characters decoding to 64 bytes |

Any missing key, duplicate key, additional key, wrong `src`/`v`, empty `payload`/`ts`/`sig`, invalid percent
escape, user info, fragment, non-HTTPS scheme, or foreign origin is rejected before anything is fetched.
The `payload` URL must carry no query or fragment and its final segment must match `^[A-Za-z0-9_-]{43}$`.

## Validity window

The link is accepted when it is at most **10 minutes old** and at most **60 seconds in the future**. The
future allowance exists only to absorb clock skew between RouteTimer and the phone.

## Canonical signed bytes

Signed bytes are UTF-8, line feeds between fields, and **no trailing line feed**:

```text
rt\n1\n{payload-absolute-uri}\n{name-or-empty}\n{unix-milliseconds}
```

`{payload-absolute-uri}` is the absolute URI exactly as it appears after percent-decoding, and
`{name-or-empty}` is the decoded route name, which may be the empty string.

## Signature

ECDSA over P-256 with SHA-256, in fixed-width IEEE P1363 concatenated `r || s` form (64 bytes), encoded
unpadded base64url. DER-encoded signatures are not accepted. RoutePacer publishes only RouteTimer's
configured **public** JWK, via `GET /api/config/route-timer-invocation`; a JWK that is absent, malformed,
non-EC, not P-256, or carries a private `d` component fails server startup. No private or symmetric key
material ever reaches the browser.

## Payload fetch

After the signature verifies, RoutePacer issues exactly one `GET` for the payload URL. The response must be
`application/gpx+xml` and at most 52,428,800 bytes. The relay deletes the row in the same statement that
returns it, so a second fetch is a `404`; a failure after the request has been dispatched is terminal and
the rider is offered manual GPX import instead of a retry.

## Fixture

`fixtures/route-timer-contract-v1.json` holds exactly these properties:

`fixtureVersion`, `publicJwk`, `canonicalText`, `payloadUrl`, `name`, `timestamp`, `signature`,
`invocationUrl`.

The key pair is test-only and must never be used in any deployed environment. Tampered cases are derived
from the fixture at test time; the valid vector itself is never modified.
