# Implementation notes

This mod was written without access to a decompiled copy of the current
game's `Assembly-CSharp.dll` or a Unity install to compile/test against -
instead, it's built by reading the actual source of two published, working
tools for this same game:

- [Vizzy Studio](https://github.com/joaofarias/VizzyStudio) - a Harmony/
  ModApi mod, for how to hook the running editor.
- [kroryan/VizzyCode](https://github.com/kroryan/VizzyCode) - a bidirectional
  XML &lt;-&gt; C# DSL converter for Vizzy programs, for the exact shape of
  the saved `.xml` format itself (positions, variables, instruction/
  expression containers).

This document lists what's directly confirmed from that source versus
what's inferred, so you know what to check first if something doesn't work.

## Confirmed program file schema (from kroryan/VizzyCode)

`VizzyXmlConverter.cs` in that project is a real, working converter for this
exact file format, so its own read/write code is strong evidence for the
schema:

- **Multiple independent top-level `<Instructions>` blocks.** The program
  root holds several sibling `<Instructions>` elements
  (`program.Elements("Instructions")`, plural) - one per independent
  column/script (an event handler, a standalone chain, etc.). Each one's
  direct children are a flat, ordered list of instruction elements forming
  one connected chain (`EmitInstructions` walks a flat `IEnumerable<XElement>`
  and emits each as one line). Nested control-flow bodies (If/While/For/
  Repeat) reuse the same `<Instructions>` tag name for their own nested flat
  child list.

  **This mattered for a real bug caught while building this:** an earlier
  version of `VizzyProgramFileExporter` looked up the target's existing
  `<Instructions>` element (singular) and merged the exported chain into it
  as extra flat children. Given the confirmed multi-block structure above,
  that would have spliced the pasted blocks into whichever column already
  happened to be there instead of creating an independent new one -
  `TryExport` now always creates a brand new `<Instructions>` sibling for an
  instruction export instead.

- **A single shared `<Expressions>` container.** Unlike Instructions, there's
  exactly one `<Expressions>` element (`program.Element("Expressions")`,
  singular), holding standalone expression-family entries as flat siblings,
  each independently positioned. A lone expression export is added there
  directly, matching how the file already treats that container.

- **Position: a `pos="X,Y"` attribute, on the block's first element only.**
  Plain comma-separated integers (`TryParsePositionAttribute` just does
  `value.Split(',')` then `int.TryParse` on each half). It only ever appears
  on the first/head element of a top-level block
  (`EmitTopLevelBlockMetadata(XElement firstEl, ...)` - the layout code that
  assigns fresh positions to newly-generated blocks only ever touches that
  first element, or each individual top-level `<CustomExpression>`).
  `VizzyProgramFileExporter.SetPosition` writes this same attribute on the
  head of an exported chain (or the lone expression), so pasted content
  keeps the "connected chain" semantics above intact - only the head needs a
  position; the rest of the chain places itself relative to it.

- **A single top-level `<Variables>` container**, holding variable
  *definitions* - `<Variable name="X" number="0" />` (numbers),
  `<Variable name="X" style="false" bool="false" />` (booleans),
  `<Variable name="X"><Constant text="..."/></Variable>` (strings), or
  `<Variable name="X"><Items/></Variable>` (lists) - confirmed directly from
  `CreateVariableDeclaration`. This is a *different* shape from a variable
  *reference* used inside an instruction/expression tree, which looks like
  `<Variable list="false" local="false" variableName="X" />` (confirmed from
  `CreateVariableReference`/`EmitSetVariable`/`EmitChangeVariable`). The two
  are easy to tell apart even without this context: a definition always
  carries a `name` attribute, a reference always carries `variableName`.
  `VariableDependencyResolver` relies on exactly this distinction to find
  which global variables a snippet touches (skipping any reference marked
  `local="true"`, since those are owned by whichever instruction creates
  them - a For loop counter, a SetLocalVariable block - not by a persistent
  global list), and `VizzyProgramFileExporter.AddMissingVariables` copies
  the real definitions found in the source program into the target if it
  doesn't already define them.

- **An `id` attribute** appears on many elements, and kroryan/VizzyCode's own
  XML generator (`AssignNodeIdsRecursive`) is careful to keep a running
  counter and never reuse an id that's already present in the file it's
  writing. That's read as a signal that ids matter for *uniqueness within a
  file*, so `VizzyProgramFileExporter.StripIds` removes them from everything
  it copies into a *different* file rather than risk colliding with unrelated
  nodes there that happen to reuse the same numbers. This is a conservative
  choice, not a confirmed requirement either way - if pasted blocks come in
  fine without ids, this was unnecessary but harmless; if the game turns out
  to need every element to have one, this is the method to revisit.

## Confirmed hooks into the running editor (from Vizzy Studio)

- `BlockElementScript.Node -> ProgramNode` and `<block>.VizzyUI.FlightProgram`
  Used throughout Vizzy Studio's patches (e.g. `VariableElementScriptPatches`,
  `ReferenceManager`).
- `InstructionElementScript.NextInstruction -> InstructionElementScript`
  Confirmed via `InstructionElementScriptPatches.DisconnectBlock`, which
  reads and rewrites this same chain of pointers when a block is dragged out
  of a chain. `VizzyExportSnippet.TryCapture` walks this same pointer to
  collect the whole column below the right-clicked block.
- `VariableElementScript.OnPointerClick(PointerEventData)`
  Confirmed to exist and be patchable (`VariableElementScriptPatches`
  patches it for its rename-on-click feature). `ContextMenuPatcher` patches
  it the same way, plus tries a couple of other classes defensively (see
  below) in case a plain instruction block's click doesn't route through it.
- `Game.Instance.UserInterface.CreateMessageDialog(string)`
  Used for user-facing success/error messages. Confirmed via
  `ReferenceManager.RemoveReference`, which calls it the same way with a
  plain string.
- `VizzyUIScript.FlightProgramsFolderPath`
  The folder this mod lists program files from. Confirmed via Vizzy
  Studio's `Mod.cs`, which reads this exact static path to back up program
  files on first run.

## Best-effort / inferred

### 1. `ProgramSerializer.SerializeProgramNode(ProgramNode, XElement)` - low risk

Vizzy Studio's `ProgramSerializerPatches` patches this with a **string**
(`[HarmonyPatch("SerializeProgramNode")]`), not `nameof(...)`, which is a
strong signal it's private or internal - that's why `SerializerBridge`
reaches it through `AccessTools.Method(...)` instead of calling it directly.
Low-risk: name and both parameter types are directly confirmed from that
patch's postfix signature. **If it breaks:** check `ProgramSerializer` in
dnSpy/ILSpy and update the `AccessTools.Method(...)` call at the top of
`Assets/Scripts/Bridge/SerializerBridge.cs`.

### 2. `ProgramSerializer.SerializeFlightProgram(FlightProgram)` - low-medium risk

Used to grab the *entire* current program's XML (so `VariableDependencyResolver`
can find real variable definitions to copy). `nameof(ProgramSerializer.SerializeFlightProgram)`
appears in Vizzy Studio's patches, confirming *some* accessible member of
that name exists, but nameof doesn't check parameter types, so the exact
overload (`FlightProgram -> XElement`) is inferred from the obvious mirror
with the confirmed `DeserializeFlightProgram(XElement) -> FlightProgram`.
Reached via reflection specifically so a wrong guess here degrades
gracefully - `SerializerBridge.TrySerializeFlightProgram` returns null on
any failure, and `VizzyExportSnippet` just skips variable auto-creation for
that export rather than failing the whole thing. **If missing variables
stop being auto-created:** check the real signature in dnSpy and update
`SerializerBridge.SerializeFlightProgramMethod`.

### 3. Which class's `OnPointerClick` fires for a right-click - low risk, self-correcting

Only `VariableElementScript.OnPointerClick` is directly confirmed.
`ContextMenuPatcher.TryPatchOnPointerClick` also tries `InstructionElementScript`
and `BlockElementScript` defensively, skipping any that don't declare their
own `OnPointerClick` (instead of crashing) and skipping inherited duplicates
via a `DeclaringType` check. Worst case if none of the three fire for a
given block type: right-click silently does nothing on that block type,
rather than the mod breaking. **If right-click doesn't do anything:** check
the Player.log for `[Vizzy Copy Paste] Hooked right-click via ...` lines to
see which class(es) got patched, then check in dnSpy whether the block
you're clicking uses a different class/method, and add it to
`ContextMenuPatcher.Apply`.

## Why plain XML files instead of live block-building

The first draft of this mod used the OS clipboard and tried to rebuild
on-screen blocks directly in the running editor for paste, which needed a
guess at a private "build a block from this data" method on
`NodeBuilderScript` with no real precedent to confirm it against. Exporting
to an existing program *file* sidesteps that entirely - appending into
another file's `Instructions`/`Expressions`/`Variables` containers is just
editing data the game already knows how to load, not reconstructing live UI
state.
