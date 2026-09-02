# Vizzy Copy Paste

A mod for **Juno: New Origins** (formerly SimpleRockets 2) that adds right-click
**Copy** and **Paste** to the Vizzy visual scripting editor.

Vizzy has no copy/paste today. This mod lets you right-click any block, copy it
plus its whole chain of following blocks (and anything nested inside it - loop
bodies, arguments, etc.), then right-click any block in *any* Vizzy program -
a different part, a different craft, even after restarting the game - and
paste it back in. The clipboard is just XML text sitting in your OS clipboard,
so it survives all of that for free.

## Status

Copy is built on confirmed, working game APIs (see below) and should work as
soon as this is compiled against your game version. Paste's automatic block
placement uses a couple of educated guesses about private method names that
**need to be checked against this game version's code** - see
[`docs/IMPLEMENTATION_NOTES.md`](docs/IMPLEMENTATION_NOTES.md) for exactly
which ones, and how to fix them with a decompiler if they're wrong. Nothing
in the mod can crash the game or lose your clipboard contents if a guess is
wrong - it just shows you a message and leaves the mod's other features
working.

## How it was built

This mod's structure and its use of the game's modding hooks (Harmony +
`ModApi.Craft.Program`) are modeled directly on
[Vizzy Studio](https://github.com/joaofarias/VizzyStudio), an existing,
published, working mod for this same game. Wherever this mod calls into a
private/internal game method, that call is either the same one Vizzy Studio
already uses (confirmed to exist), or clearly marked as a best-effort guess
that mirrors Vizzy Studio's own patterns for finding such members.

## Requirements

- Juno: New Origins, with its **Mod Tools** package (Unity project + the
  game's own assemblies for modders - see the
  [official modding tutorials](https://www.simplerockets.com/Mods/Learn) if
  you don't already have this set up).
- [Harmony](https://github.com/pardeike/Harmony) (`0Harmony.dll`) - the same
  copy you're already using for the `jno-ipad-screen` mod works fine here.
- A decompiler (dnSpy or ILSpy) pointed at your installed game's
  `Assembly-CSharp.dll`, in case any of the best-effort guesses in
  `NodeBuilderBridge.cs` need adjusting for your game version.

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
2. **Copy** - copies that block and everything below/inside it to your
   clipboard.
3. Open the program you want to paste into (any part, any craft).
4. Right-click any existing block there and choose **Paste**. The pasted
   chain appears under your cursor, ready to drop, just like the game's own
   block-cloning drag.

If Paste can't place the block automatically on your game version, it tells
you so instead of failing silently - your copy is still safe on the
clipboard, and `docs/IMPLEMENTATION_NOTES.md` explains the one file to fix.

## Project layout

```
Assets/
  Vizzy Copy Paste.asmdef
  Scripts/
    Mod.cs                        - entry point, sets up Harmony
    Clipboard/VizzyClipboard.cs    - serialize/deserialize + OS clipboard I/O
    Bridge/NodeBuilderBridge.cs    - the handful of private-API calls, all in one place
    Patches/ContextMenuPatcher.cs  - hooks right-click on blocks
    UI/BlockContextMenu.cs         - the Copy/Paste popup menu
docs/
  IMPLEMENTATION_NOTES.md          - what's confirmed vs. best-effort, and how to fix guesses
```

## Licensing notes (what isn't in this repo)

`0Harmony.dll` and the game's own assemblies (`Assembly-CSharp.dll`,
`ModApi.dll`, etc.) aren't included here - they're third-party/Jundroo's own
binaries, not this mod's code. Grab them from the game's Mod Tools package
and your existing Harmony install, same as for `jno-ipad-screen`.

This repo is MIT-licensed (see `LICENSE`) for the code actually written here.
