using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityEngine;

namespace BetterFPS
{
    [BepInPlugin("com.brokenmass.plugin.DSP.BetterFPS", "BetterFPS", "1.0.1")]
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

        [HarmonyTranspiler, HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTick))]
        static IEnumerable<CodeInstruction> FactorySystem_DrawModels_Patch(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions);
            matcher
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(FactorySystem), nameof(FactorySystem.ejectorCursor)))
                );

            // replace the code logic with the same logic wrapped in a meaningful lock for thread safety and return
            matcher
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                .InsertAndAdvance(Transpilers.EmitDelegate<Action<FactorySystem>>(factorySystem =>
                {
                    if (factorySystem.factory.dysonSphere != null)
                    {
                        lock (factorySystem.factory.dysonSphere)
                        {
                            DysonSphereRelatedGameTick(factorySystem);
                        }
                    }
                    else
                    {
                        DysonSphereRelatedGameTick(factorySystem);
                    }
                }))
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                .InsertAndAdvance(Transpilers.EmitDelegate<Action<FactorySystem>>(factorySystem =>
                {
                    lock (GameMain.history)
                    {
                        TechRelatedGameTick(factorySystem);
                    }
                }))
                .InsertAndAdvance(new CodeInstruction(OpCodes.Ret));

            return matcher.InstructionEnumeration();
        }

        [HarmonyReversePatch(HarmonyReversePatchType.Original), HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTick))]
        public static void DysonSphereRelatedGameTick(FactorySystem __instance)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                CodeMatcher matcher = new CodeMatcher(instructions);

                List<CodeInstruction> instructionsList = instructions.ToList();
                List<CodeInstruction> code = new List<CodeInstruction>();

                int startIdx = 0;
                int endIdx = matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(GameHistoryData), nameof(GameHistoryData.miningCostRate)))
                ).Pos;

                if (endIdx == instructionsList.Count)
                {
                    throw new InvalidOperationException("Cannot extract the dysonsphere part of FactorySystem.GameTick because the first indicator isn't present");
                }

                for (int i = startIdx; i < endIdx; i++)
                {
                    code.Add(instructionsList[i]);
                }



                startIdx = matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(FactorySystem), nameof(FactorySystem.ejectorCursor)))
                ).Pos;
                if (startIdx == instructionsList.Count)
                {
                    throw new InvalidOperationException("Cannot extract the dysonsphere part of FactorySystem.GameTick because the second indicator isn't present");
                }

                endIdx = matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_0),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(GameHistoryData), nameof(GameHistoryData.currentTech)))
                ).Pos;
                if (endIdx == instructionsList.Count)
                {
                    throw new InvalidOperationException("Cannot extract the dysonsphere part of FactorySystem.GameTick because the third indicator isn't present");
                }

                Debug.Log($"extracted dysonsphere part of FactorySystem.GameTick from {startIdx} to {endIdx}");
                for (int i = startIdx; i < endIdx; i++)
                {
                    code.Add(instructionsList[i]);
                }

                return code.AsEnumerable();
            }

            // make compiler happy
            _ = Transpiler(null);
            return;
        }

        [HarmonyReversePatch(HarmonyReversePatchType.Original), HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.GameTick))]
        public static void TechRelatedGameTick(FactorySystem __instance)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                CodeMatcher matcher = new CodeMatcher(instructions);

                List<CodeInstruction> instructionsList = instructions.ToList();
                List<CodeInstruction> code = new List<CodeInstruction>();

                int startIdx = 0;
                int endIdx = matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_0),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(GameHistoryData), nameof(GameHistoryData.miningCostRate)))
                ).Pos;

                if (endIdx == instructionsList.Count)
                {
                    throw new InvalidOperationException("Cannot extract the teck part of FactorySystem.GameTick because the first indicator isn't present");
                }

                for (int i = startIdx; i < endIdx; i++)
                {
                    code.Add(instructionsList[i]);
                }


                startIdx = matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_0),
                    new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(GameHistoryData), nameof(GameHistoryData.currentTech)))
                ).Pos;
                if (startIdx == instructionsList.Count)
                {
                    throw new InvalidOperationException("Cannot extract the teck part of FactorySystem.GameTick because the second indicator isn't present");
                }
                endIdx = instructionsList.Count - 1;

                Debug.Log($"extracted teck part of FactorySystem.GameTick from {startIdx} to {endIdx}");
                for (int i = startIdx; i < endIdx; i++)
                {
                    code.Add(instructionsList[i]);
                }

                return code.AsEnumerable();
            }

            // make compiler happy
            _ = Transpiler(null);
            return;
        }

        internal void OnDestroy()
        {
            harmony.UnpatchSelf();
        }

    }
}
