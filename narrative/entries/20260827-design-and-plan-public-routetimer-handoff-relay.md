---
date: 2026-08-27
slug: design-and-plan-public-routetimer-handoff-relay
title: "Design and plan public RouteTimer handoff relay"
summary: "Host the Blazor WebAssembly PWA and a .NET 10 relay API in one ASP.NET Core container behind shared Caddy."
kind: product
status: accepted
sequence: 2026-08-27T16:04:11.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/4; merge commit bb74553739f46e7e6503e56b9c20151d4783d81e"
---

## Context

RouteTimer normally runs privately on the rider computer, so the previous design in which a phone fetched GPX from a publicly reachable RouteTimer host was not deployable. RoutePacer needs a public same-origin transfer boundary without weakening its offline tracking behavior or publishing RouteTimer inbound routes.

## Decision

Host the Blazor WebAssembly PWA and a .NET 10 relay API in one ASP.NET Core container behind shared Caddy. Store each upload in a dedicated PostgreSQL container for at most ten minutes using only a SHA-256 token digest, return it once with one atomic `DELETE ... RETURNING`, and delete it immediately on consumption. Keep ECDSA P-256 Contract v1 with only the public JWK in RoutePacer. Deploy forward-only with relay uploads and client intake independently disabled by default, no database backup, restore, or rollback plan.

## Consequences

RouteTimer remains private and sends timed GPX only through outbound HTTPS. A deliberate privacy exception allows readable GPX in the public relay until consumption or expiry, while imported routes and rides remain on the phone. Deployment gains PostgreSQL, migration readiness, Caddy redaction, runtime secret provisioning, production-like cross-repository tests, and a strict enablement sequence. The implementation plan expands to cover these boundaries before tracking release acceptance.
