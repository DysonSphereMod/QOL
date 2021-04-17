using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityEngine;

namespace BetterFPS
{
    [BepInPlugin("com.brokenmass.plugin.DSP.BetterFPS", "BetterFPS", "1.0.0")]
    public class BetterFPS : BaseUnityPlugin
    {
        Harmony harmony;
        public static int MAX_THREADS = Environment.ProcessorCount - 1;

        public static ConfigEntry<bool> hideDysonSphereMesh;
        public static ConfigEntry<bool> parallelFactories;
        public static ConfigEntry<bool> disableShadows;

        internal void Start()
        {

            harmony = new Harmony("com.brokenmass.plugin.DSP.BetterFPS");
            hideDysonSphereMesh = Config.Bind<bool>("General", "hideDysonSphereMesh", false, "If true this option disable the 'expensive' rendering of the dysonspher mesh");
            parallelFactories = Config.Bind<bool>("General", "parallelFactories", false, "EXPERIMENTAL: execute factories calculation in parallel");
            disableShadows = Config.Bind<bool>("General", "disableShadows", false, "Disable shadows rendering");

            try
            {
                harmony.PatchAll(typeof(BetterFPS));

                if (disableShadows.Value)
                {
                    QualitySettings.shadows = ShadowQuality.Disable;
                }
                else
                {
                    QualitySettings.shadows = ShadowQuality.All;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(GameData), nameof(GameData.GameTick))]
        static IEnumerable<CodeInstruction> GameData_GameTick_Patch(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions)
                .MatchForward(true,
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(GameData), nameof(GameData.factoryCount)))
                )
                .MatchForward(false,
                new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(GameData), nameof(GameData.factoryCount)))
                )
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .Advance(1)
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_1))
                .SetInstructionAndAdvance(Transpilers.EmitDelegate<Action<GameData, long>>((GameData __instance, long time) =>
                {
                    if (parallelFactories.Value)
                    {
                        var factoriesQueue = new ConcurrentQueue<PlanetFactory>(__instance.factories);

                        Task[] tasks = new Task[MAX_THREADS];

                        for (int i = 0; i < MAX_THREADS; i++)
                        {
                            int taskIndex = i;
                            tasks[taskIndex] = Task.Factory.StartNew(() =>
                            {
                                try
                                {
                                    while (factoriesQueue.TryDequeue(out PlanetFactory factory))
                                    {
                                        if (factory != null)
                                        {
                                            factory.GameTick(time);
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                }
                            });
                        }
                        try
                        {
                            Task.WaitAll(tasks);
                        }
                        catch (AggregateException ae)
                        {
                            Console.WriteLine("One or more exceptions occurred: ");
                            foreach (var ex in ae.Flatten().InnerExceptions)
                                Console.WriteLine($"   {ex.Message}");
                        }
                    }
                    else
                    {
                        for (int k = 0; k < __instance.factoryCount; k++)
                        {
                            if (__instance.factories[k] != null)
                            {
                                __instance.factories[k].GameTick(time);
                            }
                        }
                    }
                }));

            return matcher.InstructionEnumeration();
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(DysonSphereSegmentRenderer), nameof(DysonSphereSegmentRenderer.DrawModels))]
        static IEnumerable<CodeInstruction> DysonSphereSegmentRenderer_DrawModels_Patch(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions)
                .MatchForward(true,
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(DysonSphereSegmentRenderer), nameof(DysonSphereSegmentRenderer.dysonSphere))),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(DysonSphere), nameof(DysonSphere.layersIdBased)))
                )
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(DysonSphereSegmentRenderer), nameof(DysonSphereSegmentRenderer.dysonSphere))),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(DysonSphere), nameof(DysonSphere.layersIdBased)))
                )
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop))
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop))
                .Advance(1)
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                .SetInstructionAndAdvance(Transpilers.EmitDelegate<Func<uint, DysonSphereSegmentRenderer, DysonSphereLayer>>((uint index, DysonSphereSegmentRenderer renderer) =>
                {
                    if (hideDysonSphereMesh.Value && DysonSphere.renderPlace == ERenderPlace.Universe)
                    {
                        return null;
                    }

                    return renderer.dysonSphere.layersIdBased[(int)((UIntPtr)index)];
                }));

            return matcher.InstructionEnumeration();
        }


        internal void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

    }
}
