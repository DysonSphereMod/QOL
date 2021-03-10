using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [BepInPlugin("com.brokenmass.plugin.DSP.MultiBuild", "MultiBuild", "1.1.2")]
    public class MultiBuild : BaseUnityPlugin
    {
        public const int MAX_IGNORED_TICKS = 60;

        private Harmony harmony;

        public static int lastCmdMode = 0;
        public static ConfigEntry<bool> itemSpecificSpacing;
        public static List<UIKeyTipNode> allTips;
        public static Dictionary<String, UIKeyTipNode> tooltips = new Dictionary<String, UIKeyTipNode>();
        public static bool multiBuildEnabled = false;
        public static bool multiBuildPossible = true;
        public static Vector3 startPos = Vector3.zero;
        public static Dictionary<int, int> spacingStore = new Dictionary<int, int>();
        public static int spacingIndex = 0;

        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.MultiBuild");

            itemSpecificSpacing = Config.Bind<bool>("General", "itemSpecificSpacing", true, "If this option is set to true, the mod will remember the last spacing used for a specific building. Otherwise the spacing will be the same for all entities.");
            spacingStore[0] = 0;
            try
            {
                harmony.PatchAll(typeof(MultiBuild));
                harmony.PatchAll(typeof(PlayerAction_Build_Patch));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            // For ScriptEngine hot-reloading
            foreach (var tooltip in tooltips.Values)
            {
                allTips.Remove(tooltip);
            }

            harmony.UnpatchSelf();
        }

        private void Update()
        {
            var isEnabled = IsMultiBuildEnabled();

            if (Input.GetKeyUp(KeyCode.LeftAlt) && IsMultiBuildAvailable())
            {
                multiBuildEnabled = !multiBuildEnabled;
                if (multiBuildEnabled)
                {
                    startPos = Vector3.zero;
                }
            }

            if ((Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && isEnabled)
            {
                spacingStore[spacingIndex]++;
                PlayerAction_Build_Patch.ignoredTicks = MAX_IGNORED_TICKS;
            }

            if ((Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isEnabled && spacingStore[spacingIndex] > 0)
            {
                spacingStore[spacingIndex]--;
                PlayerAction_Build_Patch.ignoredTicks = MAX_IGNORED_TICKS;
            }

            if ((Input.GetKeyUp(KeyCode.Alpha0) || Input.GetKeyUp(KeyCode.Keypad0)) && isEnabled)
            {
                spacingStore[spacingIndex] = 0;
                PlayerAction_Build_Patch.ignoredTicks = MAX_IGNORED_TICKS;
            }
            if (Input.GetKeyUp(KeyCode.Z) && IsMultiBuildRunning())
            {
                PlayerAction_Build_Patch.path = 1 - PlayerAction_Build_Patch.path;
                PlayerAction_Build_Patch.ignoredTicks = MAX_IGNORED_TICKS;
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
            PlayerAction_Build_Patch.path = 0;
            PlayerAction_Build_Patch.ignoredTicks = 0;
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
            if (__instance.cmd.mode != lastCmdMode)
            {
                multiBuildEnabled = false;
                startPos = Vector3.zero;
                

                if (__instance.cmd.mode != 1)
                {
                    Debug.Log($"RESETTING {lastCmdMode} - {__instance.cmd.mode} ");
                    PlayerAction_Build_Patch.copiedAssemblers.Clear();
                    PlayerAction_Build_Patch.copiedInserters.Clear();
                }

                lastCmdMode = __instance.cmd.mode;
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
                tooltips.Add("zero-spacing", __instance.RegisterTip("0", "Reset space between copies"));
                tooltips.Add("rotate-path", __instance.RegisterTip("Z", "Rotate build path"));
            }
            tooltips["toggle-build"].desired = IsMultiBuildAvailable();
            tooltips["rotate-path"].desired = tooltips["zero-spacing"].desired = tooltips["decrease-spacing"].desired = tooltips["increase-spacing"].desired = IsMultiBuildRunning();
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
        }

    }
}