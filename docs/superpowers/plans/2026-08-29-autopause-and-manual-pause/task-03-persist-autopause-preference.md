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

### Task 3: Persist the autopause preference

A standing preference in its own IndexedDB store, independent of the route. Nothing consumes it yet; this task ends with it round-tripping.

**Files:**
- Create: `src/RoutePacer.Core/Domain/AutoPauseSettings.cs`
- Create: `src/RoutePacer.Core/Storage/ISettingsRepository.cs`
- Create: `src/RoutePacer.App/Storage/IndexedDbSettingsRepository.cs`
- Modify: `src/RoutePacer.App/wwwroot/js/storage.js`
- Modify: `src/RoutePacer.App/Program.cs`
- Modify: `tests/RoutePacer.App.Tests/Fakes.cs`
- Test: `tests/RoutePacer.App.Tests/Storage/IndexedDbRepositoryContractTests.cs`
- Test: `tests/RoutePacer.E2E/OfflinePwaTests.cs`

**Interfaces:**
- Produces: `AutoPauseSettings(bool Enabled, int ThresholdSeconds)` with `const int MinimumSeconds = 5`, `MaximumSeconds = 300`, `DefaultSeconds = 15`, `static AutoPauseSettings Default`, and `AutoPauseSettings Clamped()`.
- Produces: `ISettingsRepository` with `Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken = default)` and `Task SaveAutoPauseAsync(AutoPauseSettings, CancellationToken = default)`.
- Produces: `IndexedDbSettingsRepository(IIndexedDbModule db)` and its nested `record AutoPauseDto(bool Enabled, int ThresholdSeconds)`.
- Produces: `InMemorySettingsRepository` test fake with a settable `AutoPause` property and a `SaveCount`.
- Produces: JS `getAutoPause()` and `saveAutoPause(settings)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/RoutePacer.App.Tests/Storage/IndexedDbRepositoryContractTests.cs`:

```csharp
    [Fact]
    public async Task Autopause_defaults_to_off_when_nothing_is_stored()
    {
        var settings = await new IndexedDbSettingsRepository(new RecordingIndexedDbModule()).GetAutoPauseAsync();

        settings.Should().Be(AutoPauseSettings.Default);
        settings.Enabled.Should().BeFalse();
        settings.ThresholdSeconds.Should().Be(15);
    }

    [Fact]
    public async Task A_stored_autopause_preference_is_read_back()
    {
        var module = new RecordingIndexedDbModule();
        module.Results["getAutoPause"] = new IndexedDbSettingsRepository.AutoPauseDto(true, 45);

        var settings = await new IndexedDbSettingsRepository(module).GetAutoPauseAsync();

        settings.Should().Be(new AutoPauseSettings(true, 45));
    }

    // A hand-edited store must not produce a threshold that never fires or fires instantly.
    [Theory]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    [InlineData(9999, 300)]
    public async Task A_threshold_outside_the_accepted_range_is_clamped_on_read(int stored, int expected)
    {
        var module = new RecordingIndexedDbModule();
        module.Results["getAutoPause"] = new IndexedDbSettingsRepository.AutoPauseDto(true, stored);

        (await new IndexedDbSettingsRepository(module).GetAutoPauseAsync()).ThresholdSeconds.Should().Be(expected);
    }

    [Fact]
    public async Task Saving_autopause_clamps_before_it_reaches_storage()
    {
        var module = new RecordingIndexedDbModule();

        await new IndexedDbSettingsRepository(module).SaveAutoPauseAsync(new AutoPauseSettings(true, 1000));

        var call = module.Calls.Should().ContainSingle(c => c.Name == "saveAutoPause").Subject;
        call.Args.Should().ContainSingle().Which.Should().Be(new AutoPauseSettings(true, 300));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~IndexedDbRepositoryContractTests"`

Expected: FAIL to compile — `AutoPauseSettings` and `IndexedDbSettingsRepository` do not exist.

- [ ] **Step 3: Write the domain record and the repository interface**

Create `src/RoutePacer.Core/Domain/AutoPauseSettings.cs`:

