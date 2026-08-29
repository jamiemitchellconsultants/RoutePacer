---
date: 2026-08-29
slug: feat-monochrome-pace-tiles-and-fix-the-distance-tile-reading-backwards
title: "feat: monochrome pace tiles, and fix the distance tile reading backwards"
summary: "**Carry the distinction in where the word sits.** ``` 2:03 ahead 120 m ahead behind 0:45 behind 85 m ``` Ahead puts the word after the number, behind puts it before."
kind: product
status: accepted
sequence: 2026-08-29T11:30:47.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/24; merge commit e47bff9bad298a62a969221ced5facdc6946c26a"
---

## Context

**Colour was the wrong channel.** The tracker told ahead from behind with green `#34d399` and red `#fb7185`. It is deliberately true black for OLED battery, and a dark screen in direct sunlight loses contrast before it loses legibility — so hue washes out exactly when a rider needs the number most, outdoors on a bright day, which is when they are riding. Red and green is also the pairing the common colour-vision deficiencies confuse. The old code half-acknowledged this, claiming the states stayed distinguishable "because ahead/behind is also carried by the sign of the number". A minus sign is not something anyone reads at a glance from a bar.

**And the distance tile was backwards.** `PacingService` signs its two deltas in opposite directions:

```
DeltaTime     = live - target              negative is ahead
DeltaDistance = routeDistance - expected   POSITIVE is ahead
```

Its own tests have recorded this all along — the ahead case asserts `-10` seconds and `+100` metres, the behind case `+20` and `-200`. But `RideFormat` applied a single `Direction(v) => v < 0 ? "ahead" : "behind"` to both. **A rider 100 m up the road was told "behind 100 m."** `DeltaTone` was the same mistake, so the tone class agreed with the wrong word.

This had been shipping. `docs/architecture.md` annotated `deltaDistance` as "negative is behind" while `RideFormat.Delta` documented negative as ahead; the two documents disagreed, and the code followed the wrong one.

## Decision

**Carry the distinction in where the word sits.**

```
2:03 ahead        120 m ahead
behind 0:45       behind 85 m
```

Ahead puts the word after the number, behind puts it before. The two lines have different shapes, so a glance resolves them without reading either word and without seeing colour. Position does not degrade in sunlight, does not depend on hue discrimination, and reads the same to a screen reader. The pace panels are monochrome: one neutral `#52525b` edge, `#f5f5f5` type. **Stop keeps its red** — it is a destructive control rather than a reading, and a rider must not hit it by mistake.

**Convert both deltas to a lead before wording them.** Positive means ahead, whatever the underlying quantity's own convention is, and the conversion happens once at the boundary. That is what stops one helper serving two opposite conventions. `DeltaTone` splits into `TimeTone` and `DistanceTone` for the same reason.

## Consequences

**The distance tile changes meaning for existing riders.** Anyone who has used this and built a feel for that number calibrated it on an inverted reading.

Colour is now free on the tracker, and exactly one element uses it, so Stop is unambiguous. A future state needing emphasis competes with Stop rather than with two pace panels.

The wording is longer for behind — `behind 85 m` rather than `85 m behind` — which reads slightly oddly in prose and is the point: the eye meets the word first when the news is bad.

`architecture.md` now states the opposite-conventions trap explicitly, so the next reader meets it as documentation rather than as a surprise.
