# Helper Manager — Project Handoff

Developer/agent-facing notes for picking this project up without the chat
history. The sister project `D:\RailroaderMinimap` (its `PROJECT_HANDOFF.md`)
has the general Railroader 2026.1 modding notes: built-in loader behavior,
Info.json, logging, Unity gotchas, decompiling with `ilspycmd`. This file
covers only what is specific to this mod.

## Goal

A Railroader 2026.1 (Experimental) mod for the built-in loader. A hotkey
(Ctrl+5) opens an in-game window where the player picks a lead engine and
optional mid-train and rear helpers in the same consist. The helpers are then
MU'd to the lead: the one engine that is not MU'd and not cut out.

## Status

- **Phase 1 (done, verified in-game 2026-11-01):** project skeleton, bundled
  Harmony plus resolver, Ctrl+5 hotkey, a window listing the engines in the
  selected consist (position, model, facing, MU/cut-out state, air
  connection). It updates live when MU is toggled in the Car Inspector.
- **Phase 2 (verified in-game 2026-11-01):** role picker per engine,
  validation, Link / Unlink all / Reset, links stored on cars, the Harmony
  prefix on `FindSourceLocomotive`, automatic unlinking (host), and the
  fallback loop that copies controls. Verified:
  - the patch applies;
  - throttle and reverser follow the lead, including reversed helpers;
  - independent and automatic brakes reach the helpers;
  - uncoupling unlinks;
  - links survive save/load;
  - Auto Engineer works with helpers.

  Added afterwards and not yet re-tested: a detached helper running light
  brakes to a stop.
- **Phase 3:** polish: settings for hotkey and update rate, a warning when
  the host lacks the mod, a `/helpers` console command.

## Files

```
GameModEntry.cs        entry point; installs HarmonyResolver first, then the core object
HarmonyResolver.cs     AssemblyResolve handler for the bundled 0Harmony.dll; HarmonyStatus probe
HelperManagerCore.cs   MonoBehaviour: hotkey, world lifecycle, window refresh polling
HelperWindow.cs        the custom window (game UI toolkit): role dropdowns, messages, buttons
ConsistReader.cs       reads the selected car's coupled cut, its engines and engine groups
LinkPlanner.cs         default/existing roles; validates roles into a LinkPlan
HelperLinks.cs         link keys on cars: apply/unlink, and the registry (ActiveSources) + host repair
MuSourcePatch.cs       Harmony prefix on BaseLocomotive.FindSourceLocomotive(LogicalEnd)
FallbackDriver.cs      host-only throttle/reverser copying when the patch isn't active
HotkeyConflictCheck.cs finds game actions bound to the hotkey's key (toast + log at world load)
Log.cs                 logging indirection (set from IModContext.Log)
```

Build with `dotnet build -c Release`. The `DeployToModsFolder` target copies
`HelperManager.dll`, `0Harmony.dll`, `Info.json` and `LICENSE-Harmony.txt`
to `Documents\Railroader\Mods\HelperManager`.

## Why vanilla MU can't do helpers (the core finding)

- **MU state** is two per-car `KeyValueObject` flags, `mu` and `cutOut`
  (`PropertyChange.Control.Mu` / `.CutOut`). The Car Inspector's MU toggle
  sets both.
- **The vanilla MU loop:** `BaseLocomotive.PeriodicUpdateForMu()` runs once
  a second, host only. For an MU'd unit it calls
  `FindMuSourceLocomotive()`, copies that engine's `AbstractThrottle`, and
  sets the reverser to
  `CutoffSettingForVelocity(velocity) * (FrontIsA == src.FrontIsA ? 1 : -1) * Sign(src reverser)`,
  rounded up to 0.05.
- **Why it fails for helpers:** `FindSourceLocomotive()` walks the consist
  in each direction, skipping tenders and cut-out engines, and **returns null
  at the first car that isn't a locomotive**. Vanilla MU therefore only works
  for adjacent engines.
- **Everything else that depends on the same lookup:**
  - The air brakes: `LocomotiveAirSystem._ShouldDeferToLocomotiveAir`. This
    is how an MU'd unit's independent brake follows the lead.
  - Auto Engineer: `AutoEngineer.CachedMuConnectedLocomotives()`, used by
    `TrainPlantModel` (tractive effort, cylinder brake force),
    `RolloutPlantBuilder`, and braking calculations.
- **Why reflection tricks don't work:**
  - AE's `_cachedLocomotives` is cleared and rebuilt within the same physics
    step (`InvalidateCachedCars()` in its main loop).
  - The air deferral cache is recomputed every air step by
    `SetAirGraph.UpdateLocomotiveValves`.

  So the fix is a **Harmony patch on the MU source lookup**: for an engine
  this mod linked, return its lead. That one patch fixes throttle, reverser,
  brakes and AE together. It is called per engine per physics step, so it
  must be a cheap dictionary lookup. See "Phase 2 design" for the exact
  target.
