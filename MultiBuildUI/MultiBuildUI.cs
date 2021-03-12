using BepInEx;
using HarmonyLib;
using System;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuildUI
{
    [BepInPlugin("com.brokenmass.plugin.DSP.MultiBuildUI", "MultiBuildUI", "1.0.0")]
    public class MultiBuildUI : BaseUnityPlugin
    {
        private Harmony harmony;

        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.MultiBuildUI");

            try
            {
                harmony.PatchAll(typeof(MultiBuildUI));
                harmony.PatchAll(typeof(UIFunctionPanelPatch));
                harmony.PatchAll(typeof(UIBuildMenuPatch));

                Registry.Init("blueprintsbundle", "blueprints", true, false);

                Debug.Log(Registry.bundle);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            // For ScriptEngine hot-reloading
            Registry.bundle.Unload(true);
            harmony.UnpatchSelf();
        }
    }
}