```csharp
namespace RoutePacer.Core.Domain;

/// <summary>
/// A standing rider preference, not a property of the route: it outlives any one import, so a rider
/// who always wants the same autopause sets it once.
/// </summary>
public sealed record AutoPauseSettings(bool Enabled, int ThresholdSeconds)
{
    public const int MinimumSeconds = 5;
    public const int MaximumSeconds = 300;
    public const int DefaultSeconds = 15;

    public static AutoPauseSettings Default { get; } = new(false, DefaultSeconds);

    public AutoPauseSettings Clamped()
        => this with { ThresholdSeconds = Math.Clamp(ThresholdSeconds, MinimumSeconds, MaximumSeconds) };
}
```

Create `src/RoutePacer.Core/Storage/ISettingsRepository.cs`:

```csharp
using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Storage;

/// <summary>
/// Rider preferences that outlive the route and the ride. Separate from
/// <see cref="IRouteRepository"/> precisely so that importing a route does not reset them.
/// </summary>
public interface ISettingsRepository
{
    Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default);
    Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default);
}
```

Create `src/RoutePacer.App/Storage/IndexedDbSettingsRepository.cs`:

```csharp
using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Storage;

public sealed class IndexedDbSettingsRepository(IIndexedDbModule db) : ISettingsRepository
{
    public async Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default)
    {
        var dto = await db.InvokeAsync<AutoPauseDto>("getAutoPause").ConfigureAwait(false);
        return dto is null ? AutoPauseSettings.Default : new AutoPauseSettings(dto.Enabled, dto.ThresholdSeconds).Clamped();
    }

    public Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default)
        => db.InvokeVoidAsync("saveAutoPause", [settings.Clamped()]).AsTask();

    public sealed record AutoPauseDto(bool Enabled, int ThresholdSeconds);
}
```

- [ ] **Step 4: Add the settings store to the JS module**

In `src/RoutePacer.App/wwwroot/js/storage.js`, change the version constant and extend the comment above it:

```js
// Version 2 drops the ride history stores. RoutePacer is a pacing aide, not a recorder -- the rider
// already has something recording the ride -- so finished rides are no longer kept, and the upgrade
// deletes any that version 1 left behind rather than stranding them on the device forever.
// Version 3 adds rider preferences, which outlive both the route and the ride.
const databaseVersion = 3;
```

Inside `onupgradeneeded`, after the `active_ride_points` line:

```js
      if (!db.objectStoreNames.contains("settings")) db.createObjectStore("settings", { keyPath: "key" });
```

At the end of the file:

```js
// One row, so the key is a constant. Absent means the rider has never chosen, which the caller
// reads as the default rather than as an error.
export const getAutoPause = () => openDatabase().then(db => new Promise((resolve, reject) => {
  const tx = db.transaction(["settings"]);
  const row = tx.objectStore("settings").get("autoPause");
  tx.oncomplete = () => { db.close(); resolve(row.result ?? null); };
  tx.onerror = () => { db.close(); reject(transactionError(tx, "getAutoPause")); };
}));

export const saveAutoPause = settings => withTransaction(["settings"], "readwrite", tx =>
  tx.objectStore("settings").put({ key: "autoPause", enabled: settings.enabled, thresholdSeconds: settings.thresholdSeconds }));
```

- [ ] **Step 5: Register the repository and add the test fake**

In `src/RoutePacer.App/Program.cs`, after the `IRideRepository` registration:

```csharp
builder.Services.AddScoped<ISettingsRepository, IndexedDbSettingsRepository>();
```

In `tests/RoutePacer.App.Tests/Fakes.cs`, after `InMemoryRideRepository`:

