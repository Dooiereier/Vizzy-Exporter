# Handoff notes for Claude Code

This file exists so a fresh Claude Code session in this repo has full context
without the user re-explaining it. It was written by a Claude (Cowork) cloud
session that could write code but had no Unity install, no game files, and no
GitHub push access - all real building/testing/pushing still needs to happen
here, locally.

## What this mod does

Right-click a block in Juno: New Origins' Vizzy visual scripting editor, pick
**"Export to..."**, choose one of your other saved Vizzy program files from a
list, and the block - plus the whole chain of blocks below it, and anything
nested inside it - gets copied into that file's XML on disk as a new,
independent block group. Missing global variables it references get created
in the target too. See `README.md` for the user-facing description and
`docs/IMPLEMENTATION_NOTES.md` for the technical detail on every API this mod
touches, with a confirmed-vs-best-effort risk rating for each one.

## Status: written but never compiled or run

Everything in `Assets/Scripts/` was written by reading the source of two real,
published mods for this exact game (`joaofarias/VizzyStudio` and
`kroryan/VizzyCode` - see `docs/IMPLEMENTATION_NOTES.md` for exactly what was
confirmed from each), not by compiling against the game's own assemblies or
running it in Unity. **Treat this as a first draft that needs a real build and
an in-game test pass, not as finished/verified code.**

The one genuinely uncertain piece is `Assets/Scripts/Patches/ContextMenuPatcher.cs`
best-effort-patching `OnPointerClick` on three candidate classes because it
wasn't confirmed which one fires for a plain instruction block's right-click
(only `VariableElementScript` is confirmed). If right-click does nothing in
testing, check the Player log for `[Vizzy Copy Paste] Hooked right-click via ...`
lines to see what actually got patched, then check the real class in
dnSpy/ILSpy and adjust `ContextMenuPatcher.Apply`.

## Immediate next steps, in order

1. **Get this code onto your machine with real git history.** The zip you
   have contains a full local git repo (2 commits) that was never
   successfully pushed - the cloud sandbox this was built in only has push
   access to a pre-authorized repo allowlist it couldn't add
   `Dooiereier/vizzy-copy-paste` to. From inside this folder:
   `git remote -v` should already show `origin` pointing at that GitHub repo
   (it's an empty repo on GitHub right now). Just `git push -u origin main`
   with your own credentials and it should go through normally.
2. **Set up the Unity project** the same way you already have
   `jno-ipad-screen` set up (game's Mod Tools package for `Assembly-CSharp.dll`/
   `ModApi.dll`, this repo's `Assets/` folder opened as/inside that project).
3. **Drop in `Assets/Scripts/0Harmony.dll`** (gitignored on purpose, same copy
   you use for the other mod).
4. **Build, deploy to the game's mods folder, and test:**
   - Right-click a block in Vizzy -> does the "Export to..." popup appear?
   - Pick a target file -> does it show up in that program next time it's
     opened, positioned somewhere visible, with the right nested content?
   - Reference a global variable the target doesn't have -> does it get
     created there?
   - Check the Player log for the three `SerializerBridge`/`ContextMenuPatcher`
     best-effort spots called out in `docs/IMPLEMENTATION_NOTES.md` if
     anything misbehaves - each one says exactly what to check in dnSpy.
5. Fix whatever compilation errors or wrong API guesses show up - there will
   likely be some, since none of this has touched a real compiler against the
   actual game assemblies yet.

## Known gaps (also in README.md's "Known limitations")

- No "create a new file" option in the export target picker - target must
  already exist.
- A same-named variable collision in the target is left untouched, never
  overwritten.
- "Center" placement is a heuristic (centroid of the target's existing
  blocks, since there's no way to read a closed program's actual on-screen
  camera position) - close, not exact.

## Repo layout

See the "Project layout" section near the bottom of `README.md` - it's kept
up to date there rather than duplicated here.
