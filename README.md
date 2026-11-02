# Railroader Helper Manager

A mod for [Railroader](https://store.steampowered.com/app/1683150/Railroader/)
that lets helper engines in the **middle** or at the **rear** of a train run
as MU units of the lead engine.

The game's own MU only works for engines coupled directly together. As soon
as there are cars between two engines, the trailing engine can't find the
lead. Helper Manager links engines anywhere in the same train, so a pusher
or mid-train helper follows the lead's throttle, reverser and brakes. Auto
Engineer also counts its power.

> **Status:** early development (0.1.0). Requires the Railroader 2026.1
> Experimental build and its built-in mod loader.

## Features

- **Hotkey window (Ctrl+5):** lists every engine in the selected train with
  its position, model, facing, MU/cut-out state, and whether its air is
  connected.
- **Pick a role for each engine:** Lead, Mid, Rear, or none. It's pre-filled
  from the train's layout. Live checks catch mistakes:
  - engines that aren't in the same train,
  - lead engines that aren't coupled together,
  - helpers whose air isn't connected.
- **True MU behavior for linked helpers:**
  - throttle and reverser follow the lead, including helpers facing
    backwards;
  - steam helpers set their own cutoff for their speed;
  - the lead's independent and automatic brakes apply the helpers' brakes;
  - Auto Engineer on the lead counts the helpers' tractive effort.
- **Links are saved with the cars:** they survive save/load and are shared
  with every player in multiplayer.
- **Automatic unlinking:** if a helper is uncoupled from its lead or MU is
  turned off on it, the link is removed and its throttle closed. A helper
  that drops off running light (with nothing behind it but a caboose) brakes
  to a gentle stop.

## Installation

1. In Railroader, turn on **Preferences > Experimental > Allow Unsafe DLL
   Mods**, then restart the game.
2. Download the latest release (none yet; until then, see
   [Building](#building)) and extract the `HelperManager` folder into
   `Documents\Railroader\Mods\`. The folder should contain
   `HelperManager.dll`, `0Harmony.dll`, `Info.json` and
   `LICENSE-Harmony.txt`.
3. Enable **Helper Manager** in the game's Mods screen.

## Usage

1. Select the lead locomotive.
2. Press **Ctrl+5** to open the Helper Manager window.
3. Check the role picked for each engine (Lead / Mid / Rear / —) and fix any
   errors shown under the list.
4. Press **Link helpers**. Drive the lead as normal.

Press **Unlink all** to release the helpers. They stay cut out with their
throttle closed.

## Known limitations

- **Response time:** the game copies throttle and reverser to MU units once
  a second, so helpers respond with up to a second's delay. That's the same
  as vanilla MU.
- **Brake pipe charging:** only the lead charges the brake pipe, as in
  vanilla, where a cut-out engine never feeds it. Long trains are slow to
  release.
- **Multiplayer:** the host must have the mod installed. The host is the
  one that drives the helpers.
- **Fragility:** the mod relies on the game's internal code, not a public
  modding API. A game update can break it.

## Building

Requires the .NET SDK and a Railroader install.

```powershell
dotnet build -c Release
```

The project references the game's assemblies through the `RailroaderDir`
property in `HelperManager.csproj`. Change it, or pass
`-p:RailroaderDir=...`, if the game is installed elsewhere. Each build is
copied into `Documents\Railroader\Mods\HelperManager`.

Developer notes, including how the game's MU system works and why helpers
need a patch, are in [PROJECT_HANDOFF.md](PROJECT_HANDOFF.md).

## Credits

- **[Harmony](https://github.com/pardeike/Harmony)** by Andreas Pardeike
  (MIT): bundled as `0Harmony.dll`; license in `LICENSE-Harmony.txt`.
- **[Railroader Minimap Server](https://github.com/RagguBaggu/RailroaderMinimap):**
  the sister project, whose research into the game's internals this builds
  on.

## License

MIT. See [LICENSE](LICENSE).
