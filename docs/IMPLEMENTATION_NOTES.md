# Implementation notes

This mod was written without access to a decompiled copy of the current
game's `Assembly-CSharp.dll` or a Unity install to compile/test against -
instead, it's built by reading the actual source of
[Vizzy Studio](https://github.com/joaofarias/VizzyStudio), a published,
working mod for this same game, and matching its patterns exactly wherever
possible. This document lists what's directly confirmed from that source
versus what's inferred, so you know what to check first if something
doesn't work.

The file-export architecture (right-click -> serialize -> write into an
existing program file on disk) was chosen specifically because it needs far
less of this: it never has to rebuild live on-screen blocks, so it avoids
the riskiest, least-confirmed part of the earlier clipboard/live-paste
design this mod started from.

## Confirmed (used directly, no reflection needed)

These are called exactly as Vizzy Studio's own code calls them, which means
they're accessible members Vizzy Studio's mod assembly compiles against
directly:

- `BlockElementScript.Node -> ProgramNode`
  Used throughout Vizzy Studio's patches (e.g. `VariableElementScriptPatches`).
- `InstructionElementScript.NextInstruction -> InstructionElementScript`
  Confirmed via `InstructionElementScriptPatches.DisconnectBlock`, which
  reads and rewrites this same chain of pointers
  (`NextInstruction`/`PrevInstruction`/`ParentInstruction`/`ChildInstruction`)
  when a block is dragged out of a chain. `VizzyExportSnippet.TryCapture`
  walks this same pointer to collect the whole column below the
  right-clicked block.
- `VariableElementScript.OnPointerClick(PointerEventData)`
  Confirmed to exist and be patchable (`VariableElementScriptPatches`
  patches it for its rename-on-click feature). `ContextMenuPatcher` patches
  it the same way, plus tries a couple of other classes defensively (see
  below) in case a plain instruction block's click doesn't route through it.
- `Game.Instance.UserInterface.CreateMessageDialog(string)`
  Used for user-facing success/error messages. Confirmed via
  `ReferenceManager.RemoveReference` in Vizzy Studio, which calls it the
  same way with a plain string.
- `VizzyUIScript.FlightProgramsFolderPath`
  The folder this mod lists program files from. Confirmed via Vizzy
  Studio's `Mod.cs`, which reads this exact static path to back up program
  files on first run.
- Root-level node containers are named `Instructions` and `Expressions`.
  Confirmed via `ProgramSerializerPatches.OnDeserializeNode`, which checks
  `nodeElement.Parent.Name == "Instructions" || ... == "Expressions"`.
  `VizzyProgramFileExporter.TryExport` adds exported nodes into whichever of
  these containers matches the snippet's kind, creating it if the target
  file doesn't have one yet.

## Best-effort / inferred

### 1. `ProgramSerializer.SerializeProgramNode(ProgramNode, XElement)` - low risk

Vizzy Studio's `ProgramSerializerPatches` patches this with a **string**
(`[HarmonyPatch("SerializeProgramNode")]`), not `nameof(...)`, which is a
strong signal it's private or internal rather than public - that's why
`SerializerBridge.SerializeNode` reaches it through
`AccessTools.Method(typeof(ProgramSerializer), "SerializeProgramNode", ...)`
instead of calling it directly.

This is low-risk: the method's existence, name, and both parameter types
are directly confirmed from that patch's postfix signature
(`OnSerializedFlightProgram(ProgramNode node, XElement parentElement, XElement __result)`).
The only unconfirmed detail is whether it also attaches its result to
`parentElement` internally - `SerializerBridge` doesn't need to know either
way, since it hands the returned `XElement` straight to
`VizzyProgramFileExporter`, which always deep-copies each node
(`new XElement(node)`) before attaching it to the target file, regardless of
whether it already had a parent.

**If it breaks:** open `ProgramSerializer` in dnSpy/ILSpy, confirm the
method name and parameter order still match, and update the
`AccessTools.Method(...)` call at the top of
`Assets/Scripts/Bridge/SerializerBridge.cs`.

### 2. Which class's `OnPointerClick` fires for a right-click - low risk, self-correcting

Only `VariableElementScript.OnPointerClick` is directly confirmed. It's
plausible that `InstructionElementScript` or `BlockElementScript` declare
their own `OnPointerClick` too (for clicking/selecting a plain instruction
block), but that's not confirmed the same way.

This is handled defensively on purpose:
`ContextMenuPatcher.TryPatchOnPointerClick` tries all three classes, skips
any that don't declare their own `OnPointerClick` (instead of crashing), and
skips inherited duplicates via a `DeclaringType` check so the same method
never gets patched twice. Worst case if none of the three fire for a given
block type: right-click silently does nothing on that block, rather than
the mod breaking.

**If right-click doesn't do anything:** check the Player.log for
`[Vizzy Copy Paste] Hooked right-click via ...` lines to see which
class(es) actually got patched, then check in dnSpy whether the block
you're clicking uses a different class or method for pointer clicks, and
add it to `ContextMenuPatcher.Apply`.

## Why plain XML files instead of live block-building

The first draft of this mod used the OS clipboard and tried to rebuild
on-screen blocks directly in the running editor for paste, which needed a
guess at a private "build a block from this data" method on
`NodeBuilderScript` with no real precedent in Vizzy Studio's source to
confirm it against. Exporting to an existing program *file* sidesteps that
entirely - `ProgramSerializer` already reads and writes this exact XML
format for saved `.xml` program files, so appending a serialized node into
another file's `Instructions`/`Expressions` container is just editing data
the game already knows how to load, not reconstructing live UI state.
