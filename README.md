# Vizzy Copy Paste

A mod for **Juno: New Origins** (formerly SimpleRockets 2) that adds a
right-click **"Export to..."** action to the Vizzy visual scripting editor,
for copying blocks between programs.

Vizzy has no way to reuse blocks across programs today. This mod lets you
right-click any block, choose **Export to...**, pick one of your existing
saved Vizzy program files from a list, and the block - plus its whole column
of blocks chained below it, and anything nested inside it (loop bodies,
arguments, etc.) - gets written into that file as a new block group. Next
time you open that program, it's there.

## How it works

Vizzy programs are saved as plain XML on disk (that's the same format
`ProgramSerializer` reads and writes when you save/load a program in-game).
This mod doesn't touch the game's live in-memory editor state at all for the
"paste" side - it:

1. Reads the right-clicked block's data and serializes it (and its chain)
   into XML, using the game's own serializer.
2. Lists the `.xml` files in your Vizzy programs folder.
3. On your pick, opens that file, adds the exported block(s) as a new
   top-level group, and saves it back to disk.

Because this only edits a file, not a running program, it works regardless
of which part/craft you have open, and there's no risky "rebuild blocks
on screen" step involved.

## Status

The one non-public game API this mod reaches into
(`ProgramSerializer.SerializeProgramNode`) is confirmed to exist by name and
signature - see [`docs/IMPLEMENTATION_NOTES.md`](docs/IMPLEMENTATION_NOTES.md)
for exactly how, and what to check if it ever needs adjusting for a game
update. Everything else uses directly-confirmed, public members.

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

- **Global variables aren't copied.** If the exported block references a
  global variable, you'll need a variable of the same name in the target
  program too, or the pasted block will reference something that doesn't
  exist. (Vizzy Studio's own "References" feature solves this more
  generally - out of scope here for now.)
- **Target must be an existing file.** "Export to..." lists your saved
  programs; it doesn't create a new one. (Easy to add later if you want it -
  just needs a "New file..." entry in `ExportTargetPicker`.)
- **Position isn't reflowed.** The exported blocks keep whatever on-canvas
  position they had in the source program, so they may land on top of
  existing blocks in the target file - just drag them where you want once
  you open it.

## Project layout

```
Assets/
  Vizzy Copy Paste.asmdef
  Scripts/
    Mod.cs                              - entry point, sets up Harmony
    Bridge/SerializerBridge.cs           - the one private-API call, isolated here
    Export/VizzyExportSnippet.cs         - captures a block (+ chain) as XML
    Export/VizzyProgramFileExporter.cs   - lists program files, writes the export
    Patches/ContextMenuPatcher.cs        - hooks right-click on blocks
    UI/BlockContextMenu.cs               - the "Export to..." popup
    UI/ExportTargetPicker.cs             - the file-list popup
    UI/PopupWidgets.cs                   - shared button/dialog helpers
docs/
  IMPLEMENTATION_NOTES.md                - what's confirmed vs. best-effort, and how to fix guesses
```

## Licensing notes (what isn't in this repo)

`0Harmony.dll` and the game's own assemblies (`Assembly-CSharp.dll`,
`ModApi.dll`, etc.) aren't included here - they're third-party/Jundroo's own
binaries, not this mod's code. Grab them from the game's Mod Tools package
and your existing Harmony install, same as for `jno-ipad-screen`.

This repo is MIT-licensed (see `LICENSE`) for the code actually written here.
