---
date: 2026-08-29
slug: fix-remove-the-sidebar-and-stop-navigating-to-a-page-that-no-longer-exis
title: "fix: remove the sidebar, and stop navigating to a page that no longer exists"
summary: "Remove the sidebar. There is nothing for a menu to choose between, and on the tracker it was lit pixels sitting on screen for the whole ride, including a nav toggle that shipped as a translucent white block."
kind: product
status: accepted
sequence: 2026-08-29T08:02:55.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/22; merge commit f166e695628965afc26f96db60e74441086416af"
---

## Context

`main` currently ships a broken finish: `Track.razor` calls `Navigation.NavigateTo("/rides")` when a ride stops, and `/rides` was deleted with the ride history in #20. A rider who completes a ride lands on **"Not Found"**.

The navigation menu has the same problem in a quieter form. Its links to **Routes** and **Ride history** point at pages that no longer exist, so two of its four entries are dead.

Both are leftovers from an application that had a route library and a ride history. It now has three screens — the route, importing one, and the tracker — and one of them is a full-screen instrument panel.

## Decision

Remove the sidebar. There is nothing for a menu to choose between, and on the tracker it was lit pixels sitting on screen for the whole ride, including a nav toggle that shipped as a translucent white block.

The layout becomes a single header carrying the brand as a link home: one line, and no screen left as a dead end. The tracker's ready state gains a **Back** link, which the menu used to provide.

Stopping a ride now **stays on the tracker**. That is the only place a finished ride is shown, and where #20's design already put it — there is no history to send anyone to.

The header's colours come from Bootstrap's theme variables rather than fixed values. Hardcoding a light bar put light text on a light background in dark mode; a screenshot caught that, the tests did not.

## Consequences

Navigation is now entirely in-page: Home offers **Start ride** and **Replace route**, import offers **Start ride** and **Back**, the tracker offers **Back** before a ride and **Done** after one. Adding a fourth screen later means deciding how it is reached, rather than adding a menu row.

**189 lines deleted, 26 added.** `NavMenu.razor` and its stylesheet are gone, and `MainLayout.razor.css` loses the responsive sidebar it existed to place.
