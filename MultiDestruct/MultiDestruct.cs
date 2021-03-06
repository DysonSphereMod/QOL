using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
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

        const int MAX_IGNORED_TICKS = 60;
        const int DEFAULT_DESTRUCT_AREA = 5;
        const int MAX_DESTRUCT_AREA = 15;

        private static Color DESTRUCT_AREA_COLOR = new Color(0.8490566f, 0.3096371f, 0.2843539f, 0.2039216f);
        private static Color REFORM_AREA_COLOR = new Color(0.2667686f, 0.5636916f, 0.8862745f, 0.552941f);
        private static Color ORIGINAL_REPEAT_COLOR = new Color(1f, 0.9053909f, 0.8160377f, 0.3647059f);

        public static List<UIKeyTipNode> allTips;
        public static Dictionary<String, UIKeyTipNode> tooltips = new Dictionary<String, UIKeyTipNode>();
        public static bool multiDestructEnabled = false;
        public static bool multiDestructPossible = true;

        private static int lastCmdMode = 0;

        private static int ignoredTicks = 0;
        private static int area = DEFAULT_DESTRUCT_AREA;
        private static EDestructFilter filter = EDestructFilter.All;

        private static int[] destructIndices = new int[MAX_DESTRUCT_AREA * MAX_DESTRUCT_AREA];
        public static Vector3[] destructPoints = new Vector3[MAX_DESTRUCT_AREA * MAX_DESTRUCT_AREA];
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
            // For ScriptEngine hot-reloading
            foreach (var tooltip in tooltips.Values)
            {
                allTips.Remove(tooltip);
            }

            harmony.UnpatchSelf();
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
                ignoredTicks = MAX_IGNORED_TICKS;
            }

            if ((Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isRunning && area > 1)
            {
                area--;
                ignoredTicks = MAX_IGNORED_TICKS;
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

        public static void ResetMultiDestruct()
        {
            ignoredTicks = 0;
            multiDestructEnabled = false;
            filter = EDestructFilter.All;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIBuildingGrid), "Update")]
        public static void UIGeneralTips__OnUpdate_Postfix(ref UIBuildingGrid __instance)
        {
            if (__instance == null)
            {
                return;
            }
            if (IsMultiDestructAvailable() && multiDestructEnabled)
            {
                __instance.material.SetColor("_RepeatColor", DESTRUCT_AREA_COLOR);
                __instance.material.SetColor("_CursorColor", DESTRUCT_AREA_COLOR);
                __instance.material.SetFloat("_ReformMode", 1f);
                __instance.material.SetFloat("_ZMin", -1.5f);

                int[] reformIndices = destructIndices;
                var cells = area * area;
                for (int i = 0; i < area * area; i++)
                {
                    int num6 = reformIndices[i];
                    if (num6 >= 0)
                    {
                        if (num6 < __instance.reformCursorMap.Length)
                        {
                            __instance.reformCursorMap[num6] = 2;
                        }
                    }
                }
                __instance.reformCursorBuffer.SetData(__instance.reformCursorMap);
                for (int i = 0; i < cells; i++)
                {
                    int num6 = reformIndices[i];
                    if (num6 >= 0)
                    {
                        if (num6 < __instance.reformCursorMap.Length)
                        {
                            __instance.reformCursorMap[num6] = 0;
                        }
                    }
                }
            }
            else
            {
                __instance.material.SetColor("_CursorColor", REFORM_AREA_COLOR);
                __instance.material.SetColor("_RepeatColor", ORIGINAL_REPEAT_COLOR);
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerController), "UpdateCommandState")]
        public static void UpdateCommandState_Prefix(PlayerController __instance)
        {
            if (__instance.cmd.mode != lastCmdMode)
            {
                multiDestructEnabled = false;
                lastCmdMode = __instance.cmd.mode;
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
                tooltips.Add("change-filter", __instance.RegisterTip("-", "Change destruct filter"));
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

        private static int[] _nearObjectIds = new int[4096];
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
                Vector3 centerPoint;
                __instance.reformPointsCount = __instance.planetAux.ReformSnap(__instance.groundTestPos, Math.Max(1, area - 1), 1, 1, destructPoints, destructIndices, __instance.factory.platformSystem, out centerPoint);

                if (!VFInput.onGUI)
                {
                    UICursor.SetCursor(ECursor.Delete);
                }
                __instance.ClearBuildPreviews();
                int found = __instance.nearcdLogic.GetBuildingsInAreaNonAlloc(centerPoint, 1.2f * area / 2f, _nearObjectIds);

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

                if (VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0)
                {

                }
            }
            else
            {
                __instance.DetermineDestructPreviews();
                __instance.DetermineMoreDestructionTargets();
                __instance.UpdatePreviews();
                __instance.UpdateGizmos();
                __instance.DestructAction();
            }



            return false;
        }


        [HarmonyReversePatch, HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static void Destruct(PlayerAction_Build __instance)
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> instructionsList = instructions.ToList();

                // Find the idx at which the "cargoTraffic" field of the PlanetFactory
                // Is first accessed since this is the start of the instructions that compute posing

                /* ex of the code in dotpeek:
                 * ```
                 * if (this.cursorValid && this.startObjId != this.castObjId && (this.startObjId > 0 && this.castObjId > 0))
                 * {
                 *   CargoTraffic cargoTraffic = this.factory.cargoTraffic; <- WE WANT TO START WITH THIS LINE (INCLUSIVE)
                 *   EntityData[] entityPool = this.factory.entityPool;
                 *   BeltComponent[] beltPool = cargoTraffic.beltPool;
                 *   this.posePairs.Clear();
                 *   this.startSlots.Clear();
                 * ```
                 */
                // good night friend <3 , see you tomorrow :X
                int startIdx = -1;
                for (int i = 0; i < instructionsList.Count; i++)
                {
                    if (instructionsList[i].LoadsConstant(0))
                    {
                        startIdx = i; // need the two proceeding lines that are ldarg.0 and ldfld PlayerAction_Build::factory
                        break;
                    }
                }
                if (startIdx == -1)
                {
                    throw new InvalidOperationException("Cannot patch sorter posing code b/c the start indicator isn't present");
                }

                // Find the idx at which the "posePairs" field of the PlayerAction_Build
                // Is first accessed and followed by a call to get_Count

                /*
                 * ex of the code in dotpeek:
                 * ```
                 *          else
                 *              flag6 = true;
                 *      }
                 *      else
                 *        flag6 = true;
                 *    }
                 *  }
                 *  if (this.posePairs.Count > 0) <- WE WANT TO END ON THIS LINE (EXCLUSIVE)
                 *  {
                 *    float num1 = 1000f;
                 *    float num2 = Vector3.Distance(this.currMouseRay.origin, this.cursorTarget) + 10f;
                 *    PlayerAction_Build.PosePair posePair2 = new PlayerAction_Build.PosePair();
                 * ```
                 */

                int endIdx = -1;
                for (int i = startIdx; i < instructionsList.Count - 1; i++) // go to the end - 1 b/c we need to check two instructions to find valid loc
                {
                        if (instructionsList[i].Calls(typeof(List<PlayerAction_Build>).GetMethod("ClearBuildPreviews")))
                        {
                            endIdx = i; // need the proceeding line that is ldarg.0
                            break;
                        }
                }
                if (endIdx == -1)
                {
                    throw new InvalidOperationException("Cannot patch sorter posing code b/c the end indicator isn't present");
                }

                // The first argument to an instance method (arg 0) is the instance itself
                // Since this is a static method, the instance will still need to be passed
                // For the IL instructions to work properly so manually pass the instance as
                // The first argument to the method.
                List<CodeInstruction> code = new List<CodeInstruction>();

                for (int i = startIdx; i < endIdx; i++)
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