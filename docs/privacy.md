# RoutePacer privacy

## What stays on your device

One route, and only while you keep it. RoutePacer holds a single imported route in your browser's
IndexedDB; importing another replaces it. Nothing is uploaded, synchronised, or backed up.

## What is not kept at all

Your rides. RoutePacer is a pacing aide, not a recorder — whatever you already use to record a ride
still does that, and this app does not duplicate it.

While a ride is running, positions are written to your browser so that a reload, a crash, or the phone
evicting the tab does not end the ride mid-route. When you stop, that is deleted. There is no ride
history, no export, and no way to look up what you rode: the numbers are on screen while you are on that
page, and then they are gone.

Upgrading from an earlier version deletes any ride history that version stored.

Tracking runs entirely in the browser. Location permission is requested only after you explicitly start a
ride — and a ride recovered after a crash comes back paused, so reopening the app never restarts GPS on
its own. Positions are matched and paced locally; nothing about your position leaves the device.

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

Nothing on the server needs one — it holds no state. Your route lives on your device only. If you clear
the site's data it is gone, and so is anything about a ride in progress.
