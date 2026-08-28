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
| 5 | Stop, then open ride history | Ride listed with distance, duration, average | | |

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
| 1 | Load, import a route, reload | Route still listed from IndexedDB | | |
| 2 | Upgrade over a previous install | Existing routes and rides survive the upgrade | | |
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
| 1 | Routes and ride history with no network | Both fully available | | |
| 2 | Start a ride on a previously imported route | Tracking works with no network at all | | |
