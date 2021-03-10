using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace BuildCounter
{

    [BepInPlugin("com.brokenmass.plugin.DSP.BuildCounter", "BuildCounter", "1.2.0")]
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

        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "UpdatePreviews")]
        public static void UpdatePreviews_Postfix(ref PlayerAction_Build __instance)
        {
            if (__instance.buildPreviews.Count > 0)
            {
                var counter = new Dictionary<int, ItemCounter>();

                foreach (var buildPreview in __instance.buildPreviews)
                {
                    var item = buildPreview.item;

                    if (GameMain.mainPlayer.controller.cmd.mode == -2 && item.canUpgrade)
                    {
                        item = item.GetUpgradeItem(__instance.upgradeLevel);

                        if (item == buildPreview.item)
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
                        if (__instance.handItem != null && __instance.handItem.ID == id)
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

                if (__instance.upgrading && counter.Count > 0)
                {
                    text.Append(__instance.upgradeLevel == 1 ? "\nUpgrading:" : "\nDowngrading:");
                    foreach (var itemCounter in counter.Values)
                    {
                        text.Append($"\n{SPACING}- {itemCounter.count} x {itemCounter.sourceName} to {itemCounter.name} [ {itemCounter.owned} ]");
                    }
                }
                else if (__instance.destructing)
                {
                    text.Append("\nDestructing:");
                    foreach (var itemCounter in counter.Values)
                    {
                        text.Append($"\n{SPACING}- {itemCounter.count} x {itemCounter.name}");
                    }
                }
                else if (GameMain.mainPlayer.controller.cmd.mode > 0)
                {
                    text.Append("\nBuilding:");
                    foreach (var itemCounter in counter.Values)
                    {
                        text.Append($"\n{SPACING}- {itemCounter.count} x {itemCounter.name} [ {itemCounter.owned} ]");
                    }
                }

                __instance.cursorText += text.ToString();
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
