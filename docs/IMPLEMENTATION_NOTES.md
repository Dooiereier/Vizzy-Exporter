# Implementation notes

This mod was written without access to a decompiled copy of the current
game's `Assembly-CSharp.dll` or a Unity install to compile/test against -
instead, it's built by reading the actual source of
[Vizzy Studio](https://github.com/joaofarias/VizzyStudio), a published,
working mod for this same game, and matching its patterns exactly wherever
possible. This document lists, method by method, what's directly confirmed
from that source versus what's an educated guess, so you know exactly what
to check first if something doesn't work, and where to fix it.

Everything guess-related lives in one file: `Assets/Scripts/Bridge/NodeBuilderBridge.cs`.
Nothing else in the mod needs to change if a guess turns out wrong.

## Confirmed (used directly, no reflection needed)

These are called exactly as Vizzy Studio's own code calls them, which means
they're public members Vizzy Studio's mod assembly compiles against directly:

- `ProgramSerializer.DeserializeInstructionSet(XElement containerElement) -> ProgramInstruction`
  Used in `VizzyClipboard.TryReadClipboard` to rebuild a pasted instruction
  chain. Confirmed via `ReferenceManager.LoadReferenceFromFile` in Vizzy
  Studio, which calls it the same way, and via
  `ProgramSerializerPatches`, which patches it with `nameof(...)` (only
  possible if it's accessible).
- `ProgramSerializer.DeserializeProgramNode(XElement nodeElement) -> ProgramNode`
  Used the same way, for single expression nodes. Confirmed the same way,
  plus a direct usage example in `ReferenceManager.LoadReferenceFromFile`:
  `ProgramSerializer.DeserializeProgramNode(customExpressionElement.Parent) as CustomExpression`.
- `BlockElementScript.Node -> ProgramNode` and `BlockElementScript.VizzyUI`
  Used throughout Vizzy Studio's patches (e.g.
  `VariableElementScriptPatches`, `BlockElementScriptPatches`).
- `<VizzyUI>.NodeBuilder`, `<VizzyUI>.DragBegin(List<BlockElementScript>, Vector2)`
  Confirmed via `BlockElementScriptPatches.OnStartClone`, which is exactly
  the "start dragging a freshly built clone" sequence this mod's Paste
  mirrors for a freshly *deserialized* block instead of a cloned one.
- `NodeBuilderScript.CloneBlock(BlockElementScript, bool cloneChain)`
  Not currently called by this mod, but confirms `NodeBuilderScript` is the
  right object to look for a "build blocks" method on.

## Best-effort guesses (in `NodeBuilderBridge.cs`)

### 1. `ProgramSerializer.SerializeProgramNode(ProgramNode, XElement)` - low risk

Vizzy Studio's `ProgramSerializerPatches` patches this with a **string**
(`[HarmonyPatch("SerializeProgramNode")]`), not `nameof(...)`, which is a
strong signal it's private or internal rather than public. That's why
`NodeBuilderBridge.SerializeNode` reaches it through
`AccessTools.Method(typeof(ProgramSerializer), "SerializeProgramNode", ...)`
instead of calling it directly.

This one is low-risk: the method's existence, name, and both parameter types
are directly confirmed from the patch signature
(`OnSerializedFlightProgram(ProgramNode node, XElement parentElement, XElement __result)`).
The only thing not confirmed is its exact accessibility, which doesn't matter
since we reach it by reflection either way.

**If it breaks:** open `ProgramSerializer` in dnSpy, confirm the method name
and parameter order still match, and update the `AccessTools.Method(...)`
call at the top of `NodeBuilderBridge.cs`.

### 2. The "next instruction" chain property - medium risk

`InstructionElementScriptPatches.DisconnectBlock` in Vizzy Studio proves that
the *visual* block class exposes `NextInstruction` (and `PrevInstruction`,
`ParentInstruction`, `ChildInstruction`) to thread blocks together on screen.
This mod needs the equivalent on the underlying **data** class
(`ProgramInstruction`, from `ModApi.Craft.Program.Instructions`) so that
copying a block can walk forward through the chain it heads.

There's no direct evidence in Vizzy Studio's source for the data-model
property's name - it's inferred from how instruction execution almost
certainly works (a `ProgramInstruction.Execute()` returning/advancing to
"the next one to run"). `NodeBuilderBridge.NextInstructionOf` tries
`NextInstruction`, then `Next`, then `NextNode`, and logs a warning if none
of them exist.

**Symptom if wrong:** Copy only grabs the single block you right-clicked,
never anything chained after it (nested children - loop bodies, arguments -
still copy fine either way, since those are serialized as part of the node
itself, not via this chain property).

**If it breaks:** open `ProgramInstruction` in dnSpy, find the real property
name, add it to the `NextInstructionCandidates` array in
`NodeBuilderBridge.cs` (or replace the guesses with it).

### 3. Building on-screen blocks from a deserialized node - highest risk

This is the one piece of the puzzle with no direct precedent in Vizzy
Studio's source. We know `NodeBuilderScript` is the class responsible for
turning data (`ProgramNode`) into on-screen blocks (`BlockElementScript`),
because:

- `VizzyUI.NodeBuilder.CloneBlock(block, cloneChain)` clones an *existing
  on-screen* block into a new one (confirmed).
- `NodeBuilderScript.BuildChildrenBlocks` is patched with a transpiler in
  Vizzy Studio (`NodeBuilderScriptPatches`) to special-case one node type
  while building a node's child blocks - which tells us this class also
  builds blocks from node *data*, not just by cloning what's already on
  screen. That's exactly the capability Paste needs, just for a whole
  deserialized tree instead of one child.

What isn't confirmed is the exact method name/signature for "take a
`ProgramNode` I got from deserializing XML, with no corresponding on-screen
block yet, and build the whole visual block tree for it." `NodeBuilderBridge.TryBuildAndBeginDrag`
tries a short list of plausible names
(`BuildBlock`, `BuildBlockFromNode`, `BuildBlockForNode`, `BuildNode`,
`BuildNodeBlock`, `CreateBlockForNode`) against the node's exact type and
against the general `ProgramNode` type, and gives up cleanly (shows the user
a message, doesn't crash, doesn't lose the clipboard contents) if none of
them exist.

**If it breaks:** this is the one worth actually opening a decompiler for.
In `NodeBuilderScript`, look for whatever `BuildChildrenBlocks` calls
internally to build each child's block from its `ProgramNode` - that's
almost certainly the method Paste needs. Add its real name to
`BuildFromNodeCandidates`, and double check its parameter list matches what
`TryBuildAndBeginDrag` passes (currently just the node itself - if the real
method also wants a parent transform/module/position, `TryBuildAndBeginDrag`
will need a small update to supply it).

## Why the clipboard is plain XML text

`ProgramSerializer` already reads and writes exactly this format for saved
`.xml` program files - it's the game's own native representation for a
Vizzy block tree, not something invented for this mod. Piggybacking on it
means:

- Copy/paste works across parts, craft, and game restarts for free (it's
  just what's sitting in `GUIUtility.systemCopyBuffer`, the real OS
  clipboard).
- The only two things this mod needs from the game to round-trip a snippet
  are the same serializer entry points already used to save/load programs to
  disk - nothing about individual block types needs to be known or
  special-cased.
- You can eyeball or even hand-edit a copied snippet in a text editor if
  something looks wrong, since it's just readable XML.
