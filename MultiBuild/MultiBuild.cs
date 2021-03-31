using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text.RegularExpressions;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [BepInPlugin("com.brokenmass.plugin.DSP.MultiBuild" + CHANNEL, "MultiBuild" + CHANNEL, VERSION)]
    [BepInDependency("com.brokenmass.plugin.DSP.MultiBuildUI", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(CHANNEL == "Beta" ? "com.brokenmass.plugin.DSP.MultiBuild" : "com.brokenmass.plugin.DSP.MultiBuildBeta", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.fezeral.plugins.copyinserters", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("me.xiaoye97.plugin.Dyson.AdvancedBuildDestruct", BepInDependency.DependencyFlags.SoftDependency)]

    //possible incompatible dependency : KG-Long_Building_Selection_and_Free_M_Globalmap-1.0.0
    public class MultiBuild : BaseUnityPlugin
    {
        public const string CHANNEL = "Beta";
        public const string VERSION = "2.3.0";
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
        public static bool multiBuildInserters = true;
        public static Vector3 startPos = Vector3.zero;
        public static Dictionary<int, int> spacingStore = new Dictionary<int, int>();
        public static int spacingIndex = 0;
        public static int spacingPeriod = 1;

        public static int selectionRadius = 5;

        public static bool isValidInstallation = true;
        public static List<string> incompatiblePlugins = new List<string>();

        IEnumerator CheckForUpdates()
        {
            var req = UnityWebRequest.Get($"https://dsp.thunderstore.io/api/experimental/package/brokenmass/MultiBuild${CHANNEL}/");
            req.timeout = 6;
            yield return req.SendWebRequest();

            if (!req.isNetworkError)
            {
                Regex rg = new Regex("\"version_number\":\"([^\"]*)\"");
                var match = rg.Match(req.downloadHandler.text);

                if (match.Success && VERSION != match.Groups[1].Value)
                {
                    // Notify user of new version
                }
            }
        }

        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.MultiBuild" + CHANNEL);
            itemSpecificSpacing = Config.Bind<bool>("General", "itemSpecificSpacing", true, "If this option is set to true, the mod will remember the last spacing used for a specific building. Otherwise the spacing will be the same for all entities.");
            spacingStore[0] = 0;

            //StartCoroutine(CheckForUpdates());

            try
            {
                foreach (var pluginInfo in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    if (BLACKLISTED_MODS.Contains(pluginInfo.Value.Metadata.GUID))
                    {
                        incompatiblePlugins.Add(" - " + pluginInfo.Value.Metadata.Name);
                    }
                }

                if (incompatiblePlugins.Count > 0)
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
                    UIBlueprintGroup.onRestore = () =>
                    {
                        if (BlueprintCreator.bpMode)
                        {
                            BlueprintCreator.EndBpMode(true);
                        }
                        BlueprintManager.Restore();
                    };
                    UIBlueprintGroup.onImport = () =>
                    {
                        if (BlueprintCreator.bpMode)
                        {
                            BlueprintCreator.EndBpMode(true);
                        }
                        var data = BlueprintData.Import(GUIUtility.systemCopyBuffer, out HashSet<int> incompatibleIds);
                        if (data != null)
                        {
                            BlueprintManager.Restore(data);
                            UIRealtimeTip.Popup("Blueprint successfully imported from your clipboard", false);
                        }
                        else
                        {
                            string message = "Error while importing data from your clipboard";
                            if (incompatibleIds.Count > 0)
                            {
                                message += $" - Found {incompatibleIds.Count} incompatible entities.\nIds: [{incompatibleIds.Join(null, ", ")}]";
                            }
                            UIRealtimeTip.Popup(message, true);
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
                            UIRealtimeTip.Popup("Blueprint successfully exported to your clipboard", false);
                        }
                        else
                        {
                            UIRealtimeTip.Popup("No blueprint data to export", true);
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

        internal void Update()
        {
            if (!isValidInstallation)
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
                BuildLogic.forceRecalculation = true;
            }

            if (Input.GetKeyUp(KeyCode.Tab) && IsMultiBuildAvailable())
            {
                multiBuildInserters = !multiBuildInserters;
                BuildLogic.forceRecalculation = true;
            }

            if ((Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && BlueprintCreator.bpMode && selectionRadius < 14)
            {
                ++selectionRadius;
            }

            if ((Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && BlueprintCreator.bpMode && selectionRadius > 1)
            {
                --selectionRadius;
            }

            if (VFInput.control && (Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && isEnabled)
            {
                spacingPeriod++;
                BuildLogic.forceRecalculation = true;
            }

            if (VFInput.control && (Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isEnabled && spacingPeriod > 1)
            {
                spacingPeriod--;
                BuildLogic.forceRecalculation = true;
            }

            if (!VFInput.control && (Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && isEnabled)
            {
                spacingStore[spacingIndex]++;
                BuildLogic.forceRecalculation = true;
            }

            if (!VFInput.control && (Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isEnabled && spacingStore[spacingIndex] > 0)
            {
                spacingStore[spacingIndex]--;
                BuildLogic.forceRecalculation = true;
            }

            if ((Input.GetKeyUp(KeyCode.Alpha0) || Input.GetKeyUp(KeyCode.Keypad0)) && isEnabled)
            {
                spacingStore[spacingIndex] = 0;
                spacingPeriod = 1;
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
            BuildLogic.lastRunOriginal = true;
            multiBuildInserters = true;
            multiBuildEnabled = false;
            startPos = Vector3.zero;
            spacingPeriod = 1;
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
                if (__instance.cmd.type != ECommand.Build || __instance.cmd.mode != 1)
                {
                    ResetMultiBuild();
                }
                if (__instance.cmd.type != ECommand.Build || __instance.cmd.mode != 0)
                {
                    BlueprintCreator.EndBpMode();
                }

                // the preivous command might force us to stau in BuildMode (Even though we were leaving)
                if (__instance.cmd.type == ECommand.Build && lastCmdMode == 1 && __instance.cmd.mode != 1)
                {
                    BlueprintManager.Reset();
                }

                if (__instance.cmd.type != ECommand.Build)
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
                tooltips.Add("toggle-inserters", __instance.RegisterTip("TAB", "Toggle inserters copy"));
                tooltips.Add("increase-spacing", __instance.RegisterTip("+", "Increase space between copies"));
                tooltips.Add("decrease-spacing", __instance.RegisterTip("-", "Decrease space between copies"));
                tooltips.Add("increase-period", __instance.RegisterTip("CTRL", "+", "Increase spacing period"));
                tooltips.Add("decrease-period", __instance.RegisterTip("CTRL", "-", "Decrease spacing period"));
                tooltips.Add("zero-spacing", __instance.RegisterTip("0", "Reset space between copies"));
                tooltips.Add("rotate-path", __instance.RegisterTip("Z", "Rotate build path"));

                tooltips.Add("increase-radius", __instance.RegisterTip("+", "Increase selection area"));
                tooltips.Add("decrease-radius", __instance.RegisterTip("-", "Decrease selection area"));
                tooltips.Add("bp-select", __instance.RegisterTip(0, "Add building to blueprint"));
                tooltips.Add("bp-deselect", __instance.RegisterTip("CTRL", 0, "Remove building from blueprint"));


            }
            tooltips["toggle-build"].desired = tooltips["toggle-inserters"].desired = IsMultiBuildAvailable();

            tooltips["rotate-path"].desired =
                tooltips["zero-spacing"].desired =
                tooltips["decrease-spacing"].desired =
                tooltips["increase-spacing"].desired =
                tooltips["decrease-period"].desired =
                tooltips["increase-period"].desired =
                    IsMultiBuildRunning();

            tooltips["increase-radius"].desired =
                tooltips["decrease-radius"].desired =
                tooltips["bp-select"].desired =
                tooltips["bp-deselect"].desired =
                BlueprintCreator.bpMode;
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
                    if (spacingPeriod > 1)
                    {
                        ___modeText.text += $" every {spacingPeriod} copies";
                    }
                }
            }

            if (BlueprintCreator.bpMode)
            {
                ___modeText.text = "Blueprint Mode";
            }
        }
    }
}
