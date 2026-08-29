# Manual validation matrix

Automated tests cover everything reachable from a headless browser and a container. These are the cases
that need real hardware, real radios, or the production origin. Record device, OS and browser version,
date, and evidence for each run. A row is not passed until its evidence exists.

## Installed PWA — iOS Safari

| # | Step | Expected | Result | Evidence |
|---|---|---|---|---|
| 1 | First load over HTTPS, then Add to Home Screen | Installs with the RoutePacer icon and name | | |
| 2 | Enable airplane mode, relaunch from the home screen | App starts and reaches the home page | | |
| 3 | Import a timed GPX, start a ride | Location prompt appears only at start | | |
| 4 | Ride 60 minutes with the screen active | Deltas update throughout; no reload or data loss | | |
| 5 | Stop the ride | Final distance and elapsed shown on the page; nothing kept once you leave it | | |

## Installed PWA — Android Chrome

| # | Step | Expected | Result | Evidence |
|---|---|---|---|---|
| 1 | Install, then relaunch offline | App starts from cache | | |
| 2 | Start a ride | Wake lock acquired; tracker reports "Screen kept awake" | | |
| 3 | Switch apps and return | Wake lock is revoked, then re-acquired on return | | |
| 4 | 60-minute active-screen ride | Continuous tracking; every accepted fix persisted | | |
| 5 | Deny location at the prompt | Ride stops with recovery guidance, not a crash | | |

## Desktop Chrome and Edge

| # | Step | Expected | Result | Evidence |
|---|---|---|---|---|
| 1 | Import a route, reload | Route still shown from IndexedDB | | |
| 2 | Upgrade over a version 1 install | The route survives; any old ride history is gone, and the app still starts | | |
| 3 | Go offline and reload | App shell and library load from cache | | |

## Large route

| # | Step | Expected | Result | Evidence |
|---|---|---|---|---|
| 1 | Import a 250,000-point GPX | Completes; record elapsed time and peak memory | | |
| 2 | Start a ride on it | First match within a few seconds | | |
| 3 | Track for 60 minutes | Points persist; no memory growth trend | | |

## Airplane mode

| # | Step | Expected | Result | Evidence |
|---|---|---|---|---|
| 1 | The loaded route with no network | Fully available | | |
| 2 | Start a ride on the loaded route | Tracking works with no network at all | | |

## One route, no history

| # | Step | Expected | Result | Evidence |
|---|---|---|---|---|
| 1 | Import a second route | Replaces the first; only the new one is offered | | |
| 2 | Kill the tab mid-ride, reopen | Ride returns **paused** with its distance and elapsed; no location prompt until Resume | | |
| 3 | Resume after that recovery | Tracking continues; elapsed does not jump by the time the app was closed | | |
| 4 | Replace the route while a ride is recovered | The recovered ride is discarded rather than paced against the wrong route | | |
| 5 | Stop a ride, then reload | No ride anywhere: no history page, and `active_ride` is empty | | |
