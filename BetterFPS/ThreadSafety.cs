using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.BetterFPS
{

    class ThreadSafety
    {
        public static bool executeNow = true;
        private static List<Tuple<int, int>> unlockedTechs = new List<Tuple<int, int>>();
        private static List<StorageComponent> changedStorages = new List<StorageComponent>();
        private static List<Tuple<int, int, bool>> removedModels = new List<Tuple<int, int, bool>>();
        private static List<CargoContainer> expandedCargos = new List<CargoContainer>();
        private static List<DysonSwarm> expandedDysonSwarmBullets = new List<DysonSwarm>();
        private static List<Tuple<DysonSwarm, DysonSail, int, long>> addedDysonSwarmSails = new List<Tuple<DysonSwarm, DysonSail, int, long>>();

        [HarmonyPrefix, HarmonyPatch(typeof(GameHistoryData), nameof(GameHistoryData.NotifyTechUnlock))]
        public static bool GameHistoryData_NotifyTechUnlock_Prefix(int _techId, int _level)
        {
            if (executeNow)
            {
                return true;
            }

            unlockedTechs.Add(Tuple.Create(_techId, _level));
            return false;

        }

        [HarmonyPrefix, HarmonyPatch(typeof(StorageComponent), nameof(StorageComponent.NotifyStorageChange))]
        public static bool StorageComponent_NotifyStorageChange_Prefix(StorageComponent __instance)
        {
            if (executeNow)
            {
                return true;
            }

            changedStorages.Add(__instance);
            return false;

        }

        [HarmonyPrefix, HarmonyPatch(typeof(GPUInstancingManager), nameof(GPUInstancingManager.RemoveModel))]
        public static bool GPUInstancingManager_RemoveModel_Prefix(StorageComponent __instance, int modelIndex, int modelId, bool setBuffer = true)
        {
            if (executeNow)
            {
                return true;
            }

            removedModels.Add(Tuple.Create(modelIndex, modelId, setBuffer));
            return false;

        }

        [HarmonyTranspiler, HarmonyPatch(typeof(CargoContainer), nameof(CargoContainer.Expand2x))]
        public static IEnumerable<CodeInstruction> CargoContainer_Expand2x_Patch(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions)
               .MatchForward(true,
                   new CodeMatch(OpCodes.Ldarg_0),
                   new CodeMatch(OpCodes.Ldloc_0),
                   new CodeMatch(OpCodes.Stfld, AccessTools.Field(typeof(CargoContainer), nameof(CargoContainer.poolCapacity)))
               )
               .Advance(1)
               .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
               .InsertAndAdvance(Transpilers.EmitDelegate<Action<CargoContainer>>((CargoContainer __instance) =>
               {
                   if (executeNow)
                   {
                       UpdateCargoBuffer(__instance);
                   }
                   else
                   {
                       expandedCargos.Add(__instance);
                   }
               }))
               .InsertAndAdvance(new CodeInstruction(OpCodes.Ret));

            return matcher.InstructionEnumeration();
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(DysonSwarm), nameof(DysonSwarm.SetBulletCapacity))]
        public static IEnumerable<CodeInstruction> DysonSwarm_SetBulletCapacity_Patch(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions)
               .MatchForward(false,
                   new CodeMatch(OpCodes.Ldarg_0),
                   new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(DysonSwarm), nameof(DysonSwarm.bulletBuffer)))
               )
               .Advance(1)
               .InsertAndAdvance(Transpilers.EmitDelegate<Action<DysonSwarm>>((DysonSwarm __instance) =>
               {
                   if (executeNow)
                   {
                       UpdateBulletBuffer(__instance);
                   }
                   else
                   {
                       expandedDysonSwarmBullets.Add(__instance);
                   }
               }))
               .InsertAndAdvance(new CodeInstruction(OpCodes.Ret));

            return matcher.InstructionEnumeration();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(DysonSwarm), nameof(DysonSwarm.AddSolarSail))]
        public static bool DysonSwarm_AddSolarSail_Prefix(DysonSwarm __instance, DysonSail ss, int orbitId, long expiryTime)
        {
            if (executeNow || __instance.sailRecycleCursor > 0 || __instance.sailCursor < __instance.sailCapacity - 1)
            {
                // use default method if notifyNow is true or if the operation doesn't require a resize
                return true;
            }

            addedDysonSwarmSails.Add(Tuple.Create(__instance, ss, orbitId, expiryTime));
            return false;
        }

        public static void UpdateCargoBuffer(CargoContainer __instance)
        {
            __instance.computeBuffer.Release();
            __instance.computeBuffer = new ComputeBuffer(__instance.poolCapacity, 32, ComputeBufferType.Default);
        }

        public static void UpdateBulletBuffer(DysonSwarm __instance)
        {
            if (__instance.bulletBuffer != null)
            {
                __instance.bulletBuffer.Release();
            }
            __instance.bulletBuffer = new ComputeBuffer(__instance.bulletCapacity, 112, ComputeBufferType.Default);
        }

        public static void NotifyHistory()
        {
            foreach (var unlockedTech in unlockedTechs)
            {
                GameMain.history.NotifyTechUnlock(unlockedTech.Item1, unlockedTech.Item2);
            }

            unlockedTechs.Clear();
        }
        public static void NotifyStorages()
        {
            foreach (var item in changedStorages)
            {
                item.NotifyStorageChange();
            }

            changedStorages.Clear();
        }
        public static void RemoveModels()
        {
            foreach (var removedModel in removedModels)
            {
                GameMain.gpuiManager.RemoveModel(removedModel.Item1, removedModel.Item2, removedModel.Item3);
            }

            removedModels.Clear();
        }
        public static void ExpandCargos()
        {
            foreach (var item in expandedCargos)
            {
                UpdateCargoBuffer(item);
            }

            expandedCargos.Clear();
        }
        public static void ExpandDysonSwarmBullets()
        {
            foreach (var item in expandedDysonSwarmBullets)
            {
                UpdateBulletBuffer(item);
            }

            expandedDysonSwarmBullets.Clear();
        }

        public static void AddSolarSails()
        {
            foreach (var addedDysonSwarmSail in addedDysonSwarmSails)
            {
                addedDysonSwarmSail.Item1.AddSolarSail(addedDysonSwarmSail.Item2, addedDysonSwarmSail.Item3, addedDysonSwarmSail.Item4);
            }

            addedDysonSwarmSails.Clear();
        }

        public static void LateNotify()
        {
            executeNow = true;
            NotifyHistory();
            NotifyStorages();
            RemoveModels();
            ExpandCargos();
            ExpandDysonSwarmBullets();
            AddSolarSails();
        }
    }
}
