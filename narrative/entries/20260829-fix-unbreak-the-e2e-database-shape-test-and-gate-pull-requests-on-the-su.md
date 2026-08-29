---
date: 2026-08-29
slug: fix-unbreak-the-e2e-database-shape-test-and-gate-pull-requests-on-the-su
title: "fix: unbreak the E2E database-shape test, and gate pull requests on the suite"
summary: "Two changes, one per commit. The test's result shape becomes a class with a parameterless constructor and settable properties, which is the form Playwright's converter can populate."
kind: product
status: accepted
sequence: 2026-08-29T15:24:15.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/30; merge commit 8a5c6e78e930e39efff5b48f5d85bb0565ab6959"
---

## Context

The autopause merge carried an E2E test that could never pass. `A_version_2_database_gains_the_settings_store_without_losing_its_route` read the upgraded IndexedDB schema with `EvaluateAsync<UpgradedDatabase>`, where `UpgradedDatabase` was a positional record. Playwright's converter materialises an evaluated object by calling `Activator.CreateInstance(t)` with no arguments and then assigning properties by name, so a type whose only constructor takes parameters throws `MissingMethodException` — surfaced as the far less informative `Return type mismatch. Expecting ..., got Object`.

It reached `main` because nothing ran the tests before a merge. The only `pull_request` workflows were the two narrative ones; the suite ran solely inside `publish-container`, on push to `main`. So the first signal arrived after the merge, and it failed two consecutive runs — no container published for either — before anyone read the log.

## Decision

Two changes, one per commit.

The test's result shape becomes a class with a parameterless constructor and settable properties, which is the form Playwright's converter can populate. The assertions are untouched: version 3, the `settings` store present, the seeded route surviving the upgrade. Nothing about the upgrade itself was ever wrong — the test never got as far as asserting on it.

The gate becomes one reusable workflow. `tests.yml` runs `restore → build → playwright install chromium → dotnet test`, triggered `on: pull_request` and declaring `workflow_call`; `publish-container` calls it as a `test` job and gates `publish` on `needs: test`. The check a branch must pass and the check `main` must pass are now the same steps by construction, rather than two copies of five lines that agree until someone edits one. The publish job keeps only the container work — its `restore` and `build` fed the test step, never the image, since the Dockerfile runs its own restore and publish.

## Consequences

A broken test now blocks the merge instead of blocking the release after it. Container publishing resumes.

Two costs. Test time moves onto the pull-request path, where the E2E leg dominates — the Playwright browser download and the published-app fixture, roughly two minutes. And `publish-container` now reports two jobs, `test` and `publish`, instead of one; any branch protection naming the old check needs updating.

The narrower lesson is recorded in the fix commit: within this suite, only ask `EvaluateAsync<T>` for a type Playwright can construct. The other calls all request `string`, `int`, or `string[]`, which is why this was the only one that failed.

---

Verified before and after on the full CI command, `dotnet test RoutePacer.slnx -c Release`: the test failed with the exact CI error, and the suite is now green at 3 + 160 + 95 + 17.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
