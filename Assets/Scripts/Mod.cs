using Assets.Scripts.CopyPaste.Patches;
using Assets.Scripts.CopyPaste.UI;
using HarmonyLib;
using ModApi.Mods;

namespace Assets.Scripts.CopyPaste
{
    /// <summary>
    /// Mod entry point. Structured the same way as Vizzy Studio's Mod.cs (a GameMod
    /// singleton that patches with Harmony on init) since that's a working, published
    /// reference for this game's mod loader.
    /// </summary>
    public class Mod : GameMod
    {
        private Mod() : base()
        {
        }

        public static Mod Instance { get; } = GetModInstance<Mod>();

        protected override void OnModInitialized()
        {
            base.OnModInitialized();

            Harmony harmony = new Harmony("Vizzy Copy Paste");
            harmony.PatchAll();

            ContextMenuPatcher.Apply(harmony);
            BlockContextMenu.EnsureCreated();
        }
    }
}