- **Public vs internal:** `IsMuEnabled` and `FindMuSourceLocomotive` are
  internal. Read MU as `ControlProperties[PropertyChange.Control.Mu].BoolValue`.
  Everything else needed is public: `ControlProperties`, `SendPropertyChange`,
  `FrontIsA`, `CutoffSettingForVelocity` (diesel returns 1),
  `locomotiveControl.AbstractThrottle` / `AbstractReverser`,
  `EnumerateCoupled`, `EnumerateAirOpen`.
- **Vanilla quirk:** Unity's `Mathf.Sign(0)` is 1, so a centred lead
  reverser still drives MU units forward. The fallback loop should send 0
  instead.

## Phase 2 design

- **Storage:** each linked engine carries `helperMgr.lead` (the
  controller's `car.id`) and `helperMgr.role` (`lead|mid|rear`). The
  controller has no keys. Role `lead` is an extra unit coupled to the
  controller, which vanilla MU handles anyway. Mid and Rear behave
  identically; the role is only recorded and checked for warnings.
- **Controller:** the Lead-role engine that isn't MU'd or cut out. Ties go
  to the selected engine, then the frontmost. If every Lead engine is MU'd,
  the chosen one is cut in.
- **Link order:** cut in the controller, then unlink engines set to "—",
  then for each helper write cut-out and MU first and the link keys last.
  `StateManager.ApplyLocal` applies locally then sends in order, so the
  host's rescan never sees keys on a helper with MU off.
- **Unlink:** clear the keys, MU off, throttle 0 (so a helper doesn't keep
  pulling). Cut-out is left on.
- **Detached helpers:** when the host's rescan finds a helper separated from
  its lead (uncoupled, or the lead removed) and the helper is **running
  light** (its cut is only engines, tenders and a caboose), it also cuts the
  helper in and sets the independent brake to 0.3 for a gentle stop.
  - Cutting in is required, because a cut-out loco with no MU source has
    vented brakes.
  - It isn't done when other cars are still coupled: cutting in sets the
    automatic brake to release (`trainBrakeSetting` 0 = full feed pressure),
    which would release those cars' brakes.
- **Registry:** `HelperLinks.Rescan` runs twice a second on every machine.
  It rebuilds `ActiveSources` (helper -> lead, valid links only) as a new
  dictionary and swaps it in, so the prefix's reads are never torn. A link
  is valid when:
  - the lead exists and isn't the helper itself,
  - the helper is MU'd,
  - the lead is neither MU'd nor cut out,
  - both are in the same coupled cut.

  The host unlinks invalid links with a toast, but not during the first 5
  seconds after a world load.
- **Patch target:** `FindSourceLocomotive(LogicalEnd)` instead of
  `FindMuSourceLocomotive`, because its loop and throw stop Mono's JIT from
  inlining it past the patch. A prefix skips the vanilla walk entirely for
  linked engines. `MuSourcePatch` keeps its Harmony instance in an `object`
  field, so the class loads even without 0Harmony.
- **Brakes:** `SetAirGraph.UpdateLocomotiveValves` makes a cut-out loco with
  an MU source fill its brake cylinder to the source's
  `locomotiveBrakeControlLine`. That value is the max of the independent
  brake and the automatic-brake-derived pressure, with bail-off. **A cut-out
  loco with no source has its brake cylinder vented**, so in fallback mode
  helpers have no brakes of their own. With the patch they follow the lead's
  automatic and independent brakes, with no distance limit.
- **Brake pipe charging:** cut-out locos close both brake-pipe feed valves
  (`connectionStart + 2/+3`), so only the lead's feed valve and main
  reservoir charge the pipe. That's vanilla behavior for any cut-out unit,
  and long trains charge slowly. Real distributed power would feed from
  every unit; that would need patching the air graph (possible future
  feature).
- **Window draft:** role choices are kept in a draft keyed by the selected
  engine, the engine ids and their link keys. It reloads from the links (or
  defaults) when any of those change, so applying Link shows the stored
  state. Buttons re-read and re-plan at press time.

## Other game facts used here

- **Consist order:** `IntegrationSet` stores cars in order, and every car's
  logical A end faces the lower index. `Car.EnumerateCoupled(LogicalEnd)`
  yields the whole coupled cut end to end. `EnumerateCoupled(Car.End.F)`
  starts from the end the car's front faces. Because logical ends line up
  across the set, comparing `FrontIsA` gives relative facing for engines
  any distance apart.
- **Custom per-car keys:** keys written to a car's `KeyValueObject` through
  `PropertyChange` need only Crew access unless they start with a reserved
  prefix (`_`, `ops.`, `load.`, `door.`, `gate.`, `style`, `owned`,
  `oiled`, `hotbox`). They replicate to every player and are copied into
  `Car.Descriptor()` and the snapshot's per-car properties
  (`TrainController.HandleSnapshotCars`), so they should persist in saves
  (verify). Write with `StateManager.ApplyLocal(new PropertyChange(id, key,
  new StringPropertyValue(v)))`; clear with `NullPropertyValue`.
- **Custom windows:** register with
  `WindowManager.Shared.RegisterCustomWindow(new CustomWindowDefinition{...})`
  and open with `CustomWindowHandle.Create(id).Toggle()`. Its constructor is
  internal; use `Create`. Contents are built with `UIPanelBuilder`. The game
  calls `DestroyAllCustomWindows()` on unload, which also drops the
  definitions, so register on demand. The game's own example is
  `UI.Console.Commands.LoadInventoryCommand`.
- **Input:**
  - `GameInput.MovementInputEnabled` is false while a TMP input field has
    focus; use it to ignore the hotkey while typing.
  - Bindings live in an input actions asset, not code
    (`sharedassets3.assets`; `grep -a -o "<Keyboard>/[A-Za-z0-9]*"` lists
    every bound key). **A plain-key binding also fires while Ctrl or Shift is
    held**, so a mod hotkey must use a key the game doesn't bind at all. H is
    `Game/Horn`; Ctrl+H blew the horn, hence Ctrl+5. Keys unbound as of
    2026-10-31: 5-8, F4, F9-F12, backslash, Insert, Home, End, Page Up/Down,
    numpad. `HotkeyConflictCheck` catches player rebinds at world load.
  - Events: `SelectedCarChanged` exists in `Game.Events`. There is no
    coupling event on the EventBus, only the per-car
    `Car.OnCoupledDidChange`.

## Harmony

- **The game does not ship Harmony.** Nothing in `Railroader.dll`
  references it. Copies in `Managed\` come from UMM or Railloader, which are
  orphaned on 2026.1, so don't rely on them.
- **We ship pardeike Harmony 2.3.6** (NuGet `Lib.Harmony`), unlisted in
  Info.json. The loader (`ModAssemblyManager`) `Assembly.LoadFrom`s only
  listed DLLs, each of which must contain an `IModEntryPoint`, and it has no
  resolver of its own.
  - `HarmonyResolver` returns an already-loaded 0Harmony if there is one,
    otherwise it loads ours from the mod folder.
  - Harmony 2.x isn't strong-named and shares patch state across copies.
  - BepInEx's HarmonyX isn't used in this community.
- **No Harmony types outside Harmony-only classes:** `GameModEntry` and
  anything the loader inspects or compiles early must not touch a Harmony
  type. `HarmonyStatus.Describe()` is the out-of-line probe.
- **Unload/reload:** the loader can unload and reload mods mid-session.
  Unpatch in `OnModUnload` using `Unpatch(method, HarmonyPatchType, id)`,
  which has existed since 2.0.
- **Verified in-game:** Unity's Mono runtime finds `0Harmony.dll` next to
  our DLL on its own (it probes the LoadFrom directory), so the resolver
  never ran. Keep it anyway as a safety net; it costs nothing.

## UI notes

- **Filling the window:** `VScrollView` already has flexible height. Don't
  also add `AddExpandingVerticalSpacer()`: the two split the spare height,
  and the list ends up half-size.
- **Extra reference:** `ConfigureLayout(childAlignment: ...)` needs a
  `UnityEngine.TextRenderingModule` reference for `TextAnchor`.
- **Rich text:** labels accept TMP rich text (`<b>`, `<size=%>`,
  `<color=#hex>`).

## To verify in-game (Phase 2)

- `Player.log` shows `MU source patch applied.` and no fallback warning
  appears in the window.
- Defaults: the selected engine's group is Lead, the last group is Rear,
  others are Mid. Errors and warnings update as dropdowns change.
- After Link:
  - helpers show "Linked to <lead>" and appear as MU in the engine roster;
  - throttle and reverser follow the lead (vanilla copies once a second),
    including reversed helpers;
  - steam helpers set their own cutoff for their speed;
  - the lead's independent and automatic brakes apply the helpers' brakes.
- Auto Engineer on the lead: it should count helper power (smooth
  starts, no overspeed).
- Uncouple a helper: the host unlinks it with a toast and its throttle
  goes to 0.
- Turning MU off on a helper in the Car Inspector: unlinked with a toast.
- Save and reload: links come back (the keys persist).
- Unlink all: helpers go to MU off and throttle 0, and stay cut out.
