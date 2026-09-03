using HarmonyLib;
using ModApi.Craft.Program;
using System;
using System.Reflection;
using System.Xml.Linq;

namespace Assets.Scripts.CopyPaste.Bridge
{
    /// <summary>
    /// The one piece of this mod that reaches a non-public game member. Everything else
    /// only talks to confirmed, directly-callable API (see docs/IMPLEMENTATION_NOTES.md
    /// for exactly what's confirmed vs. not, and why this is low-risk).
    /// </summary>
    internal static class SerializerBridge
    {
        // Vizzy Studio's own ProgramSerializerPatches.cs patches this exact method with
        // [HarmonyPatch("SerializeProgramNode")] - a *string*, not nameof(...), which is
        // the tell that it's private/internal (nameof would otherwise be used, and would
        // fail to compile against an inaccessible member). Its name and parameter list
        // are confirmed straight from that patch's postfix signature
        // (ProgramNode node, XElement parentElement, XElement __result).
        private static readonly MethodInfo SerializeProgramNodeMethod =
            AccessTools.Method(typeof(ProgramSerializer), "SerializeProgramNode", new[] { typeof(ProgramNode), typeof(XElement) });

        /// <summary>
        /// Serializes a single node - with everything nested inside it (arguments, loop
        /// bodies, etc.) - into an XElement, exactly like the game does when saving a
        /// program to disk. A throwaway parent element is always supplied (never null),
        /// since we don't know whether the real method reads/writes through it internally;
        /// the parent itself is discarded by the caller either way.
        /// </summary>
        public static XElement SerializeNode(ProgramNode node)
        {
            if (SerializeProgramNodeMethod == null)
            {
                throw new MissingMemberException(
                    "ProgramSerializer.SerializeProgramNode(ProgramNode, XElement) wasn't found by reflection. " +
                    "The game likely renamed or removed it - check ProgramSerializer in dnSpy/ILSpy and update " +
                    "SerializerBridge.SerializeProgramNodeMethod.");
            }

            XElement scratchParent = new XElement("Scratch");
            object result = SerializeProgramNodeMethod.Invoke(null, new object[] { node, scratchParent });
            return (XElement)result;
        }

        // Confirmed accessible (nameof(ProgramSerializer.SerializeFlightProgram) is used in
        // Vizzy Studio's ProgramSerializerPatches), but that only proves *some* accessible
        // overload named this exists - not its exact parameter type, since nameof doesn't
        // check parameters. Reached via reflection anyway so a wrong guess here degrades
        // gracefully (variable auto-creation just gets skipped) instead of failing to
        // compile the whole mod.
        private static readonly MethodInfo SerializeFlightProgramMethod =
            AccessTools.Method(typeof(ProgramSerializer), "SerializeFlightProgram", new[] { typeof(FlightProgram) });

        /// <summary>
        /// Serializes the *entire current program* (all instructions, expressions, and -
        /// what we actually want it for - the live, accurate list of global variable
        /// definitions) exactly as the game would when saving. Returns null rather than
        /// throwing if this isn't available, since callers use it for a nice-to-have
        /// (auto-creating missing variables), not for the core export path.
        /// </summary>
        public static XElement TrySerializeFlightProgram(FlightProgram program)
        {
            if (SerializeFlightProgramMethod == null || program == null)
                return null;

            try
            {
                return (XElement)SerializeFlightProgramMethod.Invoke(null, new object[] { program });
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
