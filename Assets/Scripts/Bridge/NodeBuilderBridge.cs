using Assets.Scripts.Vizzy.UI;
using Assets.Scripts.Vizzy.UI.Elements;
using HarmonyLib;
using ModApi.Craft.Program;
using ModApi.Craft.Program.Instructions;
using System;
using System.Reflection;
using System.Xml.Linq;
using UnityEngine;

namespace Assets.Scripts.CopyPaste.Bridge
{
    /// <summary>
    /// Everything in this file talks to game-internal members that are not part of the
    /// public ModApi surface, the same way Vizzy Studio's own Harmony patches do
    /// (see Assets/Scripts/Patches/*.cs in github.com/joaofarias/VizzyStudio for the
    /// reference this mod was modeled on). Because these are private/internal members,
    /// their exact names can change between game versions, and a couple of them are
    /// *educated guesses* rather than confirmed names - every guess is called out below.
    ///
    /// If something here throws or silently returns null in your game version, open the
    /// relevant Assembly-CSharp.dll in dnSpy/ILSpy (the same tool you used for the
    /// jno-ipad-screen MFD work), find the real member name, and update the string
    /// constant in the matching Try* method. Nothing else in the mod needs to change -
    /// everything else only talks to this class.
    /// </summary>
    internal static class NodeBuilderBridge
    {
        // ---- Confirmed via Vizzy Studio's own patches (HarmonyPatch("SerializeProgramNode")
        // as a *string*, not nameof - meaning it's private/internal, hence the reflection). ----
        private static readonly MethodInfo SerializeProgramNodeMethod =
            AccessTools.Method(typeof(ProgramSerializer), "SerializeProgramNode", new[] { typeof(ProgramNode), typeof(XElement) });

        /// <summary>
        /// Serializes a single node (with everything nested inside it - arguments, body
        /// instructions, etc.) into an XElement, exactly like the game does when saving a
        /// program to disk. Confirmed to exist (Vizzy Studio patches it), reached here via
        /// reflection because it isn't public.
        /// </summary>
        public static XElement SerializeNode(ProgramNode node, XElement parentElement)
        {
            if (SerializeProgramNodeMethod == null)
            {
                throw new MissingMemberException(
                    "ProgramSerializer.SerializeProgramNode(ProgramNode, XElement) wasn't found by reflection. " +
                    "The game likely renamed it - check ProgramSerializer in dnSpy and update NodeBuilderBridge.SerializeProgramNodeMethod.");
            }

            return (XElement)SerializeProgramNodeMethod.Invoke(null, new object[] { node, parentElement });
        }

        // ---- Best-effort guess: InstructionElementScript (the UI block) exposes a
        // NextInstruction property that threads the visual chain (confirmed in Vizzy
        // Studio's InstructionElementScriptPatches.DisconnectBlock). We assume the
        // underlying ProgramInstruction data model exposes the same chain under the same
        // name, since that's what the flight execution engine would walk. Verify this one
        // first if chain-copy comes out short. ----
        private static readonly string[] NextInstructionCandidates = { "NextInstruction", "Next", "NextNode" };

        /// <summary>Walks to the next instruction in a chain on the underlying data node, if any.</summary>
        public static ProgramInstruction NextInstructionOf(ProgramInstruction current)
        {
            if (current == null)
                return null;

            foreach (string candidate in NextInstructionCandidates)
            {
                PropertyInfo prop = AccessTools.Property(typeof(ProgramInstruction), candidate);
                if (prop != null && typeof(ProgramInstruction).IsAssignableFrom(prop.PropertyType))
                {
                    return prop.GetValue(current) as ProgramInstruction;
                }
            }

            Debug.LogWarning("[Vizzy Copy Paste] Could not find a NextInstruction-like property on ProgramInstruction - " +
                              "chain copy will only copy the single block you clicked. See NodeBuilderBridge.NextInstructionCandidates.");
            return null;
        }

