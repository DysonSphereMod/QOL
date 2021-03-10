using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;

namespace MultiDestruct
{
    public enum EDestructFilter
    {
        All,
        Buildings,
        Belts
    }
    public static class Extensions
    {
        public static T Next<T>(this T src) where T : struct
        {
            if (!typeof(T).IsEnum) throw new ArgumentException(String.Format("Argument {0} is not an Enum", typeof(T).FullName));

            T[] Arr = (T[])Enum.GetValues(src.GetType());
            int j = Array.IndexOf<T>(Arr, src) + 1;
            return (Arr.Length == j) ? Arr[0] : Arr[j];
        }
    }

    [BepInPlugin("com.brokenmass.plugin.DSP.MultiDestruct", "MultiDestruct", "1.0.0")]
    public class MultiDestruct : BaseUnityPlugin
    {
        Harmony harmony;

        const int DEFAULT_DESTRUCT_AREA = 5;
        const int MAX_DESTRUCT_AREA = 15;

        private static Color DESTRUCT_GIZMO_COLOR = new Color(0.9433962f, 0.1843137f, 0.1646493f, 1f);

        public static List<UIKeyTipNode> allTips;
        public static Dictionary<String, UIKeyTipNode> tooltips = new Dictionary<String, UIKeyTipNode>();
        public static bool multiDestructEnabled = false;
        public static bool multiDestructPossible = true;

        private static int lastCmdMode = 0;

        private static int area = DEFAULT_DESTRUCT_AREA;
        private static EDestructFilter filter = EDestructFilter.All;

        private static int[] _nearObjectIds = new int[4096];
        private static CircleGizmo circleGizmo;
        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.MultiDestruct");
            try
            {
                harmony.PatchAll(typeof(MultiDestruct));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

        }

        internal void OnDestroy()
        {
            harmony.UnpatchSelf();

            if (circleGizmo != null)
            {
                circleGizmo.Close();
                circleGizmo = null;
            }
            // For ScriptEngine hot-reloading
            foreach (var tooltip in tooltips.Values)
            {
                allTips.Remove(tooltip);
            }
        }

