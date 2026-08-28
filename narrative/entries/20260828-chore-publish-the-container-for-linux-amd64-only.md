---
date: 2026-08-28
slug: chore-publish-the-container-for-linux-amd64-only
title: "chore: publish the container for linux/amd64 only"
summary: "Publish `linux/amd64` only, and drop `setup-qemu-action`, which existed solely to serve the emulated leg. Record why in two places rather than leaving a bare platform list."
kind: product
status: accepted
sequence: 2026-08-28T05:32:02.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/14; merge commit eb08a097c7db66f37b0644f9a61a295e50a5734a"
---

## Context

The publish gate builds the container for `linux/amd64,linux/arm64`. GitHub offers no arm64 runner here, so `setup-qemu-action` emulates it, and the Dockerfile performs the entire `dotnet restore` and `dotnet publish` — the Blazor WebAssembly payload included — inside the image rather than copying in a prebuilt output. Emulating that publish is roughly ten times slower than running it natively.

The effect was measured on run 33142514433: the native leg finished in about four minutes and the full step took around twenty, with the arm64 leg accounting for nearly all of it. The gate runs on every push to `main`.

The image has exactly one consumer: the RoutePacer deployment on a single x64 host running Linux containers, published behind the shared Caddy ingress at `pacetracking.tqaentry.com`. Nothing has ever pulled the arm64 image.

## Decision

Publish `linux/amd64` only, and drop `setup-qemu-action`, which existed solely to serve the emulated leg.

Record why in two places rather than leaving a bare platform list. This is a public repository, and a single-platform `platforms:` line is indistinguishable from an oversight — the next reader's obvious "fix" is to add arm64 back and silently restore a twenty-minute gate. The workflow carries the reasoning at the point of change, and `deploy/README.md` states the constraint where someone choosing how to run the image will meet it.

Both notes say what to do instead: build the Dockerfile directly. It carries no architecture assumption and both base images are multi-arch, so `docker build` on an arm64 machine produces an arm64 image natively and quickly.

## Consequences

The published image no longer runs on arm64 hosts. Anyone wanting one builds it themselves; there is no prebuilt artifact and no fallback, and a `docker pull` on an arm64 machine now fails outright rather than quietly fetching an emulated-build image.

The gate returns to roughly the duration of the test suite plus a native image build, so a red main is diagnosable in minutes instead of twenty.

This narrows what the project ships. If a second deployment target ever needs arm64, the decision is revisited by adding the platform back together with a native arm64 runner — not by reinstating QEMU emulation, which is the specific thing this removes.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
