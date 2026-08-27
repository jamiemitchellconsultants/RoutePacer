# RoutePacer privacy

## What stays on your device

Imported routes, every ride you record, and all GPS positions are stored only in your browser's IndexedDB
on the device you used. They are not uploaded, synchronised, or backed up. Clearing the site's data, or
deleting a route or ride in the app, removes them permanently — there is nowhere else to recover them from.

Tracking runs entirely in the browser. Location permission is requested only after you explicitly start a
ride, and the app asks for it at that moment rather than on first load. Positions are matched and paced
locally; nothing about your position leaves the device.

## What briefly leaves your device

Only an explicit RouteTimer handoff. When you choose to send a route from RouteTimer to RoutePacer:

- RouteTimer uploads the timed GPX to the relay over TLS.
- The relay stores the **readable** GPX bytes, a SHA-256 hash of a random token, and two timestamps. It
  does not store the token itself, the route name, the payload URL, or anything identifying you.
- The row lives for at most **ten minutes**.
- The first successful fetch deletes the row in the same database statement that returns it, so the route
  is gone from the server the moment your phone has it.
- Rows that are never fetched are deleted automatically once they expire.

After import, the route is an ordinary on-device route like any file you picked yourself.

The relay bytes are readable to anyone with database access for that window. This is a deliberate trade
for a simple, short-lived handoff, not an accident, and it is why both handoff controls ship disabled.

## What is never recorded

Application, ingress, trace, and metric output never contains credentials, tokens, payload URLs,
invocation queries, signatures, route names, or GPX bytes. Request logging records only the method, the
literal route template, the status class, and a byte count; the Caddy site discards access logs entirely so
a token in a URL cannot reach ingress. This is enforced by a test that drives success and failure paths
with canary values and asserts none of them reach the captured log.

## What has no backup

The relay database has no backup, restore, or rollback procedure, by design. It holds nothing worth
recovering: every row is either consumed within seconds or expired within ten minutes. If it is lost, a new
handoff link is generated and nothing a rider owns is affected.
