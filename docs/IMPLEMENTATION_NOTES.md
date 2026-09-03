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

## Serialization API - corrected against the real game assembly

Vizzy Studio's source (what items 1-2 below were originally inferred from)
was written against an older game version. Once this mod actually got built
against the current `SimpleRockets2.dll`/`ModApi.dll` (via reflection dumps
run from an editor menu item, see git history around this note for the
throwaway `SerializerDiagnostics.cs` used to find these), both guesses
turned out to be wrong in different ways. Corrected in `SerializerBridge.cs`:

- **`SerializeProgramNode(ProgramNode, XElement)` doesn't exist.** The real
  method is `ModApi.Craft.Program.ProgramSerializer.SerializeProgramNodes`
  (plural) - **public static**, so no reflection is needed at all - with
  signature `(ProgramNode node, XElement parentElement, ref int
  instructionId, bool cloneChain)`. It writes its result as a new child of
  `parentElement` rather than returning it, and `instructionId` is a `ref`
  counter for handing out unique ids across a run (safe to reset to 0 per
  call here, since `VizzyProgramFileExporter.StripIds` strips every id
  before writing into the target file anyway). `cloneChain` is passed
  `false` since `VizzyExportSnippet` already walks the block chain itself
  one node at a time.

- **`ProgramSerializer.SerializeFlightProgram(FlightProgram)` isn't a
  static method on `ProgramSerializer`.** It's an **instance** method
  declared on the interface `ModApi.Craft.Program.IProgramSerializer`,
  which `ProgramSerializer` implements. There's no singleton/service-locator
  accessor for it anywhere in the game's API, and `ProgramSerializer`'s only
  constructor is `private ProgramSerializer()` - so `SerializerBridge`
  constructs a throwaway instance via
  `Activator.CreateInstance(typeof(ProgramSerializer), nonPublic: true)`
  and calls the interface method on it directly. It's stateless, so a new
  instance per call is fine.

**If either of these breaks again after a game update:** the fastest way to
re-diagnose is the same one that found these - a temporary `[MenuItem]` in
the Unity Editor that reflects over `System.AppDomain.CurrentDomain.GetAssemblies()`
looking for methods by parameter/return shape rather than by name (renamed
methods won't show up in a name search, but their signature usually doesn't
change as much).

### Which class's `OnPointerClick` fires for a right-click - low risk, self-correcting

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
