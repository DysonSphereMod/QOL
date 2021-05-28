using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace BuildCounter
{

    [BepInPlugin("com.brokenmass.plugin.DSP.BuildCounter", "BuildCounter", "1.2.1")]
    public class BuildCounter : BaseUnityPlugin
    {
        Harmony harmony;

        internal class ItemCounter
        {
            public int owned = 0;
            public int count = 0;
            public string name;
            public string sourceName = "";
        }

        internal static readonly string SPACING = new String(' ', 5);

        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.BuildCounter");
            try
            {
                harmony.PatchAll(typeof(BuildCounter));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            // For ScriptEngine hot-reloading
            harmony.UnpatchSelf();
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(BuildTool_Click), nameof(BuildTool_Click.CheckBuildConditions))]
        public static void BuildTool_Click_CheckBuildConditions_Postfix(BuildTool_Click __instance)
        {
            RenderBuildCounter(__instance);
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Click.CheckBuildConditions))]
        public static void BuildTool_Path_CheckBuildConditions_Postfix(BuildTool_Path __instance)
        {
            RenderBuildCounter(__instance);
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(BuildTool_Upgrade), nameof(BuildTool_Upgrade.DeterminePreviews))]
        public static void BuildTool_Upgrade_DeterminePreviews_Postfix(BuildTool_Upgrade __instance)
        {
            RenderBuildCounter(__instance);
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(BuildTool_Dismantle), nameof(BuildTool_Dismantle.DeterminePreviews))]
        public static void BuildTool_Dismantle_DeterminePreviews_Postfix(BuildTool_Dismantle __instance)
        {
            RenderBuildCounter(__instance);
        }

        public static void RenderBuildCounter(BuildTool __instance)
        {
            if (__instance.buildPreviews.Count > 0)
            {
                var counter = new Dictionary<int, ItemCounter>();

                foreach (var buildPreview in __instance.buildPreviews)
                {
                    var item = buildPreview.item;

                    if (__instance is BuildTool_Upgrade && item.canUpgrade)
                    {
                        item = item.GetUpgradeItem(((BuildTool_Upgrade)__instance).upgradeLevel);

                        if (item.ID == buildPreview.item.ID)
                        {
                            continue;
                        }
                    }

                    var id = item.ID;
                    if (!counter.ContainsKey(id))
                    {
                        var name = item.name;
                        var sourceName = buildPreview.item.name;

                        var owned = GameMain.mainPlayer.package.GetItemCount(id);
                        if (__instance.player.inhandItemId == id)
                        {
                            owned += GameMain.mainPlayer.inhandItemCount;
                        }
                        counter.Add(id, new ItemCounter()
                        {
                            name = name,
                            sourceName = sourceName,
                            owned = owned
                        });

                    }

                    counter[id].count++;
                }

                var text = new StringBuilder();

                if (__instance is BuildTool_Upgrade && counter.Count > 0)
                {
                    text.Append(((BuildTool_Upgrade)__instance).upgradeLevel == 1 ? "\nUpgrading:" : "\nDowngrading:");
                    foreach (var itemCounter in counter.Values)
                    {
                        text.Append($"\n{SPACING}- {itemCounter.count} x {itemCounter.sourceName} to {itemCounter.name} [ {itemCounter.owned} ]");
                    }
                }
                else if (__instance is BuildTool_Dismantle)
                {
                    text.Append("\nDestructing:");
                    foreach (var itemCounter in counter.Values)
                    {
                        text.Append($"\n{SPACING}- {itemCounter.count} x {itemCounter.name}");
                    }
                }
                else if (counter.Count > 0)
                {
                    text.Append("\nBuilding:");
                    foreach (var itemCounter in counter.Values)
                    {
                        text.Append($"\n{SPACING}- {itemCounter.count} x {itemCounter.name} [ {itemCounter.owned} ]");
                    }
                }

                __instance.actionBuild.model.cursorText += text.ToString();
            }

            PlayerController controller = GameMain.mainPlayer.controller;
            var defaultActivateTip = (controller.cmd.type == ECommand.Build && (controller.cmd.state > 0 || controller.cmd.mode == 3) && !VFInput.onGUI) || UIRoot.instance.uiGame.functionPanel.isPointEnter;
            if (defaultActivateTip && __instance.buildPreviews.Count > 0)
            {
                UIRoot.instance.uiGame.handTip.SetTipActive(false);
            }
            else
            {
                UIRoot.instance.uiGame.handTip.SetHandTip();
            }
        }
    }
}
