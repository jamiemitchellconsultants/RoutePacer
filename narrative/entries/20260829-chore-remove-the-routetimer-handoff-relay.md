---
date: 2026-08-29
slug: chore-remove-the-routetimer-handoff-relay
title: "chore: remove the RouteTimer handoff relay"
summary: "Remove the relay, the invocation intake, and the whole server-side backend, because nothing else used it. `RoutePacer.Persistence` held one `DbSet`, one table and one migration, all the relay's."
kind: product
status: accepted
sequence: 2026-08-29T06:11:16.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/18; merge commit b87ffe442a3ba91a0ed76c5340c2d62171eb60ac"
---

## Context

This reverses `design-and-plan-public-routetimer-handoff-relay` (PR #4) and the implementation that followed it. Per `AGENTS.md` the original entry stands unaltered; this is a `correction` citing it by slug.

The relay was built to save one file transfer. Instead of saving a prediction and opening it on the phone, RouteTimer uploaded the GPX and the phone fetched it from a signed, expiring link.

It cannot be made generally useful as designed. `HandoffRelayOptions.UploadCredential` is a single string and `RouteTimerInvocationOptions.PublicKeyJwk` is a single key, so the relay serves exactly **one** RouteTimer identity. A second deployment would need the shared credential — making it public and the relay an open file drop — and the private signing key, which would defeat the signature entirely. Invocation contract v1 is frozen at six query keys and rejects any additional one, so there is nowhere to put a tenant identifier without a v2 across both repositories.

RoutePacer is a public repository. A feature only its author can use does not belong in one.

## Decision

Remove the relay, the invocation intake, and the whole server-side backend, because nothing else used it. `RoutePacer.Persistence` held one `DbSet`, one table and one migration, all the relay's. So this also deletes PostgreSQL, `DatabaseMigrationService` and the readiness gate that existed to await it, the rate limiter whose only consumer was the upload endpoint, and `SensitiveRequestLoggingFilter`, which matched only `/api/handoffs` and `/open` and would otherwise have become middleware that logs nothing.

The deployment collapses to one stateless container: no database, no volume, no secret, no env file. `deploy/.env.example` now holds a single image tag.

Riders keep the capability. A prediction reaches a phone by saving the file and opening it — through whatever cloud storage they already use — which needs no relay, no credential and no signing key, and which works for anyone self-hosting rather than only for this deployment.

## Consequences

`privacy.md` can now state without qualification that nothing leaves the device. It previously had to describe readable GPX bytes sitting in a database for up to ten minutes and explain why that was an acceptable trade. That section is gone because the situation it described is gone.

Server-side state is now a decision to revisit deliberately rather than a thing already present. `DeploymentConfigurationTests` pins this: the production compose must declare no volume, no secrets, and exactly one environment variable, and `appsettings.json` must configure nothing but logging. A database reappearing is a test failure, not a silent drift.

Contract v1, its frozen fixture and the coordinated rollout document are deleted; RouteTimer's side is removed separately. Anyone wanting this feature back starts from the multi-tenant design that was never built, not from these files.

Deployment gets materially simpler — no LocalStack secrets, no database password rotation, no key provisioning. LocalAI's deployment script is simplified in a follow-up.
