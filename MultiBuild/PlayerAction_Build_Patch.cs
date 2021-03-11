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
    internal class SorterReconnection
    {
        public int objId = 0;
        public int belt1 = 0;
        public int belt2 = 0;
    }
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

        public static Dictionary<int, SorterReconnection> SortersToFix = new Dictionary<int, SorterReconnection>();

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlanetFactory), "WriteObjectConn")]
        public static void WriteObjectConn_Prefix(ref PlanetFactory __instance, int objId, int slot, bool isOutput, int otherObjId, ref int otherSlot)
        {
            if (otherSlot == -1 && otherObjId < 0)
            {
                for (int i = 4; i < 12; i++)
                {
                    if (__instance.prebuildConnPool[-otherObjId * 16 + i] == 0)
                    {
                        otherSlot = i;
                        break;
                    }
                }
            }
        }

        
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

/*            
        if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0)
        {
            __instance.tmp_links.Clear();
            foreach (BuildPreview buildPreview in __instance.buildPreviews)
            {
                if (buildPreview.isConnNode)
                {
                    buildPreview.lrot = Maths.SphericalRotation(buildPreview.lpos, 0f);
                }
                PrebuildData prebuild = default(PrebuildData);
                prebuild.protoId = (short)buildPreview.item.ID;
                prebuild.modelIndex = (short)buildPreview.desc.modelIndex;
                prebuild.pos = __instance.previewPose.position + __instance.previewPose.rotation * buildPreview.lpos;
                prebuild.pos2 = __instance.previewPose.position + __instance.previewPose.rotation * buildPreview.lpos2;
                prebuild.rot = __instance.previewPose.rotation * buildPreview.lrot;
                prebuild.rot2 = __instance.previewPose.rotation * buildPreview.lrot2;
                prebuild.pickOffset = (short)buildPreview.inputOffset;
                prebuild.insertOffset = (short)buildPreview.outputOffset;
                prebuild.recipeId = buildPreview.recipeId;
                prebuild.filterId = buildPreview.filterId;
                prebuild.InitRefArray(buildPreview.refCount);
                for (int i = 0; i < buildPreview.refCount; i++)
                {
                    prebuild.refArr[i] = buildPreview.refArr[i];
                }
                bool flag = true;
                if (buildPreview.coverObjId == 0 || buildPreview.willCover)
                {
                    int id = buildPreview.item.ID;
                    int num = 1;
                    if (__instance.player.inhandItemId == id && __instance.player.inhandItemCount > 0)
                    {
                        __instance.player.UseHandItems(1);
                    }
                    else
                    {
                        __instance.player.package.TakeTailItems(ref id, ref num, false);
                    }
                    flag = (num == 1);
                }
                if (flag)
                {
                    if (buildPreview.coverObjId == 0)
                    {
                        buildPreview.objId = -__instance.factory.AddPrebuildDataWithComponents(prebuild);
                    }
                    else if (buildPreview.willCover)
                    {
                        int coverObjId = buildPreview.coverObjId;
                        bool flag2 = __instance.ObjectIsBelt(coverObjId);
                        if (flag2)
                        {
                            for (int j = 0; j < 4; j++)
                            {
                                bool flag3;
                                int num2;
                                int num3;
                                __instance.factory.ReadObjectConn(coverObjId, j, out flag3, out num2, out num3);
                                int num4 = num2;
                                if (num4 != 0 && __instance.ObjectIsBelt(num4))
                                {
                                    bool flag4 = false;
                                    for (int k = 0; k < 2; k++)
                                    {
                                        __instance.factory.ReadObjectConn(num4, k, out flag3, out num2, out num3);
                                        if (num2 != 0)
                                        {
                                            bool flag5 = __instance.ObjectIsBelt(num2);
                                            bool flag6 = __instance.ObjectIsInserter(num2);
                                            if (!flag5 && !flag6)
                                            {
                                                flag4 = true;
                                                break;
                                            }
                                        }
                                    }
                                    if (flag4)
                                    {
                                        __instance.tmp_links.Add(num4);
                                    }
                                }
                            }
                        }
                        if (buildPreview.coverObjId > 0)
                        {
                            Array.Copy(__instance.factory.entityConnPool, buildPreview.coverObjId * 16, __instance.tmp_conn, 0, 16);
                            for (int l = 0; l < 16; l++)
                            {
                                bool flag7;
                                int num5;
                                int otherSlotId;
                                __instance.factory.ReadObjectConn(buildPreview.coverObjId, l, out flag7, out num5, out otherSlotId);
                                if (num5 > 0)
                                {
                                    __instance.factory.ApplyEntityDisconnection(num5, buildPreview.coverObjId, otherSlotId, l);
                                }
                            }
                            Array.Clear(__instance.factory.entityConnPool, buildPreview.coverObjId * 16, 16);
                        }
                        else
                        {
                            Array.Copy(__instance.factory.prebuildConnPool, -buildPreview.coverObjId * 16, __instance.tmp_conn, 0, 16);
                            Array.Clear(__instance.factory.prebuildConnPool, -buildPreview.coverObjId * 16, 16);
                        }
                        buildPreview.objId = -__instance.factory.AddPrebuildDataWithComponents(prebuild);
                        if (buildPreview.objId > 0)
                        {
                            Array.Copy(__instance.tmp_conn, 0, __instance.factory.entityConnPool, buildPreview.objId * 16, 16);
                        }
                        else
                        {
                            Array.Copy(__instance.tmp_conn, 0, __instance.factory.prebuildConnPool, -buildPreview.objId * 16, 16);
                        }
                        __instance.factory.EnsureObjectConn(buildPreview.objId);
                    }
                    else
                    {
                        buildPreview.objId = buildPreview.coverObjId;
                    }
                }
                else
                {
                    Assert.CannotBeReached();
                    UIRealtimeTip.Popup("物品不足".Translate(), true, 1);
                }
            }
            foreach (BuildPreview buildPreview2 in __instance.buildPreviews)
            {
                if (buildPreview2.objId != 0)
                {

                    if (buildPreview2.outputObjId != 0)
                    {
                        //Debug.Log($"a = {buildPreview2.outputObjId} {buildPreview2.item.name}");

                        __instance.factory.WriteObjectConn(buildPreview2.objId, buildPreview2.outputFromSlot, true, buildPreview2.outputObjId, buildPreview2.outputToSlot);
                    }
                    else if (buildPreview2.output != null)
                    {
                        //Debug.Log($"b = {buildPreview2.output.objId} {buildPreview2.item.name}");
                        __instance.factory.WriteObjectConn(buildPreview2.objId, buildPreview2.outputFromSlot, true, buildPreview2.output.objId, buildPreview2.outputToSlot);
                    }
                    if (buildPreview2.inputObjId != 0)
                    {
                        //Debug.Log($"c = {buildPreview2.inputObjId} {buildPreview2.item.name}");
                        __instance.factory.WriteObjectConn(buildPreview2.objId, buildPreview2.inputToSlot, false, buildPreview2.inputObjId, buildPreview2.inputFromSlot);
                    }
                    else if (buildPreview2.input != null)
                    {
                        //Debug.Log($"d = {buildPreview2.input.objId} {buildPreview2.item.name}");
                        __instance.factory.WriteObjectConn(buildPreview2.objId, buildPreview2.inputToSlot, false, buildPreview2.input.objId, buildPreview2.inputFromSlot);
                    }
                    Debug.Log(buildPreview2.item.name);
                    Debug.Log($"EEE = {(buildPreview2.output != null ? buildPreview2.output.objId : 0)} / {buildPreview2.outputObjId}   {buildPreview2.outputFromSlot} {buildPreview2.outputToSlot}");
                    Debug.Log($"EEE = {(buildPreview2.input != null ? buildPreview2.input.objId : 0)} / {buildPreview2.outputObjId}   {buildPreview2.inputFromSlot} {buildPreview2.inputToSlot}");
                }
            }
            foreach (BuildPreview buildPreview3 in __instance.buildPreviews)
            {
                if (buildPreview3.coverObjId != 0 && buildPreview3.willCover && buildPreview3.objId != 0 && __instance.ObjectIsBelt(buildPreview3.objId))
                {
                    bool flag8;
                    int num6;
                    int num7;
                    __instance.factory.ReadObjectConn(buildPreview3.objId, 0, out flag8, out num6, out num7);
                    if (num6 != 0 && flag8 && __instance.ObjectIsBelt(buildPreview3.objId))
                    {
                        int num8;
                        __instance.factory.ReadObjectConn(num6, 0, out flag8, out num8, out num7);
                        if (num8 == buildPreview3.objId)
                        {
                            __instance.factory.ClearObjectConn(num6, 0);
                        }
                    }
                }
            }
            int num9 = 0;
            foreach (BuildPreview buildPreview4 in __instance.buildPreviews)
            {
                if (buildPreview4.coverObjId != 0 && buildPreview4.willCover)
                {
                    __instance.DoDestructObject(buildPreview4.coverObjId, out num9);
                }
                foreach (int objId in __instance.tmp_links)
                {
                    __instance.DoDestructObject(objId, out num9);
                }
            }

            __instance.AfterPrebuild();
        }

*/

        return false;
            
        }
