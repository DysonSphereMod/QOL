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

        const int MAX_IGNORED_TICKS = 60;

        public static List<UIKeyTipNode> allTips;
        public static Dictionary<String, UIKeyTipNode> tooltips = new Dictionary<String, UIKeyTipNode>();
        public static bool multiBuildEnabled = false;
        public static bool multiBuildPossible = true;
        public static Vector3 startPos = Vector3.zero;

        private static int lastCmdMode = 0;
        public static bool lastFlag;
        public static string lastCursorText;
        public static bool lastCursorWarning;
        private static Vector3 lastPosition = Vector3.zero;

        private static bool executeBuildUpdatePreviews = true;

        private static int ignoredTicks = 0;
        private static int spacing = 0;
        private static int path = 0;

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

            var isRunning = IsMultiBuildRunning();

            if ((Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && isRunning)
            {
                spacing++;
                ignoredTicks = MAX_IGNORED_TICKS;
            }

            if ((Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isRunning && spacing > 0)
            {
                spacing--;
                ignoredTicks = MAX_IGNORED_TICKS;
            }
            if (Input.GetKeyUp(KeyCode.Z) && isRunning)
            {
                path = 1 - path;
                ignoredTicks = MAX_IGNORED_TICKS;
            }
        }

        public static bool IsMultiBuildAvailable()
        {
            return UIGame.viewMode == EViewMode.Build && GameMain.mainPlayer.controller.cmd.mode == 1 && multiBuildPossible;
        }

        public static bool IsMultiBuildRunning()
        {
            return IsMultiBuildAvailable() && multiBuildEnabled && startPos != Vector3.zero;
        }

        public static void ResetMultiBuild()
        {
            spacing = 0;
            path = 0;
            ignoredTicks = 0;
            multiBuildEnabled = false;
            startPos = Vector3.zero;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerController), "UpdateCommandState")]
        public static void UpdateCommandState_Prefix(PlayerController __instance)
        {
            if (__instance.cmd.mode != lastCmdMode)
            {
                multiBuildEnabled = false;
                startPos = Vector3.zero;
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
                tooltips.Add("rotate-path", __instance.RegisterTip("Z", "Rotate build path"));
            }
            tooltips["toggle-build"].desired = IsMultiBuildAvailable();
            tooltips["rotate-path"].desired = tooltips["decrease-spacing"].desired = tooltips["increase-spacing"].desired = IsMultiBuildRunning();
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(UIGeneralTips), "_OnUpdate")]
        public static void UIGeneralTips__OnUpdate_Postfix(ref Text ___modeText)
        {
            if (IsMultiBuildAvailable() && multiBuildEnabled)
            {
                ___modeText.text += $"\nMultiBuild [{(startPos == Vector3.zero ? "START" : "END")}]";

                if (spacing > 0)
                {
                    ___modeText.text += $" - Spacing {spacing}";
                }
            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
        public static bool CreatePrebuilds_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0
                && IsMultiBuildAvailable() && multiBuildEnabled && !__instance.multiLevelCovering)
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
            if (__instance.handPrefabDesc == null ||
                __instance.handPrefabDesc.minerType != EMinerType.None ||
                __instance.player.planetData.type == EPlanetType.Gas
                )
            {
                multiBuildPossible = false;
            } else
            {
                multiBuildPossible = true;
            }

            // As multibuild increase calculation exponentially (collision and rendering must be performed for every entity), we hijack the BuildMainLogic
            // and execute the relevant submethods only when needed
            executeBuildUpdatePreviews = true;
            if (IsMultiBuildRunning())
            {
                if (lastPosition != __instance.groundSnappedPos)
                {
                    lastPosition = __instance.groundSnappedPos;
                    executeBuildUpdatePreviews = true;
                }
                else
                {
                    executeBuildUpdatePreviews = false;
                }
            }
            else
            {
                lastPosition = Vector3.zero;
            }

            // Run the preview methods if we have changed position, if we have received a relevant keyboard input or in any case every MAX_IGNORED_TICKS ticks.
            executeBuildUpdatePreviews = executeBuildUpdatePreviews || VFInput._rotate || VFInput._counterRotate || ignoredTicks >= MAX_IGNORED_TICKS;

            bool flag;
            if (executeBuildUpdatePreviews)
            {
                __instance.DetermineBuildPreviews();
                flag = __instance.CheckBuildConditions();
                __instance.UpdatePreviews();
                __instance.UpdateGizmos();

                lastCursorText = __instance.cursorText;
                lastCursorWarning = __instance.cursorWarning;
                lastFlag = flag;

                ignoredTicks = 0;
            }
            else
            {
                __instance.cursorText = lastCursorText;
                __instance.cursorWarning = lastCursorWarning;
                flag = lastFlag;
                ignoredTicks++;
            }

            if (flag)
            {
                __instance.CreatePrebuilds();
            }


            return false;
        }

        [HarmonyPostfix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static void DetermineBuildPreviews_Postfix(ref PlayerAction_Build __instance)
        {
            if (IsMultiBuildRunning())
            {
                __instance.ClearBuildPreviews();

                if (__instance.previewPose.position == Vector3.zero)
                {
                    return;
                }

                __instance.previewPose.position = Vector3.zero;
                __instance.previewPose.rotation = Quaternion.identity;

                int snapPath = path;
                Vector3[] snaps = new Vector3[1024];

                var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(startPos, __instance.groundSnappedPos, ref snapPath, snaps);

                var desc = __instance.handPrefabDesc;
                Collider[] colliders = new Collider[desc.buildColliders.Length];
                Vector3 previousPos = Vector3.zero;

                var usedSnaps = new List<Vector3>(10);

                for (int s = 0; s < snappedPointCount - spacing; s++)
                {
                    var pos = snaps[s];
                    var rot = Maths.SphericalRotation(snaps[s], __instance.yaw);

                    if (s > 0)
                    {
                        var sqrDistance = (previousPos - pos).sqrMagnitude;

                        // wind turbines
                        if (desc.windForcedPower && sqrDistance < 110.25f) continue;

                        // ray receivers
                        if (desc.gammaRayReceiver && sqrDistance < 110.25f) continue;

                        // logistic stations
                        if (desc.isStation && sqrDistance < (desc.isStellarStation ? 841f : 225f)) continue;

                        if (desc.isEjector && sqrDistance < 110.25f) continue;

                        if (desc.hasBuildCollider)
                        {
                            var foundCollision = false;
                            for (var j = 0; j < desc.buildColliders.Length && !foundCollision; j++)
                            {
                                var colliderData = desc.buildColliders[j];
                                colliderData.pos = pos + rot * colliderData.pos;
                                colliderData.q = rot * colliderData.q;
                                // check only collision with layer 27 (the layer used by the our own building colliders for the previously 'placed' building)
                                foundCollision = Physics.CheckBox(colliderData.pos, colliderData.ext, colliderData.q, 134217728, QueryTriggerInteraction.Collide);
                            }

                            if (foundCollision) continue;
                        }
                    }

                    if (s > 0 && spacing > 0)
                    {
                        s += spacing;
                        pos = snaps[s];
                        rot = Maths.SphericalRotation(snaps[s], __instance.yaw);
                    }

                    previousPos = pos;
                    usedSnaps.Add(pos);

                    var bp = BuildPreview.CreateSingle(__instance.handItem, __instance.handPrefabDesc, true);
                    bp.ResetInfos();
                    bp.desc = desc;
                    bp.lpos = pos;
                    bp.lrot = rot;
                    bp.item = __instance.handItem;
                    bp.recipeId = __instance.copyRecipeId;
                    bp.filterId = __instance.copyFilterId;

                    if (desc.hasBuildCollider)
                    {
                        for (var j = 0; j < desc.buildColliders.Length; j++)
                        {
                            // create temporary collider entities for the latest 'positioned' building
                            if (colliders[j] != null)
                            {
                                ColliderPool.PutCollider(colliders[j]);
                            }

                            var colliderData = desc.buildColliders[j];
                            colliderData.pos = pos + rot * colliderData.pos;
                            colliderData.q = rot * colliderData.q;
                            colliders[j] = ColliderPool.TakeCollider(colliderData);
                            colliders[j].gameObject.layer = 27;

                        }
                    }

                    __instance.AddBuildPreview(bp);

                }

                foreach (var collider in colliders)
                {
                    ColliderPool.PutCollider(collider);
                }


                ActivateColliders(ref __instance.nearcdLogic, usedSnaps);

            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "UpdatePreviews")]
        public static bool UpdatePreviews_HarmonyPrefix(ref PlayerAction_Build __instance)
        {
            return executeBuildUpdatePreviews;
        }

        public static void ActivateColliders(ref NearColliderLogic nearCdLogic, List<Vector3> snaps)
        {
            for (int s = 0; s < snaps.Count; s ++)
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