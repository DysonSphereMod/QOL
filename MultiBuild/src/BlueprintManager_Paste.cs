using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    class BlueprintManager_Paste
    {
        public const int COPY_INDEX_MULTIPLIER = 10_000_000;

        private static int[][] _nearObjectIds = new int[Util.MAX_THREADS][];
        private static PlayerAction_Build[] _abs = new PlayerAction_Build[Util.MAX_THREADS];

        public static void PrepareThreads()
        {
            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            for (var i = 0; i < Util.MAX_THREADS; i++)
            {
                _abs[i] = Util.ClonePlayerAction_Build(actionBuild);
                _nearObjectIds[i] = new int[128];
            }
        }

        public static void Paste(BlueprintData data, Vector3 targetPos, float yaw, bool pasteInserters = true, int copyIndex = 0)
        {
            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            Vector2 targetSpr = targetPos.ToSpherical();
            float yawRad = yaw * Mathf.Deg2Rad;

            ConcurrentQueue<BuildingCopy> buildingsQueue = new ConcurrentQueue<BuildingCopy>(data.copiedBuildings);
            Util.Parallelize((int threadIndex) =>
            {
                while (buildingsQueue.TryDequeue(out BuildingCopy building))
                {
                    var pastedEntity = ConcurrentPasteBuilding(threadIndex, building, targetSpr, yaw, copyIndex);

                    lock (actionBuild.nearcdLogic)
                    {
                        actionBuild.nearcdLogic.ActiveEntityBuildCollidersInArea(pastedEntity.pose.position, 5f);
                    }

                    BuildLogic.CheckBuildConditionsWorker(_abs[threadIndex], pastedEntity.buildPreview);
                }
            });

            ConcurrentQueue<BeltCopy> beltsQueue = new ConcurrentQueue<BeltCopy>(data.copiedBelts);
            Util.Parallelize((int threadIndex) =>
            {
                while (beltsQueue.TryDequeue(out BeltCopy belt))
                {
                    var pastedEntity = ConcurrentPasteBelt(threadIndex, belt, targetSpr, yaw, copyIndex);
                }
            });



            // after creating the belt previews this restore the correct connection to other belts and buildings
            beltsQueue = new ConcurrentQueue<BeltCopy>(data.copiedBelts);
            Util.Parallelize((int threadIndex) =>
            {
                while (beltsQueue.TryDequeue(out BeltCopy belt))
                {
                    var pastedEntity = ConcurrentConnectBelt(threadIndex, belt, copyIndex);
                    BuildLogic.CheckBuildConditionsWorker(_abs[threadIndex], pastedEntity.buildPreview);
                }
            });

            if (pasteInserters)
            {
                ConcurrentQueue<InserterCopy> inserterQueue = new ConcurrentQueue<InserterCopy>(data.copiedInserters);
                Util.Parallelize((threadIndex) =>
                {
                    while (inserterQueue.TryDequeue(out InserterCopy inserter))
                    {
                        var pastedEntity = ConcurrentPasteInserter(threadIndex, inserter, yaw, copyIndex);


                    }
                });
            }
        }

        public static PastedEntity ConcurrentPasteBuilding(int threadIndex, BuildingCopy building, Vector2 targetSpr, float yaw, int copyIndex)
        {
            var actionBuild = _abs[threadIndex];
            int pasteId = COPY_INDEX_MULTIPLIER * copyIndex + building.originalId;

            if (!BlueprintManager.pastedEntities.TryGetValue(pasteId, out PastedEntity pastedEntity))
            {
                PrefabDesc desc = BlueprintManager.GetPrefabDesc(building);
                BuildPreview bp = BuildPreview.CreateSingle(building.itemProto, desc, true);
                bp.ResetInfos();
                bp.desc = desc;
                bp.item = building.itemProto;
                bp.recipeId = building.recipeId;

                pastedEntity = new PastedEntity()
                {
                    status = EPastedStatus.NEW,
                    type = EPastedType.BUILDING,
                    sourceBuilding = building,
                    buildPreview = bp
                };

                BlueprintManager.pastedEntities.TryAdd(pasteId, pastedEntity);

                lock (actionBuild.buildPreviews)
                {
                    actionBuild.buildPreviews.Add(bp);
                }
            }
            else
            {
                pastedEntity.status = EPastedStatus.UPDATE;
            }

            Vector2 newRelative = building.cursorRelativePos.Rotate(yaw * Mathf.Deg2Rad, building.originalSegmentCount);
            Vector2 sprPos = newRelative + targetSpr;

            int newSegmentCount = Util.GetSegmentsCount(sprPos);

            float sizeDeviation = building.originalSegmentCount / (float)newSegmentCount;

            sprPos = new Vector2(newRelative.x, newRelative.y * sizeDeviation) + targetSpr;

            Vector3 absoluteBuildingPos = sprPos.SnapToGrid();
            Quaternion absoluteBuildingRot = Maths.SphericalRotation(absoluteBuildingPos, yaw + building.cursorRelativeYaw);

            Pose pose = new Pose(absoluteBuildingPos, absoluteBuildingRot);

            pastedEntity.objId = InserterPoses.AddOverride(pose, building.itemProto);
            pastedEntity.pose = pose;

            pastedEntity.buildPreview.lpos = absoluteBuildingPos;
            pastedEntity.buildPreview.lrot = absoluteBuildingRot;
            pastedEntity.buildPreview.condition = EBuildCondition.Ok;

            return pastedEntity;
        }

        public static PastedEntity ConcurrentPasteBelt(int threadIndex, BeltCopy belt, Vector2 targetSpr, float yaw, int copyIndex)
        {
            var actionBuild = _abs[threadIndex];
            int pasteId = COPY_INDEX_MULTIPLIER * copyIndex + belt.originalId;
            if (!BlueprintManager.pastedEntities.TryGetValue(pasteId, out PastedEntity pastedEntity))
            {
                BuildPreview bp = BuildPreview.CreateSingle(belt.itemProto, belt.itemProto.prefabDesc, false);

                pastedEntity = new PastedEntity()
                {
                    status = EPastedStatus.NEW,
                    type = EPastedType.BELT,
                    sourceBelt = belt,
                    buildPreview = bp,
                };

                BlueprintManager.pastedEntities.TryAdd(pasteId, pastedEntity);

                lock (actionBuild.buildPreviews)
                {
                    actionBuild.buildPreviews.Add(bp);
                }
            }
            else
            {
                pastedEntity.status = EPastedStatus.UPDATE;
            }

            Vector2 newRelative = belt.cursorRelativePos.Rotate(yaw * Mathf.Deg2Rad, belt.originalSegmentCount);
            Vector2 sprPos = newRelative + targetSpr;


            int newSegmentCount = Util.GetSegmentsCount(sprPos);
            float sizeDeviation = belt.originalSegmentCount / (float)newSegmentCount;

            sprPos = new Vector2(newRelative.x, newRelative.y * sizeDeviation) + targetSpr;

            Vector3 absoluteBeltPos = sprPos.SnapToGrid(belt.altitude * 1.3333333f / 2);

            // belts have always 0 yaw
            Quaternion absoluteBeltRot = Maths.SphericalRotation(absoluteBeltPos, 0f);


            Pose pose = new Pose(absoluteBeltPos, absoluteBeltRot);

            pastedEntity.objId = InserterPoses.AddOverride(pose, belt.itemProto);
            pastedEntity.pose = pose;

            pastedEntity.buildPreview.lpos = absoluteBeltPos;
            pastedEntity.buildPreview.lrot = absoluteBeltRot;



            pastedEntity.buildPreview.condition = EBuildCondition.Ok;

            return pastedEntity;
        }

        public static PastedEntity ConcurrentPasteInserter(int threadIndex, InserterCopy inserter, float yaw, int copyIndex)
        {
            var actionBuild = _abs[threadIndex];
            int pasteId = COPY_INDEX_MULTIPLIER * copyIndex + inserter.originalId;
            if (!BlueprintManager.pastedEntities.TryGetValue(pasteId, out PastedEntity pastedEntity))
            {
                BuildPreview bp = BuildPreview.CreateSingle(inserter.itemProto, inserter.itemProto.prefabDesc, true);

                pastedEntity = new PastedEntity()
                {
                    status = EPastedStatus.NEW,
                    type = EPastedType.INSERTER,
                    sourceInserter = inserter,
                    buildPreview = bp,
                };

                bp.filterId = inserter.filterId;
                bp.inputToSlot = 1;
                bp.outputFromSlot = 0;

                BlueprintManager.pastedEntities.TryAdd(pasteId, pastedEntity);

                lock (actionBuild.buildPreviews)
                {
                    actionBuild.buildPreviews.Add(bp);
                }
            }
            else
            {
                pastedEntity.status = EPastedStatus.UPDATE;
            }

            InserterPosition positionData = InserterPoses.GetPositions(actionBuild, inserter, yaw * Mathf.Deg2Rad, copyIndex);

            pastedEntity.buildPreview.lpos = positionData.absoluteInserterPos;
            pastedEntity.buildPreview.lpos2 = positionData.absoluteInserterPos2;

            pastedEntity.buildPreview.lrot = positionData.absoluteInserterRot;
            pastedEntity.buildPreview.lrot2 = positionData.absoluteInserterRot2;

            pastedEntity.buildPreview.inputOffset = positionData.pickOffset;
            pastedEntity.buildPreview.outputOffset = positionData.insertOffset;
            pastedEntity.buildPreview.outputToSlot = positionData.endSlot;
            pastedEntity.buildPreview.inputFromSlot = positionData.startSlot;

            pastedEntity.buildPreview.condition = positionData.condition;

            pastedEntity.buildPreview.input = null;
            pastedEntity.buildPreview.inputObjId = 0;
            pastedEntity.buildPreview.output = null;
            pastedEntity.buildPreview.outputObjId = 0;

            if (BlueprintManager.pastedEntities.TryGetValue(positionData.inputPastedId, out PastedEntity inputPastedEntity))
            {
                pastedEntity.buildPreview.input = inputPastedEntity.buildPreview;
            }
            else
            {
                pastedEntity.buildPreview.inputObjId = positionData.inputEntityId;
            }

            if (BlueprintManager.pastedEntities.TryGetValue(positionData.outputPastedId, out PastedEntity outputPastedEntity))
            {
                pastedEntity.buildPreview.output = outputPastedEntity.buildPreview;
            }
            else
            {
                pastedEntity.buildPreview.outputObjId = positionData.outputEntityId;
            }

            return pastedEntity;
        }

        public static PastedEntity ConcurrentConnectBelt(int threadIndex, BeltCopy belt, int copyIndex)
        {
            var actionBuild = _abs[threadIndex];
            int pasteId = COPY_INDEX_MULTIPLIER * copyIndex + belt.originalId;
            var pastedEntity = BlueprintManager.pastedEntities[pasteId];

            BuildPreview buildPreview = pastedEntity.buildPreview;

            buildPreview.output = null;
            buildPreview.outputToSlot = -1;
            buildPreview.outputFromSlot = 0;
            buildPreview.outputOffset = 0;

            buildPreview.input = null;
            buildPreview.inputFromSlot = -1;
            buildPreview.inputToSlot = 1;
            buildPreview.inputOffset = 0;

            buildPreview.coverObjId = 0;
            buildPreview.ignoreCollider = false;
            buildPreview.willCover = false;

            var pastedBackInputId = COPY_INDEX_MULTIPLIER * copyIndex + belt.backInputId;
            var pastedLeftInputId = COPY_INDEX_MULTIPLIER * copyIndex + belt.leftInputId;
            var pastedRightInputId = COPY_INDEX_MULTIPLIER * copyIndex + belt.rightInputId;
            var pastedOutputId = COPY_INDEX_MULTIPLIER * copyIndex + belt.outputId;
            var pastedConnectedBuildingId = COPY_INDEX_MULTIPLIER * copyIndex + belt.connectedBuildingId;


            if (pastedOutputId != 0 &&
                BlueprintManager.pastedEntities.TryGetValue(pastedOutputId, out PastedEntity otherPastedEntity) &&
                otherPastedEntity.type == EPastedType.BELT &&
                otherPastedEntity.status != EPastedStatus.REMOVE &&
                Vector3.Distance(buildPreview.lpos, otherPastedEntity.buildPreview.lpos) < 10) // if the belts are too far apart ignore connection
            {
                buildPreview.output = otherPastedEntity.buildPreview;
                var otherBelt = otherPastedEntity.sourceBelt;

                if (otherBelt.backInputId == belt.originalId)
                {
                    buildPreview.outputToSlot = 1;
                }
                if (otherBelt.leftInputId == belt.originalId)
                {
                    buildPreview.outputToSlot = 2;
                }
                if (otherBelt.rightInputId == belt.originalId)
                {
                    buildPreview.outputToSlot = 3;
                }
            }



            if (pastedConnectedBuildingId != 0 &&
                BlueprintManager.pastedEntities.TryGetValue(pastedConnectedBuildingId, out PastedEntity otherBuilding) &&
                otherBuilding.type == EPastedType.BUILDING &&
                otherBuilding.status != EPastedStatus.REMOVE)
            {
                if (belt.connectedBuildingIsOutput)
                {
                    buildPreview.output = otherBuilding.buildPreview;
                    buildPreview.outputToSlot = belt.connectedBuildingSlot;
                }
                else
                {
                    buildPreview.input = otherBuilding.buildPreview;
                    buildPreview.inputFromSlot = belt.connectedBuildingSlot;
                }
            }

            bool beltHasInput = BlueprintManager.pastedEntities.ContainsKey(pastedBackInputId) || BlueprintManager.pastedEntities.ContainsKey(pastedLeftInputId) || BlueprintManager.pastedEntities.ContainsKey(pastedRightInputId);
            bool beltHasOutput = BlueprintManager.pastedEntities.ContainsKey(pastedOutputId);

            if (!beltHasInput || !beltHasOutput)
            {
                var nearObjId = _nearObjectIds[threadIndex];
                int found = actionBuild.nearcdLogic.GetBuildingsInAreaNonAlloc(buildPreview.lpos, 0.34f, nearObjId, false);
                for (int x = 0; x < found; x++)
                {
                    int overlappingEntityId = nearObjId[x];

                    if (overlappingEntityId <= 0) continue;

                    EntityData overlappingEntityData = actionBuild.factory.entityPool[overlappingEntityId];

                    if (overlappingEntityData.beltId <= 0) continue;

                    BeltComponent overlappingBelt = actionBuild.factory.cargoTraffic.beltPool[overlappingEntityData.beltId];

                    bool overlappingBeltHasInput = (overlappingBelt.backInputId + overlappingBelt.leftInputId + overlappingBelt.rightInputId) != 0;
                    bool overlappingBeltHasOutput = overlappingBelt.outputId != 0;

                    if ((beltHasOutput && !overlappingBeltHasOutput) || (beltHasInput && !overlappingBeltHasInput))
                    {
                        // found overlapping belt that can be 'replaced' to connect to existing belts
                        buildPreview.coverObjId = overlappingEntityId;
                        buildPreview.ignoreCollider = true;
                        buildPreview.willCover = true;
                    }
                }

            }

            return pastedEntity;
        }

    }
}