```csharp
public sealed class InMemorySettingsRepository : ISettingsRepository
{
    public AutoPauseSettings AutoPause { get; set; } = AutoPauseSettings.Default;
    public int SaveCount { get; private set; }

    public Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(AutoPause);

    public Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default)
    {
        AutoPause = settings.Clamped(); SaveCount++;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Update the E2E test that pins the store list**

`tests/RoutePacer.E2E/OfflinePwaTests.cs` asserts the object stores exactly, so the new store fails
it. In `The_imported_route_survives_a_reload_because_it_lives_in_indexeddb`, replace the assertion
and extend the comment:

```csharp
        // The version 2 upgrade drops the ride history stores. Their absence is the schema-level
        // statement that finished rides are not kept. Version 3 adds rider preferences, which
        // outlive both the route and the ride.
        stores.Should().BeEquivalentTo(["routes", "route_points", "active_ride", "active_ride_points", "settings"]);
```

- [ ] **Step 7: Prove the upgrade does not cost the rider their route**

A hand-built version 2 database is the only way to exercise the upgrade path against data rather
than against an empty one — a fresh browser profile creates version 3 outright and never runs it.
Add to `tests/RoutePacer.E2E/OfflinePwaTests.cs`:

```csharp
    [Fact]
    public async Task A_version_2_database_gains_the_settings_store_without_losing_its_route()
    {
        await using var context = await NewContextAsync();
        var page = await OpenAsync(context);

        await page.EvaluateAsync(SeedVersion2Database);

        // The import page reads the autopause preference when it opens, which is what makes the
        // application open the database and run the upgrade. It needs no valid route to render, so
        // the deliberately incomplete seeded row cannot fail the page before the upgrade happens.
        await page.GotoAsync($"{app.BaseUrl}/import");
        await page.WaitForSelectorAsync("input[type=file]");

        var state = await page.EvaluateAsync<UpgradedDatabase>(ReadDatabaseShape);

        state.Version.Should().Be(3);
        state.Names.Should().Contain("settings");
        state.Routes.Should().Be(1, "the upgrade is additive and must not cost the rider their route");
    }

    private sealed record UpgradedDatabase(int Version, string[] Names, int Routes);

    private const string SeedVersion2Database = @"
        async () => {
          await new Promise((resolve, reject) => {
            const request = indexedDB.deleteDatabase('routepacer');
            request.onsuccess = () => resolve();
            request.onerror = () => reject(request.error);
          });
          const db = await new Promise((resolve, reject) => {
            const request = indexedDB.open('routepacer', 2);
            request.onupgradeneeded = () => {
              const created = request.result;
              created.createObjectStore('routes', { keyPath: 'routeId' });
              created.createObjectStore('route_points', { keyPath: ['routeId', 'index'] });
              created.createObjectStore('active_ride', { keyPath: 'rideId' });
              created.createObjectStore('active_ride_points', { keyPath: ['rideId', 'sequence'] });
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
          });
          await new Promise((resolve, reject) => {
            const tx = db.transaction(['routes'], 'readwrite');
            tx.objectStore('routes').put({ routeId: 'seeded', name: 'Seeded route' });
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
          });
          db.close();
        }";

    private const string ReadDatabaseShape = @"
        async () => {
          const db = await new Promise((resolve, reject) => {
            const request = indexedDB.open('routepacer');
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
          });
          const version = db.version;
          const names = Array.from(db.objectStoreNames);
          const routes = await new Promise((resolve, reject) => {
            const request = db.transaction(['routes']).objectStore('routes').getAll();
            request.onsuccess = () => resolve(request.result.length);
            request.onerror = () => reject(request.error);
          });
          db.close();
          return { version, names, routes };
        }";
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~IndexedDbRepositoryContractTests"`

Expected: PASS, including the three clamping theory cases.

Then the E2E pair, which builds and publishes the app and is slow:

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~OfflinePwaTests"`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/RoutePacer.Core/Domain/AutoPauseSettings.cs src/RoutePacer.Core/Storage/ISettingsRepository.cs src/RoutePacer.App/Storage/IndexedDbSettingsRepository.cs src/RoutePacer.App/wwwroot/js/storage.js src/RoutePacer.App/Program.cs tests/RoutePacer.App.Tests/Fakes.cs tests/RoutePacer.App.Tests/Storage/IndexedDbRepositoryContractTests.cs tests/RoutePacer.E2E/OfflinePwaTests.cs
git commit -m "feat: remember the rider's autopause preference across routes"
```

---

