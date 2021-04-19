using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.BetterFPS
{
    class ThreadSafety
    {
        public static bool notifyNow = false;
        private static List<int[]> unlockedTechs = new List<int[]>();
        private static List<StorageComponent> changedStorages = new List<StorageComponent>();
        private static List<CargoContainer> expandedCargos = new List<CargoContainer>();

        [HarmonyPrefix, HarmonyPatch(typeof(GameHistoryData), nameof(GameHistoryData.NotifyTechUnlock))]
        public static bool GameHistoryData_NotifyTechUnlock_Prefix(int _techId, int _level)
        {
            if (notifyNow)
            {
                return true;
            }

            unlockedTechs.Add(new int[2] { _techId, _level });
            return false;

        }

        [HarmonyPrefix, HarmonyPatch(typeof(StorageComponent), nameof(StorageComponent.NotifyStorageChange))]
        public static bool StorageComponent_NotifyStorageChange_Prefix(StorageComponent __instance)
        {
            if (notifyNow)
            {
                return true;
            }

            changedStorages.Add(__instance);
            return false;

        }


        [HarmonyPrefix, HarmonyPatch(typeof(CargoContainer), nameof(CargoContainer.Expand2x))]
        public static bool CargoContainer_Expand2x_Prefix(CargoContainer __instance)
        {
            if (notifyNow)
            {
                return true;
            }

            int num = __instance.poolCapacity << 1;
            Cargo[] sourceArray = __instance.cargoPool;
            __instance.cargoPool = new Cargo[num];
            __instance.recycleIds = new int[num];
            Array.Copy(sourceArray, __instance.cargoPool, __instance.poolCapacity);
            __instance.poolCapacity = num;

            expandedCargos.Add(__instance);
            return false;
        }
        public static void NotifyHistory()
        {
            foreach (var item in unlockedTechs)
            {
                GameMain.history.NotifyTechUnlock(item[0], item[1]);
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
        public static void ExpandCargos()
        {
            foreach (var item in expandedCargos)
            {
                item.computeBuffer.Release();
                item.computeBuffer = new ComputeBuffer(item.poolCapacity, 32, ComputeBufferType.Default);
            }

            expandedCargos.Clear();
        }

        public static void LateNotify()
        {
            notifyNow = true;
            NotifyHistory();
            NotifyStorages();
            ExpandCargos();
        }
    }
}