        // ---- Best-effort: build on-screen blocks for a deserialized node tree by reusing
        // NodeBuilderScript, the same object Vizzy Studio grabs off VizzyUI to clone blocks
        // (VizzyUI.NodeBuilder.CloneBlock(...)). We don't have a confirmed method name for
        // "build blocks from an existing ProgramNode tree" (as opposed to "build a brand new
        // empty block from a toolbox definition"), so this tries a short list of plausible
        // names before giving up and reporting failure back to the caller (nothing is lost -
        // the copied XML is still sitting in the OS clipboard either way). ----
        private static readonly string[] BuildFromNodeCandidates =
        {
            "BuildBlock", "BuildBlockFromNode", "BuildBlockForNode", "BuildNode", "BuildNodeBlock", "CreateBlockForNode"
        };

        /// <summary>
        /// Attempts to construct the visual block(s) for a deserialized node and start
        /// dragging them under the mouse cursor, exactly like the built-in "clone" drag does
        /// (BlockElementScript.StartClone -> NodeBuilder.CloneBlock -> VizzyUI.DragBegin).
        /// Returns false (without throwing) if no candidate method is found, so the caller
        /// can show the user a clear message instead of the mod crashing or doing nothing.
        /// </summary>
        public static bool TryBuildAndBeginDrag(BlockElementScript anchorBlock, ProgramNode rootNode, Vector2 screenPosition, out string error)
        {
            error = null;

            // anchorBlock.VizzyUI is any block currently on screen in the editor we want to
            // paste into - it's just our way in to the shared VizzyUI/NodeBuilder for that
            // editor. It doesn't have to be the block the user right-clicked to copy.
            NodeBuilderScript nodeBuilder = anchorBlock?.VizzyUI?.NodeBuilder;
            if (nodeBuilder == null)
            {
                error = "Vizzy UI isn't available right now.";
                return false;
            }

            MethodInfo buildMethod = null;
            foreach (string candidate in BuildFromNodeCandidates)
            {
                buildMethod = AccessTools.Method(nodeBuilder.GetType(), candidate, new[] { rootNode.GetType() });
                if (buildMethod == null)
                {
                    // Also try the general ProgramNode overload in case of an exact-type mismatch.
                    buildMethod = AccessTools.Method(nodeBuilder.GetType(), candidate, new[] { typeof(ProgramNode) });
                }

                if (buildMethod != null)
                    break;
            }

            if (buildMethod == null)
            {
                error = "No known NodeBuilder method for building blocks from an existing node was found on this game version.";
                return false;
            }

            try
            {
                object builtBlock = buildMethod.Invoke(nodeBuilder, new object[] { rootNode });
                BlockElementScript blockScript = builtBlock as BlockElementScript;
                if (blockScript == null)
                {
                    error = "NodeBuilder didn't return a block we recognize.";
                    return false;
                }

                // Mirror BlockElementScriptPatches.OnStartClone: hand the freshly built block
                // to VizzyUI.DragBegin so the game's own placement/snapping/connection logic
                // takes over from here, as if the user had just started dragging it in.
                anchorBlock.VizzyUI.DragBegin(
                    new System.Collections.Generic.List<BlockElementScript> { blockScript },
                    screenPosition);

                AccessTools.Field(typeof(BlockElementScript), "_dragBeginTime")?.SetValue(blockScript, Time.unscaledTime);
                AccessTools.Field(typeof(BlockElementScript), "_dragTotalDelta")?.SetValue(blockScript, Vector2.zero);
                AccessTools.Property(typeof(BlockElementScript), "IsDragging")?.SetValue(blockScript, true);

                return true;
            }
            catch (Exception ex)
            {
                error = $"Building the pasted block failed: {ex.Message}";
                Debug.LogError($"[Vizzy Copy Paste] {error}\n{ex}");
                return false;
            }
        }
    }
}
