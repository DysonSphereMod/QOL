using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MultiBuild
{


    [BepInPlugin("com.brokenmass.plugin.DSP.MultiBuild", "MultiBuild", "1.0.0")]
    public class MultiBuild : BaseUnityPlugin
    {
        Harmony harmony;

        public static List<UIKeyTipNode> allTips;
        public static Dictionary<String, UIKeyTipNode> tooltips = new Dictionary<String, UIKeyTipNode>();
        public static bool multiBuildEnabled = false;
        public static Vector3 startPos = Vector3.zero;

        private static Collider[] _tmp_cols = new Collider[256];
        public static bool lastFlag = false;
        private static Vector3 lastPosition = Vector3.zero;
        private static bool executeBuildUpdatePreviews = true;
        private static int ignoredTicks = 0;

        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.MultiBuild");
            try
            {
                harmony.PatchAll(typeof(MultiBuild));
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
            if (Input.GetKeyUp(KeyCode.LeftAlt) && IsMultiBuildAvailable())
            {
                multiBuildEnabled = !multiBuildEnabled;
                if (multiBuildEnabled)
                {
                    startPos = Vector3.zero;
                }
            }
        }

        public static bool IsMultiBuildAvailable()
        {
            return UIGame.viewMode == EViewMode.Build && GameMain.mainPlayer.controller.cmd.mode == 1;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIKeyTips), "UpdateTipDesiredState")]
        public static void UIKeyTips_UpdateTipDesiredState_Prefix(ref UIKeyTips __instance, ref List<UIKeyTipNode> ___allTips)
        {
            if (tooltips.Count == 0)
            {
                allTips = ___allTips;
                tooltips.Add("toggle-build", __instance.RegisterTip("L-ALT", "Toggle multiBuild mode"));

            }
            tooltips["toggle-build"].desired = IsMultiBuildAvailable();
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(UIGeneralTips), "_OnUpdate")]
        public static void UIGeneralTips__OnUpdate_Postfix(ref Text ___modeText)
        {
            if (IsMultiBuildAvailable() && multiBuildEnabled)
            {
                ___modeText.text += $"\nMultiBuild [{(startPos == Vector3.zero ? "START" : "END")}]";
            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
        public static bool CreatePrebuilds_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0
                && IsMultiBuildAvailable() && multiBuildEnabled)
            {
                if (startPos == Vector3.zero)
                {
                    startPos = __instance.groundSnappedPos;
                    return false;
                }
                else
                {

                    startPos = Vector3.zero;
                    return true;
                }
            }

            return true;
        }


        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "BuildMainLogic")]
        public static bool DetermineBuildPreviews_Prefix(ref PlayerAction_Build __instance)
        {
            executeBuildUpdatePreviews = true;
            if (IsMultiBuildAvailable() && multiBuildEnabled && startPos != Vector3.zero)
            {
                if (lastPosition != __instance.groundSnappedPos)
                {
                    lastPosition = __instance.groundSnappedPos;
                    executeBuildUpdatePreviews = true;
                } else
                {
                    executeBuildUpdatePreviews = false;
                }
            }
            else
            {
                lastPosition = Vector3.zero;
            }
            // Run the preview methods if we have changed position, if we have received a relevant keyboard input or in any case every 60 ticks.
            executeBuildUpdatePreviews = executeBuildUpdatePreviews || VFInput._rotate || VFInput._counterRotate || ignoredTicks > 60;
            bool flag = lastFlag;
            if (executeBuildUpdatePreviews)
            {

                __instance.DetermineBuildPreviews();
                flag = __instance.CheckBuildConditions();
                ignoredTicks = 0;
            } else
            {
                ignoredTicks++;
            }
            __instance.UpdatePreviews();
            __instance.UpdateGizmos();
            __instance.previewGizmoOn = false;
            if (flag)
            {
                __instance.CreatePrebuilds();
            }

            lastFlag = flag;
            return false;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static void DetermineBuildPreviews_Postfix(ref PlayerAction_Build __instance)
        {
            if (IsMultiBuildAvailable() && multiBuildEnabled && startPos != Vector3.zero)
            {
                __instance.ClearBuildPreviews();

                if (__instance.previewPose.position == Vector3.zero)
                {
                    return;
                }

                __instance.previewPose.position = Vector3.zero;
                __instance.previewPose.rotation = Quaternion.identity;

                int path = 0;
                Vector3[] snaps = new Vector3[1024];
                var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(startPos, __instance.groundSnappedPos, ref path, snaps);

                var inversePreviewRot = Quaternion.Inverse(__instance.previewPose.rotation);

                //Debug.Log("------");

                ActivateColliders(ref __instance.nearcdLogic, ref snaps, snappedPointCount);

                ColliderData colliderData = __instance.handItem.prefabDesc.buildColliders[0];

                var minStep = (int)Math.Floor(2f * Math.Min(colliderData.ext.x, colliderData.ext.z));


                var desc = __instance.handPrefabDesc;
                ColliderData lastCollider = desc.buildCollider;
                Vector3 lastPos = Vector3.zero;

                for (int s = 0; s < snappedPointCount; s ++)
                {
                    var pos = snaps[s];
                    var rot = Maths.SphericalRotation(snaps[s], __instance.yaw);
                    ColliderData collider = desc.buildCollider;
                    collider.pos = pos + rot * collider.pos;
                    collider.q = rot * collider.q;


                    if(s>1)
                    {
                        var distance = Vector3.Distance(lastPos, pos);

                        // wind turbines
                        if (desc.windForcedPower && distance < 110.25f) continue;

                        // ray receivers
                        if (desc.gammaRayReceiver && distance < 110.25f) continue;

                        // logistic stations
                        if (desc.isStation && distance < (desc.isStellarStation ? 29f : 15f)) continue;


                        if (desc.isEjector && distance < 110.25f) continue;

                        if(desc.)
                    }


                    var bp = BuildPreview.CreateSingle(__instance.handItem, __instance.handPrefabDesc, true);
                    bp.ResetInfos();
                    bp.item = __instance.handItem;
                    bp.desc = desc;
                    bp.recipeId = __instance.copyRecipeId;
                    bp.filterId = __instance.copyFilterId;
                    bp.recipeId = __instance.copyRecipeId;
                    bp.filterId = __instance.copyFilterId;

                    bp.lpos = pos;
                    bp.lrot = rot;

                    __instance.AddBuildPreview(bp);

                    lastCollider = collider;
                    lastPos = pos;




                }

            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "UpdatePreviews")]
        public static bool UpdatePreviews_HarmonyPrefix(ref PlayerAction_Build __instance)
        {
            return executeBuildUpdatePreviews;
        }

        public bool Collides(ColliderData c1, ColliderData c2)
        {
            return true;
        }

        public static void ActivateColliders(ref NearColliderLogic nearCdLogic, ref Vector3[] snaps, int snappedPointCount)
        {
            for (int s = 0; s < snappedPointCount; s += 4)
            {
                nearCdLogic.activeColHashCount = 0;
                var center = snaps[s];

                Vector3 vector = Vector3.Cross(center, center - GameMain.mainPlayer.position).normalized * (5f);
                Vector3 vector2 = Vector3.Cross(vector, center).normalized * (5f);

                nearCdLogic.MarkActivePos(center);
                nearCdLogic.MarkActivePos(center + vector);
                nearCdLogic.MarkActivePos(center - vector);
                nearCdLogic.MarkActivePos(center + vector2);
                nearCdLogic.MarkActivePos(center - vector2);
                nearCdLogic.MarkActivePos(center + vector + vector2);
                nearCdLogic.MarkActivePos(center - vector + vector2);
                nearCdLogic.MarkActivePos(center + vector - vector2);
                nearCdLogic.MarkActivePos(center - vector - vector2);

                if (nearCdLogic.activeColHashCount > 0)
                {
                    for (int i = 0; i < nearCdLogic.activeColHashCount; i++)
                    {
                        int num2 = nearCdLogic.activeColHashes[i];
                        ColliderData[] colliderPool = nearCdLogic.colChunks[num2].colliderPool;
                        for (int j = 1; j < nearCdLogic.colChunks[num2].cursor; j++)
                        {
                            if (colliderPool[j].idType != 0)
                            {
                                if ((colliderPool[j].pos - center).sqrMagnitude <= 25f * 4f + colliderPool[j].ext.sqrMagnitude)
                                {
                                    if (colliderPool[j].usage != EColliderUsage.Physics || colliderPool[j].objType != EObjectType.Entity)
                                    {
                                        int num3 = num2 << 20 | j;
                                        if (nearCdLogic.colliderObjs.ContainsKey(num3))
                                        {
                                            nearCdLogic.colliderObjs[num3].live = true;
                                        }
                                        else
                                        {
                                            nearCdLogic.colliderObjs[num3] = new ColliderObject(num3, colliderPool[j]);
                                        }
                                    }
                                }
                            }
                        }
                    }


                }
            }

        }
    }
}