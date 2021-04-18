using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace com.brokenmass.plugin.DSP.BetterFPS
{
    class ThreadSafety
    {
        public static bool notifyNow = false;
        private static List<int[]> unlockedTechs = new List<int[]>();
        private static List<StorageComponent> changedStorages = new List<StorageComponent>();


        [HarmonyPrefix, HarmonyPatch(typeof(GameHistoryData), nameof(GameHistoryData.NotifyTechUnlock))]
        public static bool GameHistoryData_NotifyTechUnlock_Prefix(int _techId, int _level)
        {
            if (notifyNow)
            {
                return true;
            }
            else
            {
                unlockedTechs.Add(new int[2] { _techId, _level });
                return false;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(StorageComponent), nameof(StorageComponent.NotifyStorageChange))]
        public static bool StorageComponent_NotifyStorageChange_Prefix(StorageComponent __instance)
        {
            if (notifyNow)
            {
                return true;
            }
            else
            {
                changedStorages.Add(__instance);
                return false;
            }
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

            unlockedTechs.Clear();
        }
        public static void LateNotify()
        {
            notifyNow = true;
            NotifyHistory();
            NotifyStorages();
        }
    }
}
