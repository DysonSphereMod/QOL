using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using com.brokenmass.plugin.DSP.MultiBuildUI;
using System.Linq;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [BepInPlugin("com.brokenmass.plugin.DSP.MultiBuild" + CHANNEL, "MultiBuild" + CHANNEL, "2.0.2")]
    [BepInDependency(CHANNEL == "Beta" ? "com.brokenmass.plugin.DSP.MultiBuild" : "com.brokenmass.plugin.DSP.MultiBuildBeta", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.fezeral.plugins.copyinserters", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("me.xiaoye97.plugin.Dyson.AdvancedBuildDestruct", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.brokenmass.plugin.DSP.MultiBuildUI", BepInDependency.DependencyFlags.HardDependency)]

    //KG-Long_Building_Selection_and_Free_M_Globalmap-1.0.0
    public class MultiBuild : BaseUnityPlugin
    {
        public const string CHANNEL = "Beta";
        public static List<string> BLACKLISTED_MODS = new List<string>() {
            CHANNEL == "Beta" ? "com.brokenmass.plugin.DSP.MultiBuild" : "com.brokenmass.plugin.DSP.MultiBuildBeta",
            "org.fezeral.plugins.copyinserters",
            "me.xiaoye97.plugin.Dyson.AdvancedBuildDestruct"
        };

        private Harmony harmony;

        public static int lastCmdMode = 0;
        public static ECommand lastCmdType;
        public static ConfigEntry<bool> itemSpecificSpacing;
        public static List<UIKeyTipNode> allTips;
        public static Dictionary<String, UIKeyTipNode> tooltips = new Dictionary<String, UIKeyTipNode>();
        public static bool multiBuildEnabled = false;
        public static bool multiBuildPossible = true;
        public static Vector3 startPos = Vector3.zero;
        public static Dictionary<int, int> spacingStore = new Dictionary<int, int>();
        public static int spacingIndex = 0;
        public static int selectionRadius = 5;

        public static bool isValidInstallation = true;
        public static List<string> incompatiblePlugins = new List<string>();
        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.MultiBuild" + CHANNEL);

            itemSpecificSpacing = Config.Bind<bool>("General", "itemSpecificSpacing", true, "If this option is set to true, the mod will remember the last spacing used for a specific building. Otherwise the spacing will be the same for all entities.");
            spacingStore[0] = 0;
            try
            {
                foreach (var pluginInfo in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    Debug.Log($"Found plugin '{pluginInfo.Key}'  / '{pluginInfo.Value.Metadata.GUID}'");
                    if(BLACKLISTED_MODS.Contains(pluginInfo.Value.Metadata.GUID))
                    {
                        incompatiblePlugins.Add(" - " + pluginInfo.Value.Metadata.Name);
                    }
                }

                if(incompatiblePlugins.Count > 0)
                {
                    isValidInstallation = false;
                    harmony.PatchAll(typeof(IncompatibilityNotice));
                }

                if (isValidInstallation)
                {
                    harmony.PatchAll(typeof(MultiBuild));
                    harmony.PatchAll(typeof(BlueprintManager));
                    harmony.PatchAll(typeof(BuildLogic));
                    harmony.PatchAll(typeof(BlueprintCreator));
                    harmony.PatchAll(typeof(InserterPoses));

                    UIBlueprintGroup.onCreate = () => BlueprintCreator.StartBpMode();
                    UIBlueprintGroup.onRestore = () => BlueprintManager.Restore();
                    UIBlueprintGroup.onImport = () =>
                    {
                        if (BlueprintCreator.bpMode)
                        {
                            BlueprintCreator.EndBpMode(true);
                        }
                        var data = BlueprintData.Import(GUIUtility.systemCopyBuffer);
                        if (data != null)
                        {
                            BlueprintManager.Restore(data);
                            UIMessageBox.Show("Blueprint imported", "Blueprint successfully imported from your clipboard", "OK", 1);
                        }
                        else
                        {
                            UIMessageBox.Show("Blueprint import error", "Blueprint successfully imported from your clipboard", "OK", 1);
                        }
                    };
                    UIBlueprintGroup.onExport = () =>
                    {
                        if (BlueprintCreator.bpMode)
                        {
                            BlueprintCreator.EndBpMode();
                        }
                        if (BlueprintManager.hasData)
                        {
                            GUIUtility.systemCopyBuffer = BlueprintManager.data.Export();
                            UIMessageBox.Show("Blueprint exported", "Blueprint successfully exported to your clipboard", "OK", 0);
                        }
                        else
                        {
                            UIMessageBox.Show("Blueprint export error", "No blueprint data to export", "OK", 0);
                        }

                    };
                }
            }
            catch (Exception e)
            {
                isValidInstallation = false;
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            // For ScriptEngine hot-reloading
            BlueprintCreator.EndBpMode(true);
            BlueprintManager.Reset();
            foreach (var tooltip in tooltips.Values)
            {
                allTips.Remove(tooltip);
            }
            harmony.UnpatchSelf();
        }

        private void Update()
        {
            if(!isValidInstallation)
            {
                return;
            }
            var isEnabled = IsMultiBuildEnabled();

            if (Input.GetKeyUp(KeyCode.LeftAlt) && IsMultiBuildAvailable())
            {
                multiBuildEnabled = !multiBuildEnabled;
                if (multiBuildEnabled)
                {
                    startPos = Vector3.zero;
                }
            }

            if ((Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && BlueprintCreator.bpMode && selectionRadius < 14)
            {
                ++selectionRadius;
            }

            if ((Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && BlueprintCreator.bpMode && selectionRadius > 1)
            {
                --selectionRadius;
            }

            if ((Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && isEnabled)
            {
                spacingStore[spacingIndex]++;
                BuildLogic.forceRecalculation = true;
            }

            if ((Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isEnabled && spacingStore[spacingIndex] > 0)
            {
                spacingStore[spacingIndex]--;
                BuildLogic.forceRecalculation = true;
            }

            if ((Input.GetKeyUp(KeyCode.Alpha0) || Input.GetKeyUp(KeyCode.Keypad0)) && isEnabled)
            {
                spacingStore[spacingIndex] = 0;
                BuildLogic.forceRecalculation = true;
            }
            if (Input.GetKeyUp(KeyCode.Z) && IsMultiBuildRunning())
            {
                BuildLogic.path = 1 - BuildLogic.path;
                BuildLogic.forceRecalculation = true;
            }
        }

        public static bool IsMultiBuildAvailable()
        {
            return UIGame.viewMode == EViewMode.Build && GameMain.mainPlayer.controller.cmd.mode == 1 && multiBuildPossible;
        }

        public static bool IsMultiBuildEnabled()
        {
            return IsMultiBuildAvailable() && multiBuildEnabled;
        }

        public static bool IsMultiBuildRunning()
        {
            return IsMultiBuildEnabled() && startPos != Vector3.zero;
        }

        public static void ResetMultiBuild()
        {
            spacingIndex = 0;
            BuildLogic.path = 0;
            BuildLogic.forceRecalculation = true;
            multiBuildEnabled = false;
            startPos = Vector3.zero;

            if (!itemSpecificSpacing.Value)
            {
                spacingStore[spacingIndex] = 0;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerController), "UpdateCommandState")]
        public static void UpdateCommandState_Prefix(PlayerController __instance)
        {
            if (__instance.cmd.type != ECommand.None && __instance.cmd.type != ECommand.Follow &&
                (__instance.cmd.mode != lastCmdMode || __instance.cmd.type != lastCmdType))
            {

                multiBuildEnabled = false;
                startPos = Vector3.zero;

                if (__instance.cmd.type != ECommand.Build || __instance.cmd.mode != 0)
                {
                    BlueprintCreator.EndBpMode();
                }

                Debug.Log($"{__instance.cmd.type}  / {__instance.cmd.mode}");
                if(__instance.cmd.type != ECommand.Build || (__instance.cmd.mode != 1 && __instance.cmd.mode != 0))
                {
                    BlueprintManager.Reset();
                }


                lastCmdMode = __instance.cmd.mode;
                lastCmdType = __instance.cmd.type;
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIKeyTips), "UpdateTipDesiredState")]
        public static void UIKeyTips_UpdateTipDesiredState_Prefix(ref UIKeyTips __instance, ref List<UIKeyTipNode> ___allTips)
        {
            if (tooltips.Count == 0)
            {
                allTips = ___allTips;
                tooltips.Add("toggle-build", __instance.RegisterTip("L-ALT", "Toggle multiBuild mode"));
                tooltips.Add("increase-spacing", __instance.RegisterTip("+", "Increase space between copies"));
                tooltips.Add("decrease-spacing", __instance.RegisterTip("-", "Decrease space between copies"));
                tooltips.Add("increase-radius", __instance.RegisterTip("+", "Increase selection area"));
                tooltips.Add("decrease-radius", __instance.RegisterTip("-", "Decrease selection area"));
                tooltips.Add("zero-spacing", __instance.RegisterTip("0", "Reset space between copies"));
                tooltips.Add("rotate-path", __instance.RegisterTip("Z", "Rotate build path"));
            }
            tooltips["toggle-build"].desired = IsMultiBuildAvailable();
            tooltips["rotate-path"].desired = tooltips["zero-spacing"].desired = tooltips["decrease-spacing"].desired = tooltips["increase-spacing"].desired = IsMultiBuildRunning();
            tooltips["decrease-radius"].desired = tooltips["increase-radius"].desired = BlueprintCreator.bpMode;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(UIGeneralTips), "_OnUpdate")]
        public static void UIGeneralTips__OnUpdate_Postfix(ref Text ___modeText)
        {
            if (IsMultiBuildAvailable() && multiBuildEnabled)
            {
                ___modeText.text += $"\nMultiBuild [{(startPos == Vector3.zero ? "START" : "END")}]";

                if (spacingStore[spacingIndex] > 0)
                {
                    ___modeText.text += $" - Spacing {spacingStore[spacingIndex]}";
                }
            }

            if (BlueprintCreator.bpMode)
            {
                ___modeText.text = "Blueprint Mode";
            }
        }
    }
}
