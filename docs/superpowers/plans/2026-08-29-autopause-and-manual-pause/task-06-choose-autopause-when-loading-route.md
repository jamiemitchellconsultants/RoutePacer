# Autopause and Manual Pause Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Freeze the ahead/behind reading while a rider is stopped — automatically after a chosen number of stationary seconds, or on a tap — and unfreeze it when they ride off.

**Architecture:** A pure `StationaryDetector` in `RoutePacer.Core.Tracking` answers "has the rider moved?" from position alone. `RideSessionService` delegates to it, gains a `PauseMode` alongside its existing `RideSessionState.Paused`, and keeps the GPS watch alive through a pause so movement can end it. `PacingService` learns to subtract paused time, which is the defect that made every pause cosmetic.

**Tech Stack:** .NET 10, Blazor WebAssembly, xUnit, FluentAssertions, bUnit (`BunitContext`), `Microsoft.Extensions.Time.Testing.FakeTimeProvider`, IndexedDB via a JS module.

**Spec:** `docs/superpowers/specs/2026-08-29-autopause-design.md`

## Global Constraints

- Solution file is `RoutePacer.slnx`. Build: `dotnet build RoutePacer.slnx`. Test: `dotnet test RoutePacer.slnx`.
- Scope a test run with `--filter`, e.g. `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~StationaryDetectorTests"`. The `RoutePacer.E2E` project drives Playwright and is slow; do not run the unfiltered suite for a single task.
- Test method names are `Sentence_case_with_underscores` and read as claims about behaviour.
- No comments in code unless the *why* is non-obvious — a hidden constraint, a workaround, a surprising invariant. Do not narrate what the code plainly does.
- The tracker carries **no red or green**. Ahead and behind are told apart by where the word sits. Any new state must read as a word, a position, or a brightness — never a hue. See the comment block at the top of `wwwroot/css/tracker.css`.
- Autopause threshold: default **15 s**, minimum **5 s**, maximum **300 s**.
- Stationary radius **10 m**; resume radius **15 m**; escalation to GPS-off after **5 minutes** stationary.
- At the equator (where every test fixture sits) 0.0001° of longitude is 11.13 m. Test fixtures use latitude 0, so longitude offsets convert directly: `0.00005` ≈ 5.6 m, `0.00011` ≈ 12.2 m, `0.00015` ≈ 16.7 m.
- Commit after each task with a message whose subject says what changed for the rider, matching the repository's existing style.

---


---

### Task 6: Choose autopause when loading a route

**Files:**
- Modify: `src/RoutePacer.App/Pages/ImportRoute.razor`
- Test: `tests/RoutePacer.App.Tests/Pages/ImportRouteTests.cs`

**Interfaces:**
- Consumes: `ISettingsRepository`, `AutoPauseSettings` from Task 3.

The controls write on change rather than on import, so the choice does not depend on the file parsing and survives a failed import.

- [ ] **Step 1: Write the failing tests**

In `ImportRouteTests`, register the fake in the constructor:

```csharp
    private readonly InMemorySettingsRepository settings = new();
```

```csharp
        Services.AddSingleton<ISettingsRepository>(settings);
```

Add:

```csharp
    [Fact]
    public void Autopause_starts_off_with_the_default_threshold_shown()
    {
        var page = Render<ImportRoutePage>();

        page.Find("input[type=checkbox]").HasAttribute("checked").Should().BeFalse();
        var seconds = page.Find("input[type=number]");
        seconds.GetAttribute("value").Should().Be("15");
        seconds.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Enabling_autopause_saves_it_and_frees_the_threshold()
    {
        var page = Render<ImportRoutePage>();

        page.Find("input[type=checkbox]").Change(true);

        settings.AutoPause.Enabled.Should().BeTrue();
        page.Find("input[type=number]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Changing_the_threshold_saves_it()
    {
        var page = Render<ImportRoutePage>();
        page.Find("input[type=checkbox]").Change(true);

        page.Find("input[type=number]").Change("45");

        settings.AutoPause.Should().Be(new AutoPauseSettings(true, 45));
    }

    [Fact]
    public void A_threshold_outside_the_accepted_range_is_clamped_before_it_is_stored()
    {
        var page = Render<ImportRoutePage>();
        page.Find("input[type=checkbox]").Change(true);

        page.Find("input[type=number]").Change("9999");

        settings.AutoPause.ThresholdSeconds.Should().Be(300);
    }

    [Fact]
    public void A_stored_preference_is_shown_when_the_page_opens()
    {
        settings.AutoPause = new AutoPauseSettings(true, 90);

        var page = Render<ImportRoutePage>();

        page.Find("input[type=checkbox]").HasAttribute("checked").Should().BeTrue();
        page.Find("input[type=number]").GetAttribute("value").Should().Be("90");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~ImportRouteTests"`

Expected: FAIL — no checkbox or number input on the page.

- [ ] **Step 3: Write the implementation**

In `src/RoutePacer.App/Pages/ImportRoute.razor`, add the injection at the top:

```razor
@inject ISettingsRepository Settings
```

Add the fieldset after the `<InputFile ... />` line:

```razor
<fieldset class="autopause">
    <legend>Autopause</legend>
    <label>
        <input type="checkbox" checked="@autoPause.Enabled" @onchange="ToggleAutoPause" />
        Pause the ahead/behind when I stop
    </label>
    <label>
        After
        <input type="number" min="@AutoPauseSettings.MinimumSeconds" max="@AutoPauseSettings.MaximumSeconds" step="1"
               value="@autoPause.ThresholdSeconds" disabled="@(!autoPause.Enabled)" @onchange="ChangeThreshold" />
        seconds standing still
    </label>
</fieldset>
```

Extend the `@code` block:

```csharp
    private AutoPauseSettings autoPause = AutoPauseSettings.Default;

    protected override async Task OnInitializedAsync() => autoPause = await Settings.GetAutoPauseAsync();

    private Task ToggleAutoPause(ChangeEventArgs args) => Save(autoPause with { Enabled = args.Value is true });

    private Task ChangeThreshold(ChangeEventArgs args)
        => int.TryParse(args.Value?.ToString(), out var seconds)
            ? Save(autoPause with { ThresholdSeconds = seconds })
            : Task.CompletedTask;

    // Written on change rather than on import: the preference outlives the route, and a file that
    // failed to parse is no reason to lose the rider's choice.
    private async Task Save(AutoPauseSettings updated)
    {
        autoPause = updated.Clamped();
        await Settings.SaveAutoPauseAsync(autoPause);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~ImportRouteTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RoutePacer.App/Pages/ImportRoute.razor tests/RoutePacer.App.Tests/Pages/ImportRouteTests.cs
git commit -m "feat: choose autopause and its threshold when loading a route"
```

---

