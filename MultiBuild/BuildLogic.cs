using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [HarmonyPatch]
    public class BuildLogic
    {
        public static int path = 0;
        public static Vector3 lastPosition = Vector3.zero;
        public static bool forceRecalculation = false;
        public static bool runUpdate = false;
        public static bool lastFlag;
        public static bool lastCursorWarning;
        public static string lastCursorText;

        public static Dictionary<int, BuildingCopy> toPostProcess = new Dictionary<int, BuildingCopy>();

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
        public static bool PlayerAction_Build_CreatePrebuilds_Prefix(ref PlayerAction_Build __instance)
        {
            var runOriginal = true;
            if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0
                && MultiBuild.IsMultiBuildEnabled() && !__instance.multiLevelCovering)
            {
                if (MultiBuild.startPos == Vector3.zero)
                {
                    MultiBuild.startPos = __instance.groundSnappedPos;
                    lastPosition = Vector3.zero;
                    runOriginal = false;
                }
                else
                {
                    MultiBuild.startPos = Vector3.zero;
                    runOriginal = true;
                }


            }
            if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 1)
            {
                for (var i = 0; i < __instance.buildPreviews.Count; i++)
                {
                    var bp = __instance.buildPreviews[i];
                    if (bp.desc.isInserter && ((bp.input == null && bp.inputObjId == 0) || (bp.output == null && bp.outputObjId == 0)))
                    {
                        __instance.RemoveBuildPreview(bp);
                        --i;
                    }
                }
            }

            return runOriginal;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "AfterPrebuild")]
        public static void PlayerAction_Build_AfterPrebuilds_Prefix()
        {
            foreach (var item in BlueprintManager.pastedEntities)
            {
                var buildPreview = item.Value.buildPreview;
                var sourceBuilding = item.Value.sourceBuilding;
                if (buildPreview.objId >= 0 || item.Value.sourceBuilding == null)
                {
                    continue;
                }

                if (sourceBuilding.itemProto.prefabDesc.isStation && sourceBuilding.slotFilters.Count + sourceBuilding.stationSettings.Count > 0)
                {
                    toPostProcess.Add(buildPreview.objId, item.Value.sourceBuilding);
                }
            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "NotifyBuilt")]
        public static void PlayerAction_Build_NotifyBuilt_Prefix(ref PlayerAction_Build __instance, int preObjId, int postObjId)
        {
            forceRecalculation = true;
            if (toPostProcess.TryGetValue(preObjId, out BuildingCopy sourceBuilding))
            {
                var entity = __instance.factory.entityPool[postObjId];

                if (sourceBuilding.itemProto.prefabDesc.isStation)
                {
                    var stationComponent = __instance.factory.transport.GetStationComponent(entity.stationId);
                    foreach (var settings in sourceBuilding.stationSettings)
                    {
                        __instance.factory.transport.SetStationStorage(entity.stationId, settings.index, settings.itemId, settings.max, settings.localLogic, settings.remoteLogic, GameMain.mainPlayer.package);
                    }
                    foreach (var slotFilter in sourceBuilding.slotFilters)
                    {
                        stationComponent.slots[slotFilter.slotIndex].storageIdx = slotFilter.storageIdx;
                    }
                }


                toPostProcess.Remove(preObjId);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), "CreateEntityLogicComponents")]
        public static void PlanetFactory_CreateEntityLogicComponents_Postfix(PlanetFactory __instance, int entityId, PrefabDesc desc)
        {
            // this ensure that buildings built AFTER a connected belt are correctly configured
            // the game by default already does something like this but only for belts to belts / splitter / logistic stations
            // See PlanetFactory.CreateEntityLogicComponents
            var entity = __instance.entityPool[entityId];
            for (var i = 0; i < 4; i++)
            {
                __instance.ReadObjectConn(entityId, i, out bool isOutput, out int otherId, out _);

                // ignore unbuilt or connections that are not belts
                if (otherId <= 0 || __instance.entityPool[otherId].beltId <= 0) continue;

                var beltId = __instance.entityPool[otherId].beltId;

                if (desc.isTank)
                {
                    __instance.factoryStorage.SetTankBelt(entity.tankId, beltId, i, isOutput);
                }
                if (desc.isFractionate)
                {
                    __instance.factorySystem.SetFractionateBelt(entity.fractionateId, beltId, i, isOutput);
                }
                if (desc.isPowerExchanger)
                {
                    __instance.powerSystem.SetExchangerBelt(entity.powerExcId, beltId, i, isOutput);
                }
            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "BuildMainLogic")]
        public static bool PlayerAction_Build_BuildMainLogic_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.handPrefabDesc == null ||
                __instance.handPrefabDesc.minerType != EMinerType.None ||
                __instance.player.planetData.type == EPlanetType.Gas ||
                BlueprintManager.data.copiedBuildings.Count > 1
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

            runUpdate = true;
            __instance.DetermineBuildPreviews();
            if (runUpdate)
            {
                var backupMechaBuildArea = __instance.player.mecha.buildArea;
                if (BlueprintManager.hasData)
                {
                    __instance.player.mecha.buildArea = 10000f;
                }
                lastFlag = __instance.CheckBuildConditions();
                __instance.UpdatePreviews();
                __instance.UpdateGizmos();

                __instance.player.mecha.buildArea = backupMechaBuildArea;
                lastCursorText = __instance.cursorText;
                lastCursorWarning = __instance.cursorWarning;

            }
            else
            {
                __instance.cursorText = lastCursorText;
                __instance.cursorWarning = lastCursorWarning;
            }

            if (lastFlag)
            {
                __instance.CreatePrebuilds();

                if (__instance.waitConfirm && VFInput._buildConfirm.onDown)
                {
                    __instance.ClearBuildPreviews();
                    BuildLogic.forceRecalculation = true;
                }
            }

            return false;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static bool PlayerAction_Build_DetermineBuildPreviews_Prefix(ref PlayerAction_Build __instance)
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
                if (!MultiBuild.IsMultiBuildRunning() && (__instance.multiLevelCovering || !BlueprintManager.hasData))
                {
                    return true;
                }

                // full hijacking of DetermineBuildPreviews
                runOriginal = false;

                if (VFInput._switchSplitter.onDown)
                {
                    __instance.modelOffset++;
                    forceRecalculation = true;
                }

                if (VFInput._rotate.onDown)
                {
                    __instance.yaw += 90f;
                    __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                    __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                    forceRecalculation = true;
                }
                if (VFInput._counterRotate.onDown)
                {
                    __instance.yaw -= 90f;
                    __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                    __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                    forceRecalculation = true;
                }

                __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                __instance.previewPose.position = Vector3.zero;
                __instance.previewPose.rotation = Quaternion.identity;

                if (lastPosition == __instance.groundSnappedPos && !forceRecalculation)
                {
                    // no update necessary
                    runUpdate = false;
                    return false;
                }
                lastPosition = __instance.groundSnappedPos;
                forceRecalculation = false;

                List<BuildPreview> previews = new List<BuildPreview>();

                if (MultiBuild.IsMultiBuildRunning())
                {
                    if (!BlueprintManager.hasData)
                    {
                        BlueprintManager.data.copiedBuildings.Add(new BuildingCopy()
                        {
                            itemProto = __instance.handItem,
                            recipeId = __instance.copyRecipeId,
                            modelIndex = __instance.handPrefabDesc.modelIndex
                        });
                    }
                    var building = BlueprintManager.data.copiedBuildings[0];// BlueprintManager.data.copiedBuildings.First();

                    int snapPath = path;
                    Vector3[] snaps = new Vector3[1024];

                    var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(MultiBuild.startPos, __instance.groundSnappedPos, ref snapPath, snaps);

                    var desc = BlueprintManager.GetPrefabDesc(building);
                    Collider[] colliders = new Collider[desc.buildColliders.Length];
                    Vector3 previousPos = Vector3.zero;

                    var copiesCounter = 0;
                    for (int s = 0; s < snappedPointCount; s++)
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

                        if (s > 0 && MultiBuild.spacingStore[MultiBuild.spacingIndex] > 0 && copiesCounter % MultiBuild.spacingPeriod == 0)
                        {
                            s += MultiBuild.spacingStore[MultiBuild.spacingIndex];

                            if (s >= snappedPointCount) break;
                            pos = snaps[s];
                            rot = Maths.SphericalRotation(snaps[s], __instance.yaw);
                        }

                        copiesCounter++;
                        previousPos = pos;

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

                        previews = previews.Concat(BlueprintManager.Paste(pos, __instance.yaw, MultiBuild.multiBuildInserters)).ToList();
                    }

                    if (!BlueprintManager.hasData)
                    {
                        BlueprintManager.data.copiedBuildings.RemoveAt(0);
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
                    var pasteInserters = MultiBuild.multiBuildInserters || (BlueprintManager.data.copiedBuildings.Count + BlueprintManager.data.copiedBelts.Count > 1);
                    previews = BlueprintManager.Paste(__instance.groundSnappedPos, __instance.yaw, pasteInserters);
                }

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


                    if (original.desc.modelIndex != updated.desc.modelIndex)
                    {
                        __instance.FreePreviewModel(original);
                    }
                    else
                    {
                        updated.previewIndex = original.previewIndex;
                    }

                    __instance.buildPreviews[i] = updated;
                    original.Free();
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
        public static bool PlayerAction_Build_UpdatePreviews_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.upgrading || __instance.destructing)
            {
                return true;
            }
            int graphPoints = 0;
            int pointCount = __instance.connGraph.pointCount;
            __instance.connRenderer.ClearXSigns();
            __instance.connRenderer.ClearUpgradeArrows();
            for (int i = 0; i < __instance.buildPreviews.Count; i++)
            {
                BuildPreview buildPreview = __instance.buildPreviews[i];
                if (buildPreview.needModel)
                {
                    __instance.CreatePreviewModel(buildPreview);
                    int previewIndex = buildPreview.previewIndex;
                    if (previewIndex >= 0)
                    {
                        __instance.previewRenderers[previewIndex].transform.localPosition = __instance.previewPose.position + __instance.previewPose.rotation * buildPreview.lpos;
                        __instance.previewRenderers[previewIndex].transform.localRotation = __instance.previewPose.rotation * buildPreview.lrot;
                        bool isInserter = buildPreview.desc.isInserter;
                        Material material;
                        if (isInserter)
                        {
                            bool isNotConnected = (buildPreview.input == null && buildPreview.inputObjId == 0) || (buildPreview.output == null && buildPreview.outputObjId == 0);
                            Material original = buildPreview.condition != EBuildCondition.Ok ? Configs.builtin.previewErrorMat_Inserter : (isNotConnected ? Configs.builtin.previewGizmoMat_Inserter : Configs.builtin.previewOkMat_Inserter);
                            Material existingMaterial = __instance.previewRenderers[previewIndex].sharedMaterial;

                            if (existingMaterial != null && !existingMaterial.name.StartsWith(original.name))
                            {
                                UnityEngine.Object.Destroy(existingMaterial);
                                existingMaterial = null;
                            }

                            if (existingMaterial == null)
                            {
                                material = UnityEngine.Object.Instantiate<Material>(original);
                            }
                            else
                            {
                                material = existingMaterial;
                            }

                            __instance.GetInserterT1T2(buildPreview.objId, out bool t, out bool t2);
                            if (buildPreview.outputObjId != 0 && !__instance.ObjectIsBelt(buildPreview.outputObjId) && !__instance.ObjectIsInserter(buildPreview.outputObjId))
                            {
                                t2 = true;
                            }
                            if (buildPreview.inputObjId != 0 && !__instance.ObjectIsBelt(buildPreview.inputObjId) && !__instance.ObjectIsInserter(buildPreview.inputObjId))
                            {
                                t = true;
                            }

                            material.SetVector("_Position1", __instance.Vector3BoolToVector4(Vector3.zero, t));
                            material.SetVector("_Rotation1", __instance.QuaternionToVector4(Quaternion.identity));
                            material.SetVector("_Position2", __instance.Vector3BoolToVector4(Quaternion.Inverse(buildPreview.lrot) * (buildPreview.lpos2 - buildPreview.lpos), t2));
                            material.SetVector("_Rotation2", __instance.QuaternionToVector4(Quaternion.Inverse(buildPreview.lrot) * buildPreview.lrot2));
                            __instance.previewRenderers[previewIndex].enabled = (buildPreview.condition != EBuildCondition.NeedConn);
                        }
                        else
                        {
                            __instance.previewRenderers[previewIndex].enabled = true;
                            Material original = ((buildPreview.condition != EBuildCondition.Ok) ? Configs.builtin.previewErrorMat : Configs.builtin.previewOkMat); ;

                            Material existingMaterial = __instance.previewRenderers[previewIndex].sharedMaterial;

                            if (existingMaterial != null && !existingMaterial.name.StartsWith(original.name))
                            {
                                UnityEngine.Object.Destroy(existingMaterial);
                                existingMaterial = null;
                            }

                            if (existingMaterial == null)
                            {
                                material = UnityEngine.Object.Instantiate<Material>(original);
                            }
                            else
                            {
                                material = existingMaterial;
                            }
                        }
                        __instance.previewRenderers[previewIndex].sharedMaterial = material;
                    }
                }
                else if (buildPreview.previewIndex >= 0)
                {
                    __instance.FreePreviewModel(buildPreview);
                }
                if (buildPreview.isConnNode)
                {
                    uint color = 4U;
                    if (buildPreview.condition != EBuildCondition.Ok)
                    {
                        color = 0U;
                    }
                    if (graphPoints < pointCount)
                    {
                        __instance.connGraph.points[graphPoints] = buildPreview.lpos;
                        __instance.connGraph.colors[graphPoints] = color;
                    }
                    else
                    {
                        __instance.connGraph.AddPoint(buildPreview.lpos, color);
                    }
                    graphPoints++;
                }
            }
            __instance.connGraph.SetPointCount(graphPoints);
            if (graphPoints > 0)
            {
                __instance.showConnGraph = true;
            }

            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "SetCopyInfo")]
        public static void PlayerAction_Build_SetCopyInfo_Postfix(ref PlayerAction_Build __instance, int objectId)
        {
            BlueprintManager.Reset();
            if (objectId < 0)
                return;

            var itemProto = LDB.items.Select(__instance.factory.entityPool[objectId].protoId);

            if (itemProto.prefabDesc.insertPoses.Length > 0)
            {
                var toAdd = new List<int>() { objectId };
                BlueprintManager.CopyEntities(toAdd);
                Debug.Log($"copy time {BlueprintManager.hasData} {BlueprintManager.data.copiedBuildings.Count} ");
                if (BlueprintManager.hasData)
                {
                    BlueprintManager.data.copiedBuildings[0].recipeId = __instance.copyRecipeId;
                    __instance.yaw = BlueprintManager.data.referenceYaw;
                }
            }
        }

        [HarmonyTranspiler, HarmonyPatch(typeof(PlayerAction_Build), "CheckBuildConditions")]
        public static IEnumerable<CodeInstruction> PlayerAction_Build_CheckBuildConditions_Patch(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions)
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.condition)))
                )
                .Advance(1)
                .SetInstructionAndAdvance(Transpilers.EmitDelegate<Func<BuildPreview, bool>>(buildPreview =>
                {
                    // ignore checkbuildconditions for all inserters when copy pasting as we already took care of checking for collisions
                    if (BlueprintManager.pastedEntities.Count > 0 && buildPreview.desc.isInserter)
                    {
                        // only check that we have enough items
                        if (buildPreview.coverObjId == 0 || buildPreview.willCover)
                        {
                            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;
                            int id = buildPreview.item.ID;
                            int num = 1;
                            if (actionBuild.tmpInhandId == id && actionBuild.tmpInhandCount > 0)
                            {
                                num = 1;
                                actionBuild.tmpInhandCount--;
                            }
                            else
                            {
                                actionBuild.tmpPackage.TakeTailItems(ref id, ref num, false);
                            }
                            if (num == 0)
                            {
                                buildPreview.condition = EBuildCondition.NotEnoughItem;
                            }
                        }
                        return true;
                    }
                    return false;
                }))
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_S),
                    new CodeMatch(OpCodes.Ldc_R4, 0.4f),
                    new CodeMatch(OpCodes.Bge_Un)
                 )
                .MatchForward(false,
                    new CodeMatch(OpCodes.Stfld, AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.condition)))
                )
                .Advance(-1)
                .SetInstructionAndAdvance(Transpilers.EmitDelegate<Action<BuildPreview>>(buildPreview =>
                {
                    // remove check for belt tooClose
                    if (BlueprintManager.pastedEntities.Count > 0)
                    {
                        if (buildPreview.input == null && buildPreview.output == null)
                        {
                            buildPreview.condition = EBuildCondition.TooClose;
                        }
                    }
                }))
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Nop));

            return matcher.InstructionEnumeration();
        }


        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlanetFactory), "WriteObjectConn")]
        public static void WriteObjectConn_Prefix(ref PlanetFactory __instance, int otherObjId, ref int otherSlot)
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

        public static void ActivateColliders(ref NearColliderLogic nearCdLogic, List<Vector3> positions)
        {
            for (int s = 0; s < positions.Count; s++)
            {
                nearCdLogic.activeColHashCount = 0;
                var center = positions[s];
                var sqrAreaRadius = 4f * 4f;
                nearCdLogic.MarkActivePos(center);

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
                                if ((colliderPool[j].pos - center).sqrMagnitude <= sqrAreaRadius + colliderPool[j].ext.sqrMagnitude)
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
