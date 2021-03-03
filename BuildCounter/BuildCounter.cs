using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BuildCounter
{

    [BepInPlugin("com.brokenmass.plugin.DSP.BuildCounter", "BuildCounter", "1.0.0")]
    public class BuildCounter : BaseUnityPlugin
    {
        Harmony harmony;

        internal static readonly string SPACING = new String(' ', 5);

        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.BuildCounter");
            try
            {
                harmony.PatchAll(typeof(BuildCounter));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            // For ScriptEngine hot-reloading
            harmony.UnpatchSelf();
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "UpdatePreviews")]
        public static void UpdatePreviews_Postfix(ref PlayerAction_Build __instance)
        {
            if (__instance.buildPreviews.Count > 0)
            {
                var counter = new Dictionary<string, int>();

                foreach (var buildPreview in __instance.buildPreviews)
                {
                    var name = buildPreview.item.name;

                    if(!counter.ContainsKey(name))
                    {
                        counter.Add(name, 0);
                    }

                    counter[name]++;
                }

                __instance.cursorText += $"\nUsing:";
                foreach (var entry in counter)
                {
                    __instance.cursorText += $"\n{SPACING}- {entry.Value} x {entry.Key}";
                }
            }

        }
    }
}
