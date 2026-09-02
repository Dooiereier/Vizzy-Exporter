using Assets.Scripts.CopyPaste.UI;
using Assets.Scripts.Vizzy.UI.Elements;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.CopyPaste.Patches
{
    /// <summary>
    /// Hooks right-clicks on Vizzy blocks so we can pop the Copy/Paste menu.
    ///
    /// This patches manually at runtime instead of via [HarmonyPatch] attributes, because
    /// we're not 100% sure which class actually declares OnPointerClick for a plain
    /// instruction/base block - only that VariableElementScript does (Vizzy Studio's own
    /// VariableElementScriptPatches.cs patches it, for its rename-on-click feature). An
    /// attribute-based patch throws at startup if its target method doesn't exist; doing
    /// it in code lets us try several candidate classes and just skip the ones that don't
    /// declare their own OnPointerClick, so a wrong guess can't crash the whole mod.
    /// </summary>
    internal static class ContextMenuPatcher
    {
        public static void Apply(Harmony harmony)
        {
            // Confirmed: Vizzy Studio patches this exact method on this exact class.
            TryPatchOnPointerClick(harmony, typeof(VariableElementScript));

            // Best-effort guesses for where a right-click on a statement/base block lands.
            // Whichever of these actually declares its own OnPointerClick gets hooked; the
            // DeclaringType check in TryPatchOnPointerClick skips inherited duplicates so we
            // never end up patching the same underlying method twice.
            TryPatchOnPointerClick(harmony, typeof(InstructionElementScript));
            TryPatchOnPointerClick(harmony, typeof(BlockElementScript));
        }

        private static void TryPatchOnPointerClick(Harmony harmony, Type targetType)
        {
            try
            {
                MethodInfo method = AccessTools.Method(targetType, "OnPointerClick", new[] { typeof(PointerEventData) });
                if (method == null)
                {
                    Debug.Log($"[Vizzy Copy Paste] {targetType.Name} has no OnPointerClick - skipping.");
                    return;
                }

                if (method.DeclaringType != targetType)
                {
                    // Inherited from a type we've already tried (or will try) - patching it
                    // again here would fire our postfix twice per click.
                    return;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(ContextMenuPatcher), nameof(OnPointerClickPostfix)));
                Debug.Log($"[Vizzy Copy Paste] Hooked right-click via {targetType.Name}.OnPointerClick.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Vizzy Copy Paste] Couldn't hook right-click on {targetType.Name}: {ex.Message}");
            }
        }

        private static void OnPointerClickPostfix(object __instance, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right)
                return;

            if (__instance is BlockElementScript block)
            {
                BlockContextMenu.ShowFor(block, eventData.position);
            }
        }
    }
}
