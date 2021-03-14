using BepInEx;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuildUI
{
    [BepInPlugin("com.brokenmass.plugin.DSP.MultiBuildUI", "MultiBuildUI", "1.0.0")]
    public class MultiBuildUI : BaseUnityPlugin
    {
        private Harmony harmony;

        public static AssetBundle bundle;

        public static bool isValidInstallation = true;

        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.MultiBuildUI");
            string pluginfolder = Path.GetDirectoryName(Assembly.GetAssembly(typeof(MultiBuildUI)).Location);
            bundle = AssetBundle.LoadFromFile($"{pluginfolder}/blueprintsbundle");

            try
            {
                harmony.PatchAll(typeof(UIFunctionPanelPatch));
                harmony.PatchAll(typeof(UIBuildMenuPatch));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            // For ScriptEngine hot-reloading
            if (bundle != null)
            {
                bundle.Unload(true);
            }
            harmony.UnpatchSelf();
        }
    }
}
