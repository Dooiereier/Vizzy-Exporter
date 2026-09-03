using ModApi.Craft.Program;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Assets.Scripts.CopyPaste.Bridge
{
    /// <summary>
    /// The one piece of this mod that used to reach a non-public game member. Confirmed
    /// directly against the game's own assembly (see docs/IMPLEMENTATION_NOTES.md) - the
    /// real method is public, just named/shaped differently than Vizzy Studio's source
    /// (written against an older game version) suggested.
    /// </summary>
    internal static class SerializerBridge
    {
        /// <summary>
        /// Serializes a single node - with everything nested inside it (arguments, loop
        /// bodies, etc.) - into an XElement, exactly like the game does when saving a
        /// program to disk. ProgramSerializer.SerializeProgramNodes writes its result as a
        /// child of the given parent rather than returning it, so a scratch parent is
        /// supplied and discarded after pulling the one child back out. cloneChain is false
        /// because the caller (VizzyExportSnippet) already walks the block chain itself one
        /// node at a time.
        /// </summary>
        public static XElement SerializeNode(ProgramNode node)
        {
            XElement scratchParent = new XElement("Scratch");

            // instructionId is a ref counter the game uses to hand out unique ids across a
            // serialization run. Resetting it to 0 per node (rather than threading one
            // counter across the whole captured chain) is fine here: every id gets stripped
            // right back off by VizzyProgramFileExporter.StripIds before anything is written
            // into the target file, since ids only need to be unique within that file.
            int instructionId = 0;
            ProgramSerializer.SerializeProgramNodes(node, scratchParent, ref instructionId, false);

            XElement result = scratchParent.Elements().FirstOrDefault();
            if (result == null)
            {
                throw new InvalidOperationException(
                    "ProgramSerializer.SerializeProgramNodes produced no output for this node.");
            }

            return result;
        }

        /// <summary>
        /// Serializes the *entire current program* (all instructions, expressions, and -
        /// what we actually want it for - the live, accurate list of global variable
        /// definitions) exactly as the game would when saving. Returns null rather than
        /// throwing if this isn't available, since callers use it for a nice-to-have
        /// (auto-creating missing variables), not for the core export path.
        ///
        /// IProgramSerializer.SerializeFlightProgram is an instance method, and the only
        /// implementation (ProgramSerializer) has just a private parameterless constructor
        /// and no singleton accessor anywhere in the game's API - so a throwaway instance is
        /// constructed via reflection each call. It's stateless, so this is safe.
        /// </summary>
        public static XElement TrySerializeFlightProgram(FlightProgram program)
        {
            if (program == null)
                return null;

            try
            {
                IProgramSerializer serializer =
                    (IProgramSerializer)Activator.CreateInstance(typeof(ProgramSerializer), nonPublic: true);
                return serializer.SerializeFlightProgram(program);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning(
                    $"[Vizzy Copy Paste] Couldn't serialize the current program to look up variable definitions " +
                    $"(missing global variables won't be auto-created in the target file this time): {ex.Message}");
                return null;
            }
        }
    }
}
