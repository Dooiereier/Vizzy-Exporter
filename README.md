# Vizzy Copy Paste

A mod for **Juno: New Origins** (formerly SimpleRockets 2) that adds a
right-click **"Export to..."** action to the Vizzy visual scripting editor,
for copying blocks between programs.

Vizzy has no way to reuse blocks across programs today. This mod lets you
right-click any block, choose **Export to...**, pick one of your existing
saved Vizzy program files from a list, and the block - plus its whole column
of blocks chained below it, and anything nested inside it (loop bodies,
arguments, etc.) - gets written into that file as a new, independent block
group, placed near the middle of that program's existing content. Next time
you open that program, it's there, positioned somewhere you'll actually see
it rather than off in a stale coordinate. If the exported blocks reference
global variables, custom expressions, or custom instructions the target
program doesn't have yet, those get created there too - copied from their
real definitions in the source program (not guessed), transitively: a
pulled-in custom block that itself calls further custom blocks or variables
gets those pulled in as well.

## How it works

Vizzy programs are saved as plain XML on disk (that's the same format
`ProgramSerializer` reads and writes when you save/load a program in-game).
This mod doesn't touch the game's live in-memory editor state at all for the
"paste" side - it:

1. Reads the right-clicked block's data and serializes it (and its chain)
   into XML, using the game's own serializer, and notes which global
   variables it references.
2. Lists the `.xml` files in your Vizzy programs folder.
3. On your pick, opens that file, adds the exported block(s) as a new,
   independent top-level group positioned near the middle of that file's
   existing content, adds any of the referenced global variables the file
   doesn't already define, and saves it back to disk.

Because this only edits a file, not a running program, it works regardless
of which part/craft you have open, and there's no risky "rebuild blocks
on screen" step involved.

## Status

Built, deployed, and working in-game against the current version of Juno:
New Origins. Every game API this mod uses is a confirmed, public member -
see [`docs/IMPLEMENTATION_NOTES.md`](docs/IMPLEMENTATION_NOTES.md) for the
exact methods and what to check if a future game update ever renames one.

## How it was built

This mod's structure and its use of the game's modding hooks (Harmony +
`ModApi.Craft.Program`) are modeled on
[Vizzy Studio](https://github.com/joaofarias/VizzyStudio), an existing,
published, working mod for this same game. The one reflection-based call in
this mod is the same private method Vizzy Studio's own patches already
target.

## Requirements

- Juno: New Origins, with its **Mod Tools** package (Unity project + the
  game's own assemblies for modders - see the
  [official modding tutorials](https://www.simplerockets.com/Mods/Learn) if
  you don't already have this set up).
- [Harmony](https://github.com/pardeike/Harmony) (`0Harmony.dll`) - the same
  copy you're already using for the `jno-ipad-screen` mod works fine here.

## Setup

1. Open this repo's `Assets` folder as (or inside) a Unity project set up
   with the game's Mod Tools, the same way you already have `jno-ipad-screen`
   set up.
2. Drop your own copy of `0Harmony.dll` into `Assets/Scripts/` (it's
   gitignored here on purpose - see [Licensing](#licensing-notes-on-what-isnt-in-this-repo)
   below).
3. Build/export the mod the same way you already do for your other Juno mod,
   and deploy it into the game's mods folder for testing.

## Using it

1. Right-click any block in the Vizzy editor.
2. Choose **Export to...**.
3. Pick one of your existing saved programs from the list.
4. Open that program (any part, any craft) - your exported block(s) are
   there as a new group.

If export fails for some reason, it tells you why instead of failing
silently - nothing is written until it can confirm the target file is valid.

## Known limitations (v1)

- **A colliding name is never touched**, for a global variable, custom
  expression, or custom instruction alike. If the target already defines one
  with the same name as one the export needs, that existing definition is
  left exactly as-is (on purpose - it might be a deliberately different
  type/value/implementation, and overwriting it could break other blocks in
  the target that depend on it). If it turns out to be meaningfully
  different from what the source expected, the pasted block may misbehave
  until you sort that out by hand.
- **Placement is a best guess, not the real camera position**, for the main
  exported block. There's no way to read a program's actual on-screen
  scroll/zoom state from outside the running editor - the saved file stores
  nothing like it - so the export is placed near the target's `FlightStart`
  ("on start") event instead, since that's present in essentially every
  program and, by convention, tends to sit close to where the view lands
  when you open one (falling back to the plain origin if a program somehow
  has none), with a little random jitter so repeated exports into the same
  file don't stack exactly on top of each other. An earlier version averaged
  the position of every positioned top-level block instead; that broke down
  on real, heavily-used programs where most blocks never had a `pos` at all
  and the tiny handful that did skewed the average toward an arbitrary
  corner of the canvas, nowhere near where the user was actually looking.
  Any newly-created custom instruction definitions keep their original
  position from the source program instead of being placed near
  `FlightStart` - they're supporting code, not the thing you're looking for,
  and custom blocks are typically clustered together off in their own area
  of the canvas already.
- **Target must be an existing file.** "Export to..." lists your saved
  programs; it doesn't create a new one. (Easy to add later if you want it -
  just needs a "New file..." entry in `ExportTargetPicker`.)

## Project layout

```
Assets/
  Vizzy Exporter.asmdef
  ModData.asset                             - Mod Tools manifest (name, assemblies, version)
  Scripts/
    Mod.cs                                  - entry point, sets up Harmony
    ModSettings.cs                          - the mod's (currently empty) settings page
    DeployTools.cs                          - editor menu to copy the built .sr2-mod into Juno's mods folder
    Bridge/SerializerBridge.cs              - the game's real (confirmed) serialization API calls
    Export/VizzyExportSnippet.cs            - captures a block (+ whole chain, incl. nested bodies) as XML
    Export/VariableDependencyResolver.cs    - finds referenced global variables + their real definitions
    Export/CustomBlockDependencyResolver.cs - finds referenced custom expressions/instructions + their real definitions
    Export/VizzyProgramFileExporter.cs      - lists program files, writes the export, positions it, adds missing dependencies
    Patches/ContextMenuPatcher.cs           - hooks right-click on blocks
    UI/BlockContextMenu.cs                  - the "Export to another Vizzy" popup
    UI/ExportTargetPicker.cs                - the scrollable file-list popup
    UI/PopupWidgets.cs                      - shared button/dialog helpers
docs/
  IMPLEMENTATION_NOTES.md                   - what's confirmed vs. best-effort, and how to fix guesses
```

## Licensing notes (what isn't in this repo)

`0Harmony.dll` and the game's own assemblies (`Assembly-CSharp.dll`,
`ModApi.dll`, etc.) aren't included here - they're third-party/Jundroo's own
binaries, not this mod's code. Grab them from the game's Mod Tools package
and your existing Harmony install, same as for `jno-ipad-screen`.

This repo is MIT-licensed (see `LICENSE`) for the code actually written here.
