# RoutePacer privacy

## What stays on your device

Everything. Imported routes, every ride you record, and all GPS positions are stored only in your
browser's IndexedDB on the device you used. They are not uploaded, synchronised, or backed up. Clearing
the site's data, or deleting a route or ride in the app, removes them permanently — there is nowhere else
to recover them from.

Tracking runs entirely in the browser. Location permission is requested only after you explicitly start a
ride, and the app asks for it at that moment rather than on first load. Positions are matched and paced
locally; nothing about your position leaves the device.

## What leaves your device

Nothing.

The server has no API, no database, and no account. It serves the application files and answers two
health probes, and that is the whole of it. There is no upload path, so there is no route, ride, position,
or file of yours on any server — not briefly, not encrypted, not at all.

A route reaches RoutePacer only when you pick a GPX or FIT file yourself, and that file is read in the
browser. It is never sent anywhere.

An earlier version of RoutePacer ran a relay that briefly held an uploaded GPX so a route could be handed
over from RouteTimer. That feature was abandoned and the relay, its database, and its credentials were
removed. Transfer a prediction the ordinary way instead: save the file and open it on your phone, through
whatever file or cloud storage you already use.

## What is never recorded

There is nothing to record. The server exposes no endpoint that carries route data, so no route name,
position, or file content can appear in a log. Request logging by the framework is switched off in
`appsettings.json` rather than filtered, so a URL never reaches application output, and the Caddy site
discards access logs so nothing reaches ingress either.

## What has no backup

Nothing on the server needs one — it holds no state. Your routes and rides live on your device only, so
your own device backup is the only copy that exists. If you clear the site's data, they are gone.
