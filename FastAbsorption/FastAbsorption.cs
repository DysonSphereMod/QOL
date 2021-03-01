using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FastAbsorption
{
    [BepInPlugin("com.brokenmass.plugin.DSP.FastAbsorption", "FastAbsorption", "1.0.0")]
    public class FastAbsorption : BaseUnityPlugin
    {
        Harmony harmony;

        public static ConfigEntry<int> frequencyMultiplier;
        public static ConfigEntry<int> travelSpeedMultiplier;

        void Start()
        {
            frequencyMultiplier = Config.Bind<int>("General", "frequencyMultiplier", 10, "How much more frequently should sail be requested by every DysonSphere node [Value must be between 1 (no effect - one sail every 2 seconds) and 120 (one sail every frame)]");
            travelSpeedMultiplier = Config.Bind<int>("General", "travelSpeedMultiplier", 1, "How much faster do sails take to travel to the requesting node. Values greater than 1 make the sail teleport to the proximity of the target node. [Value must be between 1 (no effect - 2minutes travel time) and 120 (2 second travel time)]");

            frequencyMultiplier.Value = Math.Min(Math.Max(frequencyMultiplier.Value, 1), 120); // clamping value between 1 and 120
            travelSpeedMultiplier.Value = Math.Min(Math.Max(travelSpeedMultiplier.Value, 1), 120); // clamping value between 1 and 120


            harmony = new Harmony("com.brokenmass.plugin.DSP.FastAbsorption");
            try
            {
                harmony.PatchAll(typeof(DysonSphereLayer_GameTick_Patch));
                harmony.PatchAll(typeof(DysonSwarm_AbsorbSail_Patch));

                Debug.Log($"[FastAbsorption Mod] frequencyMultiplier : {frequencyMultiplier.Value}x | travelSpeedMultiplier : {travelSpeedMultiplier.Value}x ");
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

        [HarmonyPatch(typeof(DysonSphereLayer), "GameTick")]
        public static class DysonSphereLayer_GameTick_Patch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);

                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].LoadsConstant(120L))
                    {
                        code[i].operand = (int)(120 / frequencyMultiplier.Value);
                    }
                }

                return code.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(DysonSwarm), "AbsorbSail")]
        public static class DysonSwarm_AbsorbSail_Patch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);

                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].LoadsConstant(14400L))
                    {
                        code[i].operand = (int)(14400L / travelSpeedMultiplier.Value);
                        break;
                    }
                }

                return code.AsEnumerable();
            }
        }
    }
}