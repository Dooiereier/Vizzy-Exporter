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
    }
}
