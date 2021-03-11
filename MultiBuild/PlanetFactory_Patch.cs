using HarmonyLib;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    internal class PlanetFactory_Patch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlanetFactory), "WriteObjectConn")]
        public static void WriteObjectConn_Prefix(ref PlanetFactory __instance, int objId, int slot, bool isOutput, int otherObjId, ref int otherSlot)
        {
            if (otherSlot == -1 && otherObjId < 0)
            {
                for (int i = 4; i < 12; i++)
                {
                    if (__instance.prebuildConnPool[-otherObjId * 16 + i] == 0)
                    {
                        otherSlot = i;
                        break;
                    }
                }
            }
        }
    }
}
