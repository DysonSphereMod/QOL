using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public class PastedEntity
    {
        public BuildingCopy sourceBuilding;
        public BuildPreview buildPreview;
        public Pose pose;
        public int objId;
    }

    [HarmonyPatch]
    public class BlueprintManager
    {
        public static BlueprintData previousData = new BlueprintData();
        public static BlueprintData data = new BlueprintData();
        public static bool hasData = false;

        public static Dictionary<int, PastedEntity> pastedEntities = new Dictionary<int, PastedEntity>();

        private static Vector3[] _snaps = new Vector3[1000];

        /*public static Queue<InserterPosition> currentPositionCache;
        public static Queue<InserterPosition> nextPositionCache;

        private static void SwapPositionCache()
        {
            currentPositionCache = nextPositionCache;
            nextPositionCache = new Queue<InserterPosition>();
        }*/

        public static void Reset()
        {
            if (!hasData)
            {
                return;
            }
            hasData = false;
            previousData = data;
            data =  new BlueprintData();
            pastedEntities.Clear();
            GC.Collect();
        }

        public static void Restore(BlueprintData newData = null)
        {
            hasData = true;
            var temp = data;
            data = newData ?? previousData;
            previousData = temp;
            pastedEntities.Clear();
            GC.Collect();

            EnterBuildModeAfterBp();
        }

        public static void EnterBuildModeAfterBp()
        {
            if (!hasData)
            {
                return;
            }

            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            // if no building use storage id as fake buildingId as we need something with buildmode == 1
            var firstItemProtoID = data.copiedBuildings.Count > 0 ?
                        data.copiedBuildings.First().Value.itemProto.ID :
                        2101;

            actionBuild.yaw = data.referenceYaw;
            actionBuild.player.SetHandItems(firstItemProtoID, 0, 0);
            actionBuild.controller.cmd.type = ECommand.Build;
            actionBuild.controller.cmd.mode = 1;
        }

        public static PrefabDesc GetPrefabDesc (BuildingCopy copiedBuilding)
        {
            ModelProto modelProto = LDB.models.Select(copiedBuilding.modelIndex);
            if (modelProto != null)
            {
                return modelProto.prefabDesc;
            }
            else
            {
                return copiedBuilding.itemProto.prefabDesc;
            }
        }

        public static Vector3[] GetMovesBetweenPoints(Vector3 from, Vector3 to, Quaternion inverseFromRotation)
        {
            if (from == to)
            {
                return new Vector3[0];
            }

            int path = 0;

            var snappedPointCount = GameMain.data.mainPlayer.planetData.aux.SnapLineNonAlloc(from, to, ref path, _snaps);
            Vector3 lastSnap = from;
            Vector3[] snapMoves = new Vector3[snappedPointCount];
            for (int s = 0; s < snappedPointCount; s++)
            {
                // note: reverse rotation of the delta so that rotation works
                Vector3 snapMove = inverseFromRotation * (_snaps[s] - lastSnap);
                snapMoves[s] = snapMove;
                lastSnap = _snaps[s];
            }

            return snapMoves;
        }

        public static Vector3 GetPointFromMoves(Vector3 from, Vector3[] moves, Quaternion fromRotation)
        {
            var targetPos = from;

            // Note: rotates each move relative to the rotation of the from
            for (int i = 0; i < moves.Length; i++)
                targetPos = GameMain.data.mainPlayer.planetData.aux.Snap(targetPos + fromRotation * moves[i], true, false);

            return targetPos;
        }
        
        public static BeltCopy copyBelt(int sourceEntityId)
        {
            if (data.copiedBelts.ContainsKey(sourceEntityId))
            {
                return data.copiedBelts[sourceEntityId];
            }

            var factory = GameMain.data.localPlanet.factory;
            var planetAux = GameMain.data.mainPlayer.planetData.aux;
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            var sourceEntity = factory.entityPool[sourceEntityId];

            var sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (!sourceEntityProto.prefabDesc.isBelt)
            {
                return null;
            }

            var belt = factory.cargoTraffic.beltPool[sourceEntity.beltId];

            var sourcePos = sourceEntity.pos;
            var sourceRot = sourceEntity.rot;

            var copiedBelt = new BeltCopy()
            {
                originalId = sourceEntityId,
                protoId = sourceEntityProto.ID,
                itemProto = sourceEntityProto,
                originalPos = sourcePos,
                originalRot = sourceRot,

                backInputId = factory.cargoTraffic.beltPool[belt.backInputId].entityId,
                leftInputId = factory.cargoTraffic.beltPool[belt.leftInputId].entityId,
                rightInputId = factory.cargoTraffic.beltPool[belt.rightInputId].entityId,
                outputId = factory.cargoTraffic.beltPool[belt.outputId].entityId,
            };

            bool isOutput;
            int otherId;
            int otherSlot;

            factory.ReadObjectConn(sourceEntityId, 0, out isOutput, out otherId, out otherSlot);
            if(otherId>0 && factory.entityPool[otherId].beltId == 0)
            {
                copiedBelt.connectedBuildingId = otherId;
                copiedBelt.connectedBuildingIsOutput = isOutput;
                copiedBelt.connectedBuildingSlot = otherSlot;

            }
            factory.ReadObjectConn(sourceEntityId, 1, out isOutput, out otherId, out otherSlot);
            if (otherId > 0 && factory.entityPool[otherId].beltId == 0)
            {
                copiedBelt.connectedBuildingId = otherId;
                copiedBelt.connectedBuildingIsOutput = isOutput;
                copiedBelt.connectedBuildingSlot = otherSlot;
            }

            if (data.referencePos == Vector3.zero)
            {
                data.referencePos = sourcePos;
                data.inverseReferenceRot = Quaternion.Inverse(sourceRot);
            }
            else
            {
                copiedBelt.cursorRelativePos = data.inverseReferenceRot * (copiedBelt.originalPos - data.referencePos);
                copiedBelt.movesFromReference = GetMovesBetweenPoints(data.referencePos, copiedBelt.originalPos, data.inverseReferenceRot);
            }

            data.copiedBelts.Add(copiedBelt.originalId, copiedBelt);
            hasData = true;

            return copiedBelt;
        }

        public static BuildingCopy copyBuilding(int sourceEntityId)
        {
            if (data.copiedBuildings.ContainsKey(sourceEntityId))
            {
                return data.copiedBuildings[sourceEntityId];
            }
            var factory = GameMain.data.localPlanet.factory;
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            var sourceEntity = factory.entityPool[sourceEntityId];

            var sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (sourceEntityProto.prefabDesc.isBelt || sourceEntityProto.prefabDesc.isInserter || sourceEntityProto.prefabDesc.minerType != EMinerType.None)
            {
                return null;
            }

            var sourcePos = sourceEntity.pos;
            var sourceRot = sourceEntity.rot;

            Quaternion zeroRot = Maths.SphericalRotation(sourcePos, 0f);
            float yaw = Vector3.SignedAngle(zeroRot.Forward(), sourceRot.Forward(), zeroRot.Up());

            var copiedBuilding = new BuildingCopy()
            {
                originalId = sourceEntityId,
                protoId = sourceEntityProto.ID,
                itemProto = sourceEntityProto,
                originalPos = sourcePos,
                originalRot = sourceRot,
                modelIndex = sourceEntity.modelIndex
            };


            if (sourceEntityProto.prefabDesc.isAssembler)
            {
                copiedBuilding.recipeId = factory.factorySystem.assemblerPool[sourceEntity.assemblerId].recipeId;
            }


            if (sourceEntityProto.prefabDesc.isStation)
            {
                var stationComponent = factory.transport.stationPool[sourceEntity.stationId];

                for (var i = 0; i < stationComponent.slots.Length; i++)
                {
                    Debug.Log(stationComponent.slots[i].storageIdx);
                    if(stationComponent.slots[i].storageIdx != 0)
                    {
                        copiedBuilding.slotFilters.Add(new BuildingCopy.SlotFilter()
                        {
                            slotIndex = i,
                            storageIdx = stationComponent.slots[i].storageIdx
                        });
                    }
                }

                for (var i = 0; i < stationComponent.storage.Length; i++)
                {
                    Debug.Log(stationComponent.storage[i].itemId);
                    if (stationComponent.storage[i].itemId != 0)
                    {
                        copiedBuilding.stationSettings.Add(new BuildingCopy.StationSetting()
                        {
                            index = i,
                            itemId = stationComponent.storage[i].itemId,
                            max = stationComponent.storage[i].max,
                            localLogic = stationComponent.storage[i].localLogic,
                            remoteLogic = stationComponent.storage[i].remoteLogic
                        });
                    }
                }
            }

            if(sourceEntityProto.prefabDesc.isSplitter)
            {
                var splitterComponennt = factory.cargoTraffic.splitterPool[sourceEntity.splitterId];

                // TODO: find a way to restore splitter settings 
            }

            if (data.referencePos == Vector3.zero)
            {
                data.referencePos = sourcePos;
                data.inverseReferenceRot = Quaternion.Inverse(sourceRot);
                data.referenceYaw = yaw;
            }
            else
            {
                copiedBuilding.cursorRelativePos = data.inverseReferenceRot * (copiedBuilding.originalPos - data.referencePos);
                copiedBuilding.movesFromReference = GetMovesBetweenPoints(data.referencePos, copiedBuilding.originalPos, data.inverseReferenceRot);
                copiedBuilding.cursorRelativeYaw = yaw - data.referenceYaw;
            }

            data.copiedBuildings.Add(copiedBuilding.originalId, copiedBuilding);

            // Ignore building without inserter slots
            if (sourceEntityProto.prefabDesc.insertPoses.Length > 0)
            {
                // Find connected inserters
                var inserterPool = factory.factorySystem.inserterPool;
                var entityPool = factory.entityPool;
                var prebuildPool = factory.prebuildPool;

                for (int i = 1; i < factory.factorySystem.inserterCursor; i++)
                {
                    if (inserterPool[i].id != i) continue;

                    var inserter = inserterPool[i];
                    var inserterEntity = entityPool[inserter.entityId];

                    if (data.copiedInserters.ContainsKey(inserter.entityId)) continue;

                    var pickTarget = inserter.pickTarget;
                    var insertTarget = inserter.insertTarget;

                    if (pickTarget == sourceEntityId || insertTarget == sourceEntityId)
                    {
                        ItemProto itemProto = LDB.items.Select(inserterEntity.protoId);

                        bool incoming = insertTarget == sourceEntityId;
                        var otherId = incoming ? pickTarget : insertTarget; // The belt or other building this inserter is attached to
                        Vector3 otherPos = Vector3.zero;
                        ItemProto otherProto = null;

                        if (otherId > 0)
                        {
                            otherPos = entityPool[otherId].pos;
                            otherProto = LDB.items.Select((int)entityPool[otherId].protoId);
                        }
                        else if (otherId < 0)
                        {
                            otherPos = prebuildPool[-otherId].pos;
                            otherProto = LDB.items.Select((int)entityPool[-otherId].protoId);
                        }
                        else
                        {
                            otherPos = inserter.pos2;
                            otherProto = null;
                        }

                        // Store the Grid-Snapped moves from assembler to belt/other
                        Vector3[] movesFromReference = GetMovesBetweenPoints(sourcePos, otherPos, Quaternion.Inverse(sourceRot));

                        bool otherIsBelt = otherProto == null || otherProto.prefabDesc.isBelt;

                        // Cache info for this inserter
                        InserterCopy copiedInserter = new InserterCopy
                        {
                            itemProto = itemProto,
                            protoId = itemProto.ID,
                            originalId = inserter.entityId,

                            pickTarget = pickTarget,
                            insertTarget = insertTarget,

                            referenceBuildingId = copiedBuilding.originalId,

                            incoming = incoming,

                            // rotations + deltas relative to the source building's rotation
                            rot = Quaternion.Inverse(sourceRot) * inserterEntity.rot,
                            rot2 = Quaternion.Inverse(sourceRot) * inserter.rot2,
                            posDelta = Quaternion.Inverse(sourceRot) * (inserterEntity.pos - sourcePos), // Delta from copied building to inserter pos
                            pos2Delta = Quaternion.Inverse(sourceRot) * (inserter.pos2 - sourcePos), // Delta from copied building to inserter pos2

                            // store to restore inserter speed
                            refCount = Mathf.RoundToInt((float)(inserter.stt - 0.499f) / itemProto.prefabDesc.inserterSTT),

                            // not important?
                            pickOffset = inserter.pickOffset,
                            insertOffset = inserter.insertOffset,

                            // needed for pose?
                            t1 = inserter.t1,
                            t2 = inserter.t2,

                            filterId = inserter.filter,

                            movesFromReference = movesFromReference,

                            startSlot = -1,
                            endSlot = -1,

                            otherIsBelt = otherIsBelt
                        };

                        // compute the start and end slot that the cached inserter uses
                        InserterPoses.CalculatePose(actionBuild, pickTarget, insertTarget);

                        if (actionBuild.posePairs.Count > 0)
                        {
                            float minDistance = 1000f;
                            for (int j = 0; j < actionBuild.posePairs.Count; ++j)
                            {
                                var posePair = actionBuild.posePairs[j];
                                float startDistance = Vector3.Distance(posePair.startPose.position, inserterEntity.pos);
                                float endDistance = Vector3.Distance(posePair.endPose.position, inserter.pos2);
                                float poseDistance = startDistance + endDistance;

                                if (poseDistance < minDistance)
                                {
                                    minDistance = poseDistance;
                                    copiedInserter.startSlot = posePair.startSlot;
                                    copiedInserter.endSlot = posePair.endSlot;

                                    copiedInserter.pickOffset = (short)posePair.startOffset;
                                    copiedInserter.insertOffset = (short)posePair.endOffset;
                                }
                            }
                        }

                        data.copiedInserters.Add(copiedInserter.originalId, copiedInserter);
                    }
                }
            }

            hasData = true;
            return copiedBuilding;
        }

        public static List<BuildPreview> paste(Vector3 targetPos, float yaw)
        {
            pastedEntities.Clear();
            InserterPoses.resetOverrides();

            var absoluteTargetRot = Maths.SphericalRotation(targetPos, yaw);
            var previews = new List<BuildPreview>();
            var absolutePositions = new List<Vector3>();

            foreach (var building in data.copiedBuildings.Values)
            {
                Vector3 absoluteBuildingPos = GetPointFromMoves(targetPos, building.movesFromReference, absoluteTargetRot);
                Quaternion absoluteBuildingRot = Maths.SphericalRotation(absoluteBuildingPos, yaw + building.cursorRelativeYaw);
                var desc = GetPrefabDesc(building);
                BuildPreview bp = BuildPreview.CreateSingle(building.itemProto, desc , true);
                bp.ResetInfos();
                bp.desc = desc;
                bp.item = building.itemProto;
                bp.recipeId = building.recipeId;
                bp.lpos = absoluteBuildingPos;
                bp.lrot = absoluteBuildingRot;

                var pose = new Pose(absoluteBuildingPos, absoluteBuildingRot);

                var objId = InserterPoses.addOverride(pose, building.itemProto);

                pastedEntities.Add(building.originalId, new PastedEntity()
                {
                    sourceBuilding = building,
                    pose = pose,
                    objId = objId,
                    buildPreview = bp
                });
                absolutePositions.Add(absoluteBuildingPos);
                previews.Add(bp);
            }

            foreach (var belt in data.copiedBelts.Values)
            {
                var absoluteBeltPos = GetPointFromMoves(targetPos, belt.movesFromReference, absoluteTargetRot);
                var absoluteBeltRot = Maths.SphericalRotation(absoluteBeltPos, yaw);

                BuildPreview bp = BuildPreview.CreateSingle(belt.itemProto, belt.itemProto.prefabDesc, true);
                bp.ResetInfos();
                bp.desc = belt.itemProto.prefabDesc;
                bp.item = belt.itemProto;

                bp.lpos = absoluteBeltPos;
                bp.lrot = absoluteBeltRot;
                bp.outputToSlot = -1;
                bp.outputFromSlot = 0;

                bp.inputFromSlot = -1;
                bp.inputToSlot = 1;

                bp.outputOffset = 0;
                bp.inputOffset = 0;

                var pose = new Pose(absoluteBeltPos, absoluteBeltRot);

                var objId = InserterPoses.addOverride(pose, belt.itemProto);

                pastedEntities.Add(belt.originalId, new PastedEntity()
                {
                    pose = pose,
                    objId = objId,
                    buildPreview = bp
                });
                absolutePositions.Add(absoluteBeltPos);
                previews.Add(bp);
            }


            // after creating the belt previews this restore the correct connection to other belts and buildings
            foreach (var belt in data.copiedBelts.Values)
            {
                var preview = pastedEntities[belt.originalId].buildPreview;

                if (belt.outputId != 0 && pastedEntities.ContainsKey(belt.outputId))
                {
                    preview.output = pastedEntities[belt.outputId].buildPreview;

                    if (data.copiedBelts[belt.outputId].backInputId == belt.originalId)
                    {
                        preview.outputToSlot = 1;
                    }
                    if (data.copiedBelts[belt.outputId].leftInputId == belt.originalId)
                    {
                        preview.outputToSlot = 2;
                    }
                    if (data.copiedBelts[belt.outputId].rightInputId == belt.originalId)
                    {
                        preview.outputToSlot = 3;
                    }
                }

                if (belt.connectedBuildingId != 0 && pastedEntities.ContainsKey(belt.connectedBuildingId))
                {
                    if(belt.connectedBuildingIsOutput)
                    {
                        preview.output = pastedEntities[belt.connectedBuildingId].buildPreview;
                        preview.outputToSlot = belt.connectedBuildingSlot;
                    } else
                    {
                        preview.input = pastedEntities[belt.connectedBuildingId].buildPreview;
                        preview.inputFromSlot = belt.connectedBuildingSlot;
                    }
                }
            }

            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            BuildLogic.ActivateColliders(ref actionBuild.nearcdLogic, absolutePositions);

            foreach (var copiedInserter in data.copiedInserters.Values)
            {
                var positionData = InserterPoses.GetPositions(copiedInserter);

                var bp = BuildPreview.CreateSingle(LDB.items.Select(copiedInserter.itemProto.ID), copiedInserter.itemProto.prefabDesc, true);
                bp.ResetInfos();

                var buildPreview = pastedEntities[copiedInserter.referenceBuildingId].buildPreview;

                bp.lrot = buildPreview.lrot * copiedInserter.rot;
                bp.lrot2 = buildPreview.lrot * copiedInserter.rot2;

                bp.lpos = buildPreview.lpos + buildPreview.lrot * positionData.posDelta;
                bp.lpos2 = buildPreview.lpos + buildPreview.lrot * positionData.pos2Delta;

                bp.inputToSlot = 1;
                bp.outputFromSlot = 0;

                bp.inputOffset = positionData.pickOffset;
                bp.outputOffset = positionData.insertOffset;
                bp.outputToSlot = positionData.endSlot;
                bp.inputFromSlot = positionData.startSlot;
                bp.condition = positionData.condition;

                bp.filterId = copiedInserter.filterId;

                if (pastedEntities.ContainsKey(positionData.inputOriginalId))
                {
                    bp.input = pastedEntities[positionData.inputOriginalId].buildPreview;
                }
                else
                {
                    bp.inputObjId = positionData.inputObjId;
                }

                if (pastedEntities.ContainsKey(positionData.outputOriginalId))
                {
                    bp.output = pastedEntities[positionData.outputOriginalId].buildPreview;
                }
                else
                {
                    bp.outputObjId = positionData.outputObjId;
                }

                pastedEntities.Add(copiedInserter.originalId, new PastedEntity()
                {
                    buildPreview = bp
                });

                previews.Add(bp);
            }

            return previews;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ConnGizmoRenderer), "Update")]
        public static void ConnGizmoRenderer_Update_Postfix(ref ConnGizmoRenderer __instance)
        {
            foreach (var pastedEntity in pastedEntities)
            {
                var preview = pastedEntity.Value.buildPreview;
                if (preview.desc.beltSpeed <= 0)
                {
                    continue;
                }

                ConnGizmoObj item = default(ConnGizmoObj);
                item.pos = preview.lpos;
                item.rot = Quaternion.FromToRotation(Vector3.up, preview.lpos.normalized);
                item.color = 3u;
                item.size = 1f;

                if (preview.condition != EBuildCondition.Ok)
                {
                    item.color = 0u;
                }

                __instance.objs_1.Add(item);

                if (preview.output != null)
                {
                    Vector3 vector2 = preview.output.lpos - preview.lpos;
                    item.rot = Quaternion.LookRotation(vector2.normalized, preview.lpos.normalized);
                    item.size = vector2.magnitude;
                    __instance.objs_2.Add(item);
                }

                if (preview.input != null)
                {
                    item.pos = preview.input.lpos;
                    item.rot = Quaternion.FromToRotation(Vector3.up, preview.input.lpos.normalized);
                    item.color = 3u;
                    item.size = 1f;
                    if (preview.condition != EBuildCondition.Ok)
                    {
                        item.color = 0u;
                    }
                    __instance.objs_1.Add(item);

                    Vector3 vector2 = preview.lpos - preview.input.lpos;
                    item.rot = Quaternion.LookRotation(vector2.normalized, preview.input.lpos.normalized);
                    item.size = vector2.magnitude;
                    __instance.objs_2.Add(item);
                }

                
            }

            __instance.cbuffer_0.SetData<ConnGizmoObj>(__instance.objs_0);
            __instance.cbuffer_1.SetData<ConnGizmoObj>(__instance.objs_1, 0, 0, (__instance.objs_1.Count >= __instance.cbuffer_1.count) ? __instance.cbuffer_1.count : __instance.objs_1.Count);
            __instance.cbuffer_2.SetData<ConnGizmoObj>(__instance.objs_2, 0, 0, (__instance.objs_2.Count >= __instance.cbuffer_2.count) ? __instance.cbuffer_2.count : __instance.objs_2.Count);
            __instance.cbuffer_3.SetData<ConnGizmoObj>(__instance.objs_3, 0, 0, (__instance.objs_3.Count >= __instance.cbuffer_3.count) ? __instance.cbuffer_3.count : __instance.objs_3.Count);
            __instance.cbuffer_4.SetData<ConnGizmoObj>(__instance.objs_4, 0, 0, (__instance.objs_4.Count >= __instance.cbuffer_4.count) ? __instance.cbuffer_4.count : __instance.objs_4.Count);
        }
    }
}
