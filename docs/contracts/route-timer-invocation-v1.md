# RouteTimer invocation Contract v1

The HTTPS URL is `/open` with exactly one each of `src`, `v`, `payload`, `name`, `ts`, and `sig`. `src` is `RouteTimer`, `v` is `1`, `payload` is an absolute same-origin handoff URL, and `ts` is Unix milliseconds. The payload is valid for ten minutes in the past and sixty seconds in the future. The signature is unpadded base64url P-256 ECDSA/SHA-256 in fixed-width IEEE P1363 form.

Signed bytes are UTF-8 with no trailing line feed:

    rt\n1\n{payload-absolute-uri}\n{name-or-empty}\n{unix-milliseconds}
