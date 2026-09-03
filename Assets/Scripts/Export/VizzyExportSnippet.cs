using Assets.Scripts.CopyPaste.Bridge;
using Assets.Scripts.Vizzy.UI.Elements;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace Assets.Scripts.CopyPaste.Export
{
    /// <summary>Which family of Vizzy node a captured snippet holds.</summary>
    public enum VizzyNodeKind
    {
        Instruction,
        Expression
    }

    /// <summary>
    /// A block (or, for instructions, the whole column of blocks chained below it) captured
    /// from the editor, ready to be written into another program file. Nodes are kept as
    /// XElements in memory - there's no clipboard/round-trip step, since export happens in
    /// one right-click-to-file-pick motion.
    /// </summary>
    public class VizzyExportSnippet
    {
        public VizzyNodeKind Kind;
        public List<XElement> Nodes = new List<XElement>();

        /// <summary>
        /// Captures a right-clicked block. For an instruction block this walks
        /// InstructionElementScript.NextInstruction - the same UI-level chain pointer Vizzy
        /// Studio's own DisconnectBlock patch manipulates - so right-clicking any block in a
        /// column exports that block plus the rest of the column below it, not just the one
        /// block. Nested content (loop bodies, arguments, etc.) rides along automatically
        /// since it's serialized as part of each node itself.
        /// </summary>
        public static bool TryCapture(BlockElementScript block, out VizzyExportSnippet snippet, out string error)
        {
            snippet = null;
            error = null;

            if (block == null || block.Node == null)
            {
                error = "Nothing to export - right-click a block first.";
                return false;
            }

            try
            {
                List<XElement> nodes = new List<XElement>();

                if (block is InstructionElementScript headBlock)
                {
                    InstructionElementScript current = headBlock;
                    int guard = 0;
                    while (current != null && guard++ < 10000)
                    {
                        if (current.Node != null)
                        {
                            nodes.Add(SerializerBridge.SerializeNode(current.Node));
                        }

                        current = current.NextInstruction;
                    }

                    if (nodes.Count == 0)
                    {
                        error = "Couldn't read that block's data.";
                        return false;
                    }

                    snippet = new VizzyExportSnippet { Kind = VizzyNodeKind.Instruction, Nodes = nodes };
                }
                else
                {
                    nodes.Add(SerializerBridge.SerializeNode(block.Node));
                    snippet = new VizzyExportSnippet { Kind = VizzyNodeKind.Expression, Nodes = nodes };
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to read this block: {ex.Message}";
                Debug.LogError($"[Vizzy Copy Paste] {error}\n{ex}");
                return false;
            }
        }
    }
}
