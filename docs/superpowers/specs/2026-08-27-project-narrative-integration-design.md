# Project Narrative Integration Design

**Date:** 2026-08-27

**Status:** Approved in chat

## Goal

Install [Project Narrative](https://github.com/jamiemitchellconsultants/Narrative) in RoutePacer so decision-bearing pull requests can produce deterministic, review-first decision-history entries after merge.

## Approach

Use Narrative's official installer from the repository root with Node.js 20 or newer:

```bash
npx --yes --package=github:jamiemitchellconsultants/Narrative narrative install
```

The installer remains the authority for its generated scaffold. Its workflows will continue tracking `jamiemitchellconsultants/Narrative@main`, as explicitly selected for automatic updates. The integration will not vendor Narrative or manually reproduce files that the installer owns.

## Generated Scaffold

The installer is expected to create or preserve these files non-destructively:

- `.project-narrative.json`
- `narrative/preamble.md`
- `Narrative.md`
- `.github/workflows/maintain-narrative.yml`
- `.github/workflows/validate-narrative.yml`
- `.github/pull_request_template.md`

`Narrative.md` is a compiled projection. Future changes to narrative wording must edit a fragment under `narrative/entries/` and run `narrative compile`; contributors must never hand-edit the projection.

## Agent Instructions

Create `AGENTS.md` as the canonical repository instruction file. It will record the Narrative contract verbatim in substance:

- Decision-bearing pull requests require the exact `narrative-required` label and all three headings: `## Narrative Context`, `## Narrative Decision`, and `## Narrative Consequences`.
- Capture runs only on merge. A missing label exits silently; labelled pull requests with missing headings fail visibly. Neither mistake can be repaired by editing the merged pull request.
- Explicit pull-request bodies must carry the three headings because they replace the template.
- Narrative-only maintenance pull requests remain unlabelled.
- Corrections create a new `correction` entry citing the original slug; accepted history is not rewritten retrospectively.

Add pointer-only files for other tier-one agents. Each pointer will direct the agent to read `AGENTS.md` and will not duplicate its rules:

- `.github/copilot-instructions.md`
- `CLAUDE.md`
- `GEMINI.md`
- `.cursor/rules/project-instructions.mdc` with `alwaysApply: true`
- `.windsurf/rules/project-instructions.md` with `trigger: always_on`
- `.clinerules/project-instructions.md`

## Validation and Failure Handling

Run the deterministic validation gate after scaffolding:

```bash
npx --yes --package=github:jamiemitchellconsultants/Narrative narrative check
```

A non-zero exit stops the integration; generated validation will not be weakened. Inspect the complete diff to confirm the installer did not overwrite existing content and that the pull-request template contains the three exact headings. The installation pull request must not carry `narrative-required`, preventing recursive capture of the mechanical installation itself.

## Delivery

Implement on `codex/add-project-narrative`, commit the scaffold and agent instructions, push the branch, and open an unlabelled pull request against `main`.

The handoff will report these GitHub-admin actions as incomplete until performed:

1. Enable read/write workflow permissions and allow GitHub Actions to create and approve pull requests.
2. Create the label named exactly `narrative-required`.
3. Merge the installation pull request before relying on decision capture, because workflows must exist on the default branch.

## Out of Scope

- Authoring decision entries or inventing rationale for existing work.
- Adding `narrative-required` to the installation pull request.
- Pinning workflows to a tag or commit SHA.
- Rewriting accepted Narrative history.
