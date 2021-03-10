using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    internal class PlayerAction_Build_Patch
    {
        public static bool lastFlag;
        public static string lastCursorText;
        public static bool lastCursorWarning;
        public static Vector3 lastPosition = Vector3.zero;
        public static float lastYaw = 0f;
        public static int lastPath = 0;

        public static bool executeBuildUpdatePreviews = true;

        public static int ignoredTicks = 0;
        public static int path = 0;

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
        public static bool CreatePrebuilds_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0
                && MultiBuild.IsMultiBuildEnabled() && !__instance.multiLevelCovering)
            {
                if (MultiBuild.startPos == Vector3.zero)
                {
                    MultiBuild.startPos = __instance.groundSnappedPos;
                    return false;
                }
                else
                {
                    MultiBuild.startPos = Vector3.zero;
                    return true;
                }
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "BuildMainLogic")]
        public static bool BuildMainLogic_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.handPrefabDesc == null ||
                __instance.handPrefabDesc.minerType != EMinerType.None ||
                __instance.player.planetData.type == EPlanetType.Gas
                )
            {
                MultiBuild.multiBuildPossible = false;
            }
            else
            {
                MultiBuild.multiBuildPossible = true;
            }

            if (MultiBuild.itemSpecificSpacing.Value && __instance.handItem != null && MultiBuild.spacingIndex != __instance.handItem.ID)
            {
                MultiBuild.spacingIndex = __instance.handItem.ID;
                if (!MultiBuild.spacingStore.ContainsKey(MultiBuild.spacingIndex))
                {
                    MultiBuild.spacingStore[MultiBuild.spacingIndex] = 0;
                }
            }

            // As multibuild increase calculation exponentially (collision and rendering must be performed for every entity), we hijack the BuildMainLogic
            // and execute the relevant submethods only when needed
            executeBuildUpdatePreviews = true;
            /* if (MultiBuild.IsMultiBuildRunning())
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
             }*/

            // Run the preview methods if we have changed position, if we have received a relevant keyboard input or in any case every MAX_IGNORED_TICKS ticks.
            executeBuildUpdatePreviews = true; // executeBuildUpdatePreviews || VFInput._rotate || VFInput._counterRotate || ignoredTicks >= MultiBuild.MAX_IGNORED_TICKS;

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

                if (__instance.waitConfirm && VFInput._buildConfirm.onDown)
                {
                    __instance.ClearBuildPreviews();
                    ignoredTicks = MultiBuild.MAX_IGNORED_TICKS;
                }
            }

            return false;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static bool DetermineBuildPreviews_Prefix(ref PlayerAction_Build __instance)
        {
            var runOriginal = true;

            if (__instance.controller.cmd.mode == 1 && __instance.player.planetData.type != EPlanetType.Gas && __instance.cursorValid)
            {
                if (__instance.handPrefabDesc != null && __instance.handPrefabDesc.minerType != EMinerType.None)
                {
                    return true;
                }
                __instance.waitConfirm = __instance.cursorValid;
                __instance.multiLevelCovering = false;
                if (__instance.handPrefabDesc != null && __instance.handPrefabDesc.multiLevel)
                {
                    int objectProtoId = __instance.GetObjectProtoId(__instance.castObjId);
                    if (objectProtoId == __instance.handItem.ID)
                    {
                        __instance.multiLevelCovering = true;
                    }
                }
                if (__instance.multiLevelCovering && !MultiBuild.IsMultiBuildRunning())
                {
                    return true;
                }

                // full hijacking of DetermineBuildPreviews
                runOriginal = false;

                if (VFInput._switchSplitter.onDown)
                {
                    __instance.modelOffset++;
                }

                if (VFInput._rotate.onDown)
                {
                    __instance.yaw += 90f;
                    __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                    __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                }
                if (VFInput._counterRotate.onDown)
                {
                    __instance.yaw -= 90f;
                    __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                    __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                }
                __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;

                __instance.previewPose.position = Vector3.zero;
                __instance.previewPose.rotation = Quaternion.identity;

                //__instance.previewPose.position = __instance.cursorTarget;
                //__instance.previewPose.rotation = Maths.SphericalRotation(__instance.previewPose.position, __instance.yaw);

                var inversePreviewRot = Quaternion.Inverse(__instance.previewPose.rotation);
                if (BlueprintManager.data.copiedBuildings.Count == 0)
                {
                    BlueprintManager.data.copiedBuildings.Add(0, new BuildingCopy()
                    {
                        itemProto = __instance.handItem,

                        recipeId = __instance.copyRecipeId
                    });
                }

                if (lastPosition == __instance.groundSnappedPos && lastYaw == __instance.yaw && path == lastPath)
                {
                    return false;
                }
                lastPosition = __instance.groundSnappedPos;
                lastYaw = __instance.yaw;
                lastPath = path;

                List<BuildPreview> previews = new List<BuildPreview>();
                var absolutePositions = new List<Vector3>(10);
                if (BlueprintManager.data.copiedBuildings.Count == 1 && MultiBuild.IsMultiBuildRunning())
                {
                    var building = BlueprintManager.data.copiedBuildings[0];

                    int snapPath = path;
                    Vector3[] snaps = new Vector3[1024];

                    var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(MultiBuild.startPos, __instance.groundSnappedPos, ref snapPath, snaps);

                    var desc = building.itemProto.prefabDesc;
                    Collider[] colliders = new Collider[desc.buildColliders.Length];
                    Vector3 previousPos = Vector3.zero;

                    var maxSnaps = Math.Max(1, snappedPointCount - MultiBuild.spacingStore[MultiBuild.spacingIndex]);

                    for (int s = 0; s < maxSnaps; s++)
                    {
                        var pos = snaps[s];
                        var rot = Maths.SphericalRotation(snaps[s], __instance.yaw);

                        if (s > 0)
                        {
                            var sqrDistance = (previousPos - pos).sqrMagnitude;

                            // power towers
                            if (desc.isPowerNode && !desc.isAccumulator && sqrDistance < 12.25f) continue;

                            // wind turbines
                            if (desc.windForcedPower && sqrDistance < 110.25f) continue;

                            // ray receivers
                            if (desc.gammaRayReceiver && sqrDistance < 110.25f) continue;

                            // logistic stations
                            if (desc.isStation && sqrDistance < (desc.isStellarStation ? 841f : 225f)) continue;

                            // ejector
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

                        if (s > 0 && MultiBuild.spacingStore[MultiBuild.spacingIndex] > 0)
                        {
                            s += MultiBuild.spacingStore[MultiBuild.spacingIndex];
                            pos = snaps[s];
                            rot = Maths.SphericalRotation(snaps[s], __instance.yaw);
                        }

                        previousPos = pos;
                        absolutePositions.Add(pos);

                        var bp = BuildPreview.CreateSingle(building.itemProto, building.itemProto.prefabDesc, true);
                        bp.ResetInfos();
                        bp.desc = building.itemProto.prefabDesc;
                        bp.item = building.itemProto;
                        bp.lpos = pos;
                        bp.lrot = rot;
                        bp.recipeId = building.recipeId;

                        //pose.position - this.previewPose.position =  this.previewPose.rotation * buildPreview.lpos;
                        //pose.rotation = this.previewPose.rotation * buildPreview.lrot;
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

                        previews.Add(bp);
                    }

                    foreach (var collider in colliders)
                    {
                        if (collider != null)
                        {
                            ColliderPool.PutCollider(collider);
                        }
                    }
                }
                else
                {
                    previews = BlueprintManager.toBuildPreviews(__instance.groundSnappedPos, __instance.yaw, out absolutePositions);
                }

                ActivateColliders(ref __instance.nearcdLogic, absolutePositions);

                // synch previews
                for (var i = 0; i < previews.Count; i++)
                {
                    if (i >= __instance.buildPreviews.Count)
                    {
                        __instance.AddBuildPreview(previews[i]);
                    }
                    else
                    {
                        var original = __instance.buildPreviews[i];
                        var updated = previews[i];
                        if (original.desc != updated.desc || original.item != updated.item)
                        {
                            original.ResetInfos();
                            original.desc = updated.desc;
                            original.item = updated.item;
                        }
                        original.recipeId = updated.recipeId;
                        original.filterId = updated.filterId;
                        original.condition = EBuildCondition.Ok;

                        original.lpos = updated.lpos;
                        original.lrot = updated.lrot;

                        original.lpos2 = updated.lpos2;
                        original.lrot2 = updated.lrot2;
                    }
                }

                if (__instance.buildPreviews.Count > previews.Count)
                {
                    var toRemove = __instance.buildPreviews.Count - previews.Count;

                    for (var i = 0; i < toRemove; i++)
                    {
                        __instance.RemoveBuildPreview(previews.Count);
                    }
                }
            }

            return runOriginal;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "UpdatePreviews")]
        public static bool UpdatePreviews_Prefix(ref PlayerAction_Build __instance)
        {
            return executeBuildUpdatePreviews;
        }

        public static void ActivateColliders(ref NearColliderLogic nearCdLogic, List<Vector3> positions)
        {
            for (int s = 0; s < positions.Count; s++)
            {
                nearCdLogic.activeColHashCount = 0;
                var center = positions[s];

                //Vector3 vector = Vector3.Cross(center, center - GameMain.mainPlayer.position).normalized * (5f);
                //Vector3 vector2 = Vector3.Cross(vector, center).normalized * (5f);

                nearCdLogic.MarkActivePos(center);
                /* nearCdLogic.MarkActivePos(center + vector);
                 nearCdLogic.MarkActivePos(center - vector);
                 nearCdLogic.MarkActivePos(center + vector2);
                 nearCdLogic.MarkActivePos(center - vector2);
                 nearCdLogic.MarkActivePos(center + vector + vector2);
                 nearCdLogic.MarkActivePos(center - vector + vector2);
                 nearCdLogic.MarkActivePos(center + vector - vector2);
                 nearCdLogic.MarkActivePos(center - vector - vector2);*/

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

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "SetCopyInfo")]
        public static void SetCopyInfo_Postfix(ref PlayerAction_Build __instance, int objectId, int protoId)
        {
            BlueprintManager.Reset();
            if (objectId < 0)
                return;

            var copiedAssembler = BlueprintManager.copyAssembler(objectId);
            // Set the current build rotation to the copied building rotation
            Quaternion zeroRot = Maths.SphericalRotation(copiedAssembler.originalPos, 0f);
            float yaw = Vector3.SignedAngle(zeroRot.Forward(), copiedAssembler.originalRot.Forward(), zeroRot.Up());

            __instance.yaw = yaw;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "CheckBuildConditions")]
        public static void CheckBuildConditions_Postfix(PlayerAction_Build __instance, ref bool __result)
        {
            if (BlueprintManager.data.copiedInserters.Count + BlueprintManager.data.copiedBelts.Count + BlueprintManager.data.copiedBuildings.Count > 0)
            {
                var flag = true;
                for (int i = 0; i < __instance.buildPreviews.Count; i++)
                {
                    BuildPreview buildPreview = __instance.buildPreviews[i];

                    if (buildPreview.condition == EBuildCondition.OutOfReach)
                    {
                        buildPreview.condition = EBuildCondition.Ok;
                    }
                    if (buildPreview.desc.isInserter && (
                        buildPreview.condition == EBuildCondition.TooFar ||
                        buildPreview.condition == EBuildCondition.TooClose
                        ))
                    {
                        buildPreview.condition = EBuildCondition.Ok;
                    }

                    if (buildPreview.condition != EBuildCondition.Ok)
                    {
                        flag = false;
                    }
                }

                if (!__result && flag)
                {
                    UICursor.SetCursor(ECursor.Default);
                    __instance.cursorText = __instance.prepareCursorText;
                    __instance.prepareCursorText = string.Empty;
                    __instance.cursorWarning = false;
                }

                __result = flag;
            }
        }

        private static Color COPY_GIZMO_COLOR = new Color(1f, 1f, 1f, 1f);
        private static CircleGizmo circleGizmo;
        private static int[] _nearObjectIds = new int[4096];

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "GameTick")]
        public static void GameTick(PlayerAction_Build __instance)
        {
            if (VFInput.shift && __instance.controller.cmd.mode == 0)
            {
                if (circleGizmo == null)
                {
                    circleGizmo = CircleGizmo.Create(1, __instance.groundTestPos, 10);

                    circleGizmo.fadeOutScale = circleGizmo.fadeInScale = 1.8f;
                    circleGizmo.fadeOutTime = circleGizmo.fadeInTime = 0.15f;
                    circleGizmo.color = COPY_GIZMO_COLOR;
                    circleGizmo.autoRefresh = true;
                    circleGizmo.Open();
                }

                circleGizmo.position = __instance.groundTestPos;
                circleGizmo.radius = 1.2f * 10;

                if (VFInput._buildConfirm.onDown)
                {
                    BlueprintManager.Reset();
                }
                if (VFInput._buildConfirm.pressing)
                {
                    int found = __instance.nearcdLogic.GetBuildingsInAreaNonAlloc(__instance.groundTestPos, 10, _nearObjectIds);

                    for (int i = 0; i < found; i++)
                    {
                        BlueprintManager.copyAssembler(_nearObjectIds[i]);
                    }

                    for (int i = 0; i < found; i++)
                    {
                        BlueprintManager.copyBelt(_nearObjectIds[i]);
                    }
                }
                if (VFInput._buildConfirm.onUp)
                {
                    if (BlueprintManager.data.copiedBuildings.Count > 0)
                    {
                        __instance.player.SetHandItems(BlueprintManager.data.copiedBuildings.First().Value.itemProto.ID, 0, 0);
                        __instance.controller.cmd.type = ECommand.Build;
                        __instance.controller.cmd.mode = 1;
                    }

                    Debug.Log($"blueprint size: {BlueprintManager.data.export().Length}");

                   
                }
            }
            else
            {
                if (circleGizmo != null)
                {
                    circleGizmo.Close();
                    circleGizmo = null;
                }
            }
        }

        
      
    }
}