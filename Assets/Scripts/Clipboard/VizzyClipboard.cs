using Assets.Scripts.CopyPaste.Bridge;
using Assets.Scripts.Vizzy.UI.Elements;
using ModApi.Craft.Program;
using ModApi.Craft.Program.Instructions;
using System;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace Assets.Scripts.CopyPaste.Clipboard
{
    /// <summary>
    /// The kind of node stored in a clipboard payload. Vizzy has two families of blocks:
    /// instructions (statements, chained via NextInstruction) and expressions (values,
    /// plugged into instruction/expression argument slots). We need to know which one
    /// we deserialized into before we can hand it back to the game.
    /// </summary>
    public enum VizzyNodeKind
    {
        Instruction,
        Expression
    }

    /// <summary>
    /// A deserialized clipboard payload: the root node plus which family it belongs to.
    /// For instructions, "Root" is the head of the copied chain - walk
    /// ((ProgramInstruction)Root).NextInstruction to reach the rest of it.
    /// </summary>
    public class VizzyClipboardPayload
    {
        public VizzyNodeKind Kind;
        public ProgramNode Root;
    }

    /// <summary>
    /// Serializes/deserializes a Vizzy block (and, for instructions, its whole following
    /// chain) to and from a small self-describing XML document, and moves that document
    /// through the OS clipboard so it survives copy/paste across different parts, craft,
    /// and even game restarts (it's just text sitting in the system clipboard).
    ///
    /// This class only touches the documented ModApi serialization entry points
    /// (ProgramSerializer.SerializeProgramNode / DeserializeProgramNode /
    /// DeserializeInstructionSet) - the same ones the game itself uses to save and load
    /// .xml program files - so it doesn't need to know anything about a node's internal
    /// fields. See docs/IMPLEMENTATION_NOTES.md for what's confirmed vs. best-effort here.
    /// </summary>
    public static class VizzyClipboard
    {
        private const string RootTag = "VizzyCopyPasteClipboard";
        private const string ChainTag = "Chain";
        private const int FormatVersion = 1;

        /// <summary>
        /// Copies a block to the clipboard. For instructions this walks the NextInstruction
        /// chain starting at <paramref name="block"/> and copies the whole branch, matching
        /// what the in-game "clone chain" drag (Shift/Alt-drag) already copies - we're just
        /// serializing instead of cloning onto the canvas.
        /// </summary>
        public static bool TryCopy(BlockElementScript block, out string error)
        {
            error = null;

            if (block == null || block.Node == null)
            {
                error = "Nothing selected to copy.";
                return false;
            }

            try
            {
                XElement root = new XElement(RootTag);
                root.SetAttributeValue("version", FormatVersion);

                if (block is InstructionElementScript && block.Node is ProgramInstruction headInstruction)
                {
                    root.SetAttributeValue("kind", nameof(VizzyNodeKind.Instruction));

                    // Each serialized node is added to Chain directly, using whatever tag
                    // name the game's own serializer gives it (e.g. "SetLocalVariable") -
                    // that tag name is exactly what DeserializeInstructionSet switches on to
                    // rebuild the right type, so nothing here may wrap or rename it.
                    XElement chain = new XElement(ChainTag);
                    ProgramInstruction current = headInstruction;
                    int guard = 0;
                    while (current != null && guard++ < 10000)
                    {
                        XElement nodeXml = NodeBuilderBridge.SerializeNode(current, chain);

                        // Defensive: some versions of SerializeProgramNode attach the result to
                        // the parent element themselves, others just return it and expect the
                        // caller to. Only add it ourselves if it isn't already attached, so we
                        // can't end up with the same node listed twice under Chain.
                        if (nodeXml.Parent == null)
                            chain.Add(nodeXml);

                        // NOTE: NextInstruction is the property name observed on the *visual*
                        // InstructionElementScript in the reference mod this was built from.
                        // The underlying ProgramInstruction data model is expected to expose
                        // the same chain via a property of the same name (it's what the
                        // execution engine walks at runtime) - if your decompiler shows a
                        // different name for this version of the game, update NextInstructionOf().
                        current = NodeBuilderBridge.NextInstructionOf(current);
                    }

                    root.Add(chain);
                }
                else
                {
                    root.SetAttributeValue("kind", nameof(VizzyNodeKind.Expression));

                    // Single node, added directly under root with its own real tag name -
                    // DeserializeProgramNode reads that tag name to know what to rebuild.
                    XElement nodeXml = NodeBuilderBridge.SerializeNode(block.Node, root);
                    if (nodeXml.Parent == null)
                        root.Add(nodeXml);
                }

                string xml = root.ToString(SaveOptions.DisableFormatting);
                GUIUtility.systemCopyBuffer = xml;

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to copy block: {ex.Message}";
                Debug.LogError($"[Vizzy Copy Paste] {error}\n{ex}");
                return false;
            }
        }

        /// <summary>
        /// Reads the OS clipboard and, if it holds a payload this mod wrote, deserializes it
        /// back into live ProgramNode objects via the game's own deserializer.
        /// </summary>
        public static bool TryReadClipboard(out VizzyClipboardPayload payload, out string error)
        {
            payload = null;
            error = null;

            string text = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(text))
            {
                error = "Clipboard is empty.";
                return false;
            }

            XElement root;
            try
            {
                root = XElement.Parse(text);
            }
            catch
            {
                error = "Clipboard doesn't contain a Vizzy Copy Paste snippet.";
                return false;
            }

            if (root.Name != RootTag)
            {
                error = "Clipboard doesn't contain a Vizzy Copy Paste snippet.";
                return false;
            }

            string kindAttr = root.Attribute("kind")?.Value;
            if (!Enum.TryParse(kindAttr, out VizzyNodeKind kind))
            {
                error = "Unrecognized clipboard payload.";
                return false;
            }

            try
            {
                if (kind == VizzyNodeKind.Instruction)
                {
                    XElement chain = root.Element(ChainTag);
                    if (chain == null || !chain.Elements().Any())
                    {
                        error = "Clipboard snippet has no instructions in it.";
                        return false;
                    }

                    // DeserializeInstructionSet takes the *container* element that wraps a
                    // chain of instructions and returns the head instruction, exactly what
                    // the game calls when loading a saved program's root instruction list.
                    ProgramInstruction head = ProgramSerializer.DeserializeInstructionSet(chain) as ProgramInstruction;
                    if (head == null)
                    {
                        error = "Could not rebuild the copied instructions.";
                        return false;
                    }

                    payload = new VizzyClipboardPayload { Kind = VizzyNodeKind.Instruction, Root = head };
                    return true;
                }
                else
                {
                    XElement nodeXml = root.Elements().FirstOrDefault();
                    if (nodeXml == null)
                    {
                        error = "Clipboard snippet is empty.";
                        return false;
                    }

                    ProgramNode node = ProgramSerializer.DeserializeProgramNode(nodeXml);
                    if (node == null)
                    {
                        error = "Could not rebuild the copied block.";
                        return false;
                    }

                    payload = new VizzyClipboardPayload { Kind = VizzyNodeKind.Expression, Root = node };
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = $"Failed to parse clipboard: {ex.Message}";
                Debug.LogError($"[Vizzy Copy Paste] {error}\n{ex}");
                return false;
            }
        }

        /// <summary>Human-readable clipboard status for menu labels (e.g. "Paste" vs "Paste (empty)").</summary>
        public static bool ClipboardHasVizzyPayload()
        {
            string text = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(text))
                return false;

            // Cheap check without a full parse - good enough for enabling/disabling a menu item.
            return text.TrimStart().StartsWith("<" + RootTag);
        }
    }
}