        void Update()
        {

            if (Input.GetKeyUp(KeyCode.LeftAlt) && IsMultiDestructAvailable())
            {
                multiDestructEnabled = !multiDestructEnabled;
            }

            var isRunning = IsMultiDestructRunning();

            if ((Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && isRunning && area < MAX_DESTRUCT_AREA)
            {
                area++;
            }

            if ((Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isRunning && area > 1)
            {
                area--;
            }

            if (Input.GetKeyUp(KeyCode.Tab) && isRunning)
            {
                filter = filter.Next();
            }
        }

        public static bool IsMultiDestructAvailable()
        {
            return UIGame.viewMode == EViewMode.Build && GameMain.mainPlayer.controller.cmd.mode == -1 && multiDestructPossible;
        }

        public static bool IsMultiDestructRunning()
        {
            return IsMultiDestructAvailable() && multiDestructEnabled;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerController), "UpdateCommandState")]
        public static void UpdateCommandState_Prefix(PlayerController __instance)
        {
            if (__instance.cmd.mode != lastCmdMode)
            {
                lastCmdMode = __instance.cmd.mode;

                multiDestructEnabled = false;
                if (circleGizmo != null)
                {
                    circleGizmo.Close();
                    circleGizmo = null;
                }
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIKeyTips), "UpdateTipDesiredState")]
        public static void UIKeyTips_UpdateTipDesiredState_Prefix(ref UIKeyTips __instance, ref List<UIKeyTipNode> ___allTips)
        {
            if (tooltips.Count == 0)
            {
                allTips = ___allTips;
                tooltips.Add("toggle-destruct", __instance.RegisterTip("L-ALT", "Toggle MultiDestruct mode"));
                tooltips.Add("increase-area", __instance.RegisterTip("+", "Increase delete area"));
                tooltips.Add("decrease-area", __instance.RegisterTip("-", "Decrease delete area"));
                tooltips.Add("change-filter", __instance.RegisterTip("TAB", "Change destruct filter"));
            }
            tooltips["toggle-destruct"].desired = IsMultiDestructAvailable();
            tooltips["change-filter"].desired = tooltips["decrease-area"].desired = tooltips["increase-area"].desired = IsMultiDestructRunning();
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(UIGeneralTips), "_OnUpdate")]
        public static void UIGeneralTips__OnUpdate_Postfix(ref Text ___modeText)
        {
            if (IsMultiDestructAvailable() && multiDestructEnabled)
            {
                ___modeText.text += $"\nMultiDestruct [{filter}] - Area {area}";
            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "DestructMainLogic")]
        public static bool DestructMainLogic_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.player.planetData.type == EPlanetType.Gas)
            {
                multiDestructPossible = false;
            }
            else
            {
                multiDestructPossible = true;
            }

            if (IsMultiDestructAvailable() && multiDestructEnabled)
            {
                if (circleGizmo == null)
                {
                    circleGizmo = CircleGizmo.Create(1, __instance.groundTestPos, area);

                    circleGizmo.fadeOutScale = circleGizmo.fadeInScale = 1.8f;
                    circleGizmo.fadeOutTime = circleGizmo.fadeInTime = 0.15f;
                    circleGizmo.color = DESTRUCT_GIZMO_COLOR;
                    circleGizmo.autoRefresh = true;
                    circleGizmo.Open();
                }

                circleGizmo.position = __instance.groundTestPos;
                circleGizmo.radius = 1.2f * area;


                if (!VFInput.onGUI)
                {
                    UICursor.SetCursor(ECursor.Delete);
                }
                __instance.ClearBuildPreviews();
                int found = __instance.nearcdLogic.GetBuildingsInAreaNonAlloc(__instance.groundTestPos, area, _nearObjectIds);

                var ids = new HashSet<int>();
                for (int x = 0; x < found; x++)
                {
                    var objId = _nearObjectIds[x];
                    if (ids.Contains(objId)) continue;

                    ids.Add(objId);
                    ItemProto itemProto = __instance.GetItemProto(objId);
                    PrefabDesc desc = itemProto.prefabDesc;
                    if (
                        (itemProto.prefabDesc.isBelt && filter == EDestructFilter.Buildings) ||
                        (!itemProto.prefabDesc.isBelt && filter == EDestructFilter.Belts))
                    {
                        continue;
                    }

                    Pose objectPose = __instance.GetObjectPose(objId);

                    BuildPreview buildPreview = new BuildPreview
                    {
                        item = itemProto,
                        desc = itemProto.prefabDesc,
                        lpos = objectPose.position,
                        lrot = objectPose.rotation,
                        objId = objId,
                        needModel = desc.lodCount > 0 && desc.lodMeshes[0] != null,
                        isConnNode = true
                    };

                    bool isInserter = buildPreview.desc.isInserter;
                    if (isInserter)
                    {
                        Pose objectPose2 = __instance.GetObjectPose2(buildPreview.objId);
                        buildPreview.lpos2 = objectPose2.position;
                        buildPreview.lrot2 = objectPose2.rotation;
                    }

                    if (buildPreview.desc.multiLevel)
                    {
                        bool flag;
                        int num;
                        int num2;
                        __instance.factory.ReadObjectConn(buildPreview.objId, 15, out flag, out num, out num2);
                        if (num != 0)
                        {
                            _nearObjectIds[found++] = num;
                        }
                    }

                    __instance.AddBuildPreview(buildPreview);
                }

                // reverse list so the building on 'top' are deleted first
                __instance.buildPreviews.Reverse();
                __instance.DetermineMoreDestructionTargets();
                __instance.UpdatePreviews();
                __instance.UpdateGizmos();

                if ((VFInput._buildConfirm.onDown || VFInput._buildConfirm.pressing) && __instance.buildPreviews.Count > 0)
                {
                    Destruct(__instance);
                }

                return false;
            }
            else
            {
                if (circleGizmo != null)
                {
                    circleGizmo.Close();
                    circleGizmo = null;
                }
                return true;
            }
        }

        /* Take the code from Destruct Action in the first if block
        *
        * if (VFInput._buildConfirm.onDown && this.buildPreviews.Count > 0)
		8 {
		*	int num = 0; <------------------- FROM HERE (included)
		*	bool flag = false;
		*	foreach (BuildPreview buildPreview in this.buildPreviews)
        *   ......
        *
        *   this.ClearBuildPreviews(); <----- TO HERE (included)
		* }
		* if (VFInput._buildConfirm.pressing && !VFInput._chainUX)
        */
        [HarmonyReversePatch, HarmonyPatch(typeof(PlayerAction_Build), "DestructAction")]
        public static void Destruct(PlayerAction_Build __instance)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> instructionsList = instructions.ToList();

                int startIdx = -1;
                for (int i = 0; i < instructionsList.Count - 1; i++)  // go to the end - 1 b/c we need to check two instructions to find valid loc
                {
                    if (instructionsList[i].opcode == OpCodes.Ldc_I4_0 && instructionsList[i + 1].opcode == OpCodes.Stloc_1)
                    {
                        startIdx = i;
                        break;
                    }
                }
                if (startIdx == -1)
                {
                    throw new InvalidOperationException("Cannot patch sorter posing code b/c the start indicator isn't present");
                }

                int endIdx = -1;
                for (int i = startIdx; i < instructionsList.Count; i++)
                {
                    if (instructionsList[i].Calls(typeof(PlayerAction_Build).GetMethod("ClearBuildPreviews")))
                    {
                        endIdx = i;
                        break;
                    }
                }
                if (endIdx == -1)
                {
                    throw new InvalidOperationException("Cannot patch sorter posing code b/c the end indicator isn't present");
                }

                List<CodeInstruction> code = new List<CodeInstruction>();

                for (int i = startIdx; i <= endIdx; i++)
                {
                    code.Add(instructionsList[i]);
                }
                return code.AsEnumerable();
            }

            // make compiler happy
            _ = Transpiler(null);
            return;
        }
    }
}