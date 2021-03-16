using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [HarmonyPatch]
    class IncompatibilityNotice
    {

        [HarmonyPostfix, HarmonyPatch(typeof(GameMain), "Begin")]
        public static void PlayerAction_Build_CheckBuildConditions_Postfix()
        {
            UIMessageBox.Show("Multibuild mod confict", $"MultiBuild{MultiBuild.CHANNEL} is not compatible with the following plugins and will not be loaded:\n{MultiBuild.incompatiblePlugins.Join(null, "\n")}", "OK", 1);
        }
    }
}