/*
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "AfterPrebuild")]
        public static void AfterPrebuild_Prefix(ref PlayerAction_Build __instance)
        {
            foreach (var buildPreview in __instance.buildPreviews)
            {
                if (buildPreview.desc.isInserter)
                {
                    var belt1 = 0;
                    var belt2 = 0;
                    if (buildPreview.input != null)
                    {
                        belt1 = buildPreview.input.objId;
                    }
                    if (buildPreview.output != null)
                    {
                        belt2 = buildPreview.output.objId;
                    }
                    if (belt1 != 0 || belt2 != 0)
                    {
                        SortersToFix.Add(buildPreview.objId, new SorterReconnection
                        {
                            objId = buildPreview.objId,
                            belt1 = belt1,
                            belt2 = belt2
                        });
                    }
                }
            }
        }
*/
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
            executeBuildUpdatePreviews = true; 

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
                    previews = BlueprintManager.paste(__instance.groundSnappedPos, __instance.yaw, out absolutePositions);
                }

                ActivateColliders(ref __instance.nearcdLogic, absolutePositions);

                // synch previews
                for (var i = 0; i < previews.Count; i++)
                {
                    var updated = previews[i];
                    if (i >= __instance.buildPreviews.Count)
                    {
                        __instance.AddBuildPreview(updated);
                        continue;
                    }

                    var original = __instance.buildPreviews[i];

                    if (original.desc != updated.desc || original.item != updated.item)
                    {
                        __instance.RemoveBuildPreview(original);
                        __instance.AddBuildPreview(previews[i]);
                        continue;
                    }

                    updated.previewIndex = original.previewIndex;
                    __instance.buildPreviews[i] = updated;
                    /*         original.recipeId = updated.recipeId;
                             original.filterId = updated.filterId;
                             original.condition = EBuildCondition.Ok;

                             original.lpos = updated.lpos;
                             original.lrot = updated.lrot;

                             original.lpos2 = updated.lpos2;
                             original.lrot2 = updated.lrot2;*/
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
                circleGizmo.radius = 1.2f * 5;

                if (VFInput._buildConfirm.onDown)
                {
                    BlueprintManager.Reset();
                }
                if (VFInput._buildConfirm.pressing)
                {
                    int found = __instance.nearcdLogic.GetBuildingsInAreaNonAlloc(__instance.groundTestPos, 5, _nearObjectIds);

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