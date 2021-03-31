using UnityEngine;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public enum EPastedType
    {
        BUILDING,
        BELT,
        INSERTER
    }

    public enum EPastedStatus
    {
        NEW,
        UPDATE,
        REMOVE
    }

    public class PastedEntity
    {
        public EPastedStatus status;
        public EPastedType type;
        public BuildingCopy sourceBuilding;
        public BeltCopy sourceBelt;
        public InserterCopy sourceInserter;
        public BuildPreview buildPreview;
        public Pose pose;
        public int objId;
    }

    [HarmonyPatch]
    public class BlueprintManager
    {
        public const int COPY_INDEX_MULTIPLIER = 10_000_000;

        public static BlueprintData previousData = new BlueprintData();
        public static BlueprintData data = new BlueprintData();
        public static bool hasData = false;

        public static ConcurrentDictionary<int, PastedEntity> pastedEntities = new ConcurrentDictionary<int, PastedEntity>(Util.MAX_THREADS, 0);
        public static bool useExperimentalWidthFix = false;

        private static int[][] _nearObjectIds = new int[Util.MAX_THREADS][];
        private static PlayerAction_Build[] _abs = new PlayerAction_Build[Util.MAX_THREADS];
        public static void Reset()
        {
            if (!hasData)
            {
                return;
            }

            hasData = false;
            previousData = data;
            data = new BlueprintData();
            pastedEntities.Clear();
            GC.Collect();

            UpdateUIText();
        }

        public static void Restore(BlueprintData newData = null)
        {
            if (hasData)
            {
                BlueprintData temp = data;
                data = newData ?? previousData;
                previousData = temp;
            }
            else
            {
                hasData = true;
                data = newData ?? previousData;
            }

            pastedEntities.Clear();
            GC.Collect();
            UpdateUIText();
            EnterBuildModeAfterBp();
        }

        public static void UpdateUIText()
        {
            UIFunctionPanelPatch.blueprintGroup.infoTitle.text = "Stored:";
            if (previousData.name != "")
            {
                string name = previousData.name;
                if (name.Length > 25)
                {
                    name = name.Substring(0, 22) + "...";
                }

                UIFunctionPanelPatch.blueprintGroup.infoTitle.text += $" {name}";
            }

            Dictionary<string, int> counter = new Dictionary<string, int>();

            foreach (BuildingCopy bulding in previousData.copiedBuildings)
            {
                string name = bulding.itemProto.name;
                if (!counter.ContainsKey(name)) counter.Add(name, 0);
                counter[name]++;
            }

            foreach (BeltCopy belt in previousData.copiedBelts)
            {
                string name = "Belts";
                if (!counter.ContainsKey(name)) counter.Add(name, 0);
                counter[name]++;
            }

            foreach (InserterCopy inserter in previousData.copiedInserters)
            {
                string name = "Inserters";
                if (!counter.ContainsKey(name)) counter.Add(name, 0);
                counter[name]++;
            }


            if (counter.Count > 0)
            {
                UIFunctionPanelPatch.blueprintGroup.InfoText.text = counter.Select(x => $"{x.Value} x {x.Key}").Join(null, ", ");
            }
            else
            {
                UIFunctionPanelPatch.blueprintGroup.InfoText.text = "None";
            }
        }

        public static void EnterBuildModeAfterBp()
        {
            if (!hasData)
            {
                return;
            }

            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            // if no building use storage id as fake buildingId as we need something with buildmode == 1
            int firstItemProtoID = data.copiedBuildings.Count > 0 ? data.copiedBuildings.First().itemProto.ID : 2101;

            actionBuild.yaw = 0f;
            actionBuild.player.SetHandItems(firstItemProtoID, 0, 0);
            actionBuild.controller.cmd.mode = 1;
            actionBuild.controller.cmd.type = ECommand.Build;
        }

        public static PrefabDesc GetPrefabDesc(BuildingCopy copiedBuilding)
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

        public static void CopyEntities(List<int> entityIds)
        {
            PlanetFactory factory = GameMain.data.localPlanet.factory;

            var buildings = new List<EntityData>();
            var belts = new Dictionary<int, EntityData>();

            foreach (int id in entityIds)
            {
                EntityData entity = factory.entityPool[id];
                ItemProto entityProto = LDB.items.Select(entity.protoId);

                if (entityProto.prefabDesc.isInserter || entityProto.prefabDesc.minerType != EMinerType.None) continue;


                // ignore multilevel buildings (for now)
                if (!entityProto.prefabDesc.isBelt && (entity.pos.magnitude - GameMain.localPlanet.realRadius - 0.2f) < 0.5f)
                {
                    buildings.Add(entity);
                }
                else
                {
                    belts.Add(entity.id, entity);
                }
            }

            if (buildings.Count == 0 && belts.Count == 0)
            {
                return;
            }

            EntityData globalReference = buildings.Count > 0 ? buildings.First() : belts.Values.First();
            foreach (EntityData building in buildings) CopyBuilding(building, globalReference);
            foreach (EntityData belt in belts.Values) CopyBelt(belt, globalReference);
        }

        public static BeltCopy CopyBelt(EntityData sourceEntity, EntityData referenceEntity)
        {
            PlanetFactory factory = GameMain.data.localPlanet.factory;

            ItemProto sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (!sourceEntityProto.prefabDesc.isBelt)
            {
                return null;
            }

            BeltComponent belt = factory.cargoTraffic.beltPool[sourceEntity.beltId];
            Vector2 sourceSprPos = sourceEntity.pos.ToSpherical();

            BeltCopy copiedBelt = new BeltCopy()
            {
                originalId = sourceEntity.id,
                protoId = sourceEntityProto.ID,
                itemProto = sourceEntityProto,
                altitude = Mathf.RoundToInt(2 * (sourceEntity.pos.magnitude - GameMain.localPlanet.realRadius - 0.2f) / 1.3333333f),
                backInputId = factory.cargoTraffic.beltPool[belt.backInputId].entityId,
                leftInputId = factory.cargoTraffic.beltPool[belt.leftInputId].entityId,
                rightInputId = factory.cargoTraffic.beltPool[belt.rightInputId].entityId,
                outputId = factory.cargoTraffic.beltPool[belt.outputId].entityId,
            };

            factory.ReadObjectConn(sourceEntity.id, 0, out bool isOutput, out int otherId, out int otherSlot);
            if (otherId > 0 && factory.entityPool[otherId].beltId == 0)
            {
                copiedBelt.connectedBuildingId = otherId;
                copiedBelt.connectedBuildingIsOutput = isOutput;
                copiedBelt.connectedBuildingSlot = otherSlot;
            }

            factory.ReadObjectConn(sourceEntity.id, 1, out isOutput, out otherId, out otherSlot);
            if (otherId > 0 && factory.entityPool[otherId].beltId == 0)
            {
                copiedBelt.connectedBuildingId = otherId;
                copiedBelt.connectedBuildingIsOutput = isOutput;
                copiedBelt.connectedBuildingSlot = otherSlot;
            }

            if (sourceEntity.id == referenceEntity.id)
            {
                data.referencePos = sourceSprPos;
            }
            else
            {
                copiedBelt.originalSegmentCount = sourceSprPos.GetSegmentsCount();
                copiedBelt.cursorRelativePos = (sourceSprPos - data.referencePos).Clamp();
            }

            data.copiedBelts.Add(copiedBelt);

            factory.ReadObjectConn(sourceEntity.id, 4, out _, out otherId, out _);

            if (otherId != 0)
            {
                // only copy belt to belt inserter if both belts are part fo the blueprint
                factory.ReadObjectConn(otherId, 0, out _, out int endId, out _);
                factory.ReadObjectConn(otherId, 1, out _, out int startId, out _);

                int idToFind = sourceEntity.id == endId ? startId : endId;

                if (data.copiedBelts.FindIndex(x => x.originalId == idToFind) != -1)
                {

                    EntityData inserterEntity = factory.entityPool[otherId];
                    CopyInserter(inserterEntity, sourceEntity);
                }
            }

            hasData = true;
            return copiedBelt;
        }

        public static BuildingCopy CopyBuilding(EntityData sourceEntity, EntityData referenceEntity)
        {
            PlanetFactory factory = GameMain.data.localPlanet.factory;

            ItemProto sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            Vector3 sourcePos = sourceEntity.pos;
            Quaternion sourceRot = sourceEntity.rot;

            Quaternion zeroRot = Maths.SphericalRotation(sourcePos, 0f);
            float yaw = Vector3.SignedAngle(zeroRot.Forward(), sourceRot.Forward(), zeroRot.Up());


            BuildingCopy copiedBuilding = new BuildingCopy()
            {
                originalId = sourceEntity.id,
                protoId = sourceEntityProto.ID,
                itemProto = sourceEntityProto,
                modelIndex = sourceEntity.modelIndex
            };


            if (sourceEntity.assemblerId > 0)
            {
                copiedBuilding.recipeId = factory.factorySystem.assemblerPool[sourceEntity.assemblerId].recipeId;
            }
            else if (sourceEntity.labId > 0)
            {
                LabComponent labComponent = factory.factorySystem.labPool[sourceEntity.labId];
                copiedBuilding.recipeId = ((!labComponent.researchMode) ? labComponent.recipeId : -1);
            }
            else if (sourceEntity.powerGenId > 0)
            {
                PowerGeneratorComponent powerGeneratorComponent = factory.powerSystem.genPool[sourceEntity.powerGenId];
                if (powerGeneratorComponent.gamma)
                {
                    copiedBuilding.recipeId = ((powerGeneratorComponent.productId <= 0) ? 0 : 1);
                }
            }
            else if (sourceEntity.powerExcId > 0)
            {
                copiedBuilding.recipeId = Mathf.RoundToInt(factory.powerSystem.excPool[sourceEntity.powerExcId].targetState);
            }
            else if (sourceEntity.ejectorId > 0)
            {
                copiedBuilding.recipeId = factory.factorySystem.ejectorPool[sourceEntity.ejectorId].orbitId;
            }
            else if (sourceEntity.stationId > 0)
            {
                StationComponent stationComponent = factory.transport.stationPool[sourceEntity.stationId];

                for (int i = 0; i < stationComponent.slots.Length; i++)
                {
                    if (stationComponent.slots[i].storageIdx != 0)
                    {
                        copiedBuilding.slotFilters.Add(new SlotFilter()
                        {
                            slotIndex = i,
                            storageIdx = stationComponent.slots[i].storageIdx
                        });
                    }
                }

                for (int i = 0; i < stationComponent.storage.Length; i++)
                {
                    if (stationComponent.storage[i].itemId != 0)
                    {
                        copiedBuilding.stationSettings.Add(new StationSetting()
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
            else if (sourceEntity.splitterId > 0)
            {

                // TODO: find a way to restore splitter settings
                // SplitterComponent splitterComponent = factory.cargoTraffic.splitterPool[sourceEntity.splitterId];

            }

            Vector2 sourceSprPos = sourcePos.ToSpherical();

            if (sourceEntity.id == referenceEntity.id)
            {
                data.referencePos = sourceSprPos;
                copiedBuilding.cursorRelativeYaw = yaw;
            }
            else
            {
                copiedBuilding.originalSegmentCount = sourceSprPos.GetSegmentsCount();
                copiedBuilding.cursorRelativePos = (sourceSprPos - data.referencePos).Clamp();
                copiedBuilding.cursorRelativeYaw = yaw;
            }

            data.copiedBuildings.Add(copiedBuilding);

            for (int i = 0; i < sourceEntityProto.prefabDesc.insertPoses.Length; i++)
            {
                factory.ReadObjectConn(sourceEntity.id, i, out bool _, out int otherObjId, out int _);

                if (otherObjId > 0)
                {
                    EntityData inserterEntity = factory.entityPool[otherObjId];
                    CopyInserter(inserterEntity, sourceEntity);
                }
            }

            hasData = true;
            return copiedBuilding;
        }

        public static InserterCopy CopyInserter(EntityData sourceEntity, EntityData referenceEntity)
        {
            PlanetFactory factory = GameMain.data.localPlanet.factory;
            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            if (sourceEntity.inserterId == 0)
            {
                return null;
            }

            InserterComponent inserter = factory.factorySystem.inserterPool[sourceEntity.inserterId];

            if (data.copiedInserters.FindIndex(x => x.originalId == inserter.entityId) != -1)
            {
                return null;
            }

            int pickTarget = inserter.pickTarget;
            int insertTarget = inserter.insertTarget;

            ItemProto itemProto = LDB.items.Select(sourceEntity.protoId);

            bool incoming = insertTarget == referenceEntity.id;
            int otherId = incoming ? pickTarget : insertTarget;


            Vector2 referenceSprPos = referenceEntity.pos.ToSpherical();
            Vector2 sourceSprPos = sourceEntity.pos.ToSpherical();
            Vector2 sourceSprPos2 = inserter.pos2.ToSpherical();

            // The belt or other building this inserter is attached to
            Vector2 otherSprPos;
            ItemProto otherProto;

            if (otherId > 0)
            {
                otherProto = LDB.items.Select(factory.entityPool[otherId].protoId);
                otherSprPos = factory.entityPool[otherId].pos.ToSpherical();
            }
            else if (otherId < 0)
            {
                otherProto = LDB.items.Select(factory.prebuildPool[-otherId].protoId);
                otherSprPos = factory.prebuildPool[-otherId].pos.ToSpherical();
            }
            else
            {
                otherSprPos = sourceSprPos2;
                otherProto = null;
            }

            bool otherIsBelt = otherProto == null || otherProto.prefabDesc.isBelt;


            // Cache info for this inserter
            InserterCopy copiedInserter = new InserterCopy
            {
                itemProto = itemProto,
                protoId = itemProto.ID,
                originalId = inserter.entityId,

                pickTarget = pickTarget,
                insertTarget = insertTarget,

                referenceBuildingId = referenceEntity.id,

                incoming = incoming,

                // rotations + deltas relative to the source building's rotation
                rot = Quaternion.Inverse(referenceEntity.rot) * sourceEntity.rot,
                rot2 = Quaternion.Inverse(referenceEntity.rot) * inserter.rot2,
                posDelta = sourceSprPos - referenceSprPos, // Delta from copied building to inserter pos
                pos2Delta = sourceSprPos2 - referenceSprPos, // Delta from copied building to inserter pos2

                posDeltaCount = sourceSprPos.GetSegmentsCount(),
                pos2DeltaCount = sourceSprPos2.GetSegmentsCount(),

                otherPosDelta = otherSprPos - referenceSprPos,
                otherPosDeltaCount = otherSprPos.GetSegmentsCount(),

                // not important?
                pickOffset = inserter.pickOffset,
                insertOffset = inserter.insertOffset,

                filterId = inserter.filter,

                startSlot = -1,
                endSlot = -1,

                otherIsBelt = otherIsBelt
            };

            InserterPoses.CalculatePose(actionBuild, pickTarget, insertTarget);

            if (actionBuild.posePairs.Count > 0)
            {
                float minDistance = 1000f;
                for (int j = 0; j < actionBuild.posePairs.Count; ++j)
                {
                    var posePair = actionBuild.posePairs[j];
                    float startDistance = Vector3.Distance(posePair.startPose.position, sourceEntity.pos);
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


            /*        factory.ReadObjectConn(sourceEntity.id, 1, out bool isOutput, out int connectedId, out int connectedSlot);

                        if (connectedId != 0)
                        {
                            copiedInserter.startSlot = connectedSlot;
                        }


                        factory.ReadObjectConn(sourceEntity.id, 0, out _, out connectedId, out connectedSlot);
                        if (connectedId != 0)
                        {
                            copiedInserter.endSlot = connectedSlot;
                        }
            */

            data.copiedInserters.Add(copiedInserter);

            return copiedInserter;
        }

        public static void PreparePaste()
        {
            InserterPoses.ResetOverrides();

            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            for (var i = 0; i < Util.MAX_THREADS; i++)
            {
                _abs[i] = Util.ClonePlayerAction_Build(actionBuild);
                _nearObjectIds[i] = new int[128];
            }

            foreach (var pastedEntity in pastedEntities.Values)
            {
                pastedEntity.status = EPastedStatus.REMOVE;
            }
        }

        public static void AfterPaste()
        {
            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            var entitiesToRemove = pastedEntities.Where(entity => entity.Value.status == EPastedStatus.REMOVE).ToList();
            foreach (var pastedEntity in entitiesToRemove)
            {
                actionBuild.RemoveBuildPreview(pastedEntity.Value.buildPreview);
                pastedEntities.TryRemove(pastedEntity.Key, out _);
                pastedEntity.Value.buildPreview.Free();
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "ClearBuildPreviews")]
        public static void PlayerAction_Build_ClearBuildPreviews_Prefix()
        {
            pastedEntities.Clear();
            InserterPoses.ResetOverrides();
        }

        public static void Paste(Vector3 targetPos, float yaw, bool pasteInserters = true, int copyIndex = 0)
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

            if (!pastedEntities.TryGetValue(pasteId, out PastedEntity pastedEntity))
            {
                PrefabDesc desc = GetPrefabDesc(building);
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

                pastedEntities.TryAdd(pasteId, pastedEntity);

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

            int segments = (int)(GameMain.localPlanet.realRadius / 4f + 0.1f) * 4;
            float rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * GameMain.localPlanet.realRadius;
            int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            int newSegmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, segments);

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
            if (!pastedEntities.TryGetValue(pasteId, out PastedEntity pastedEntity))
            {
                BuildPreview bp = BuildPreview.CreateSingle(belt.itemProto, belt.itemProto.prefabDesc, false);

                pastedEntity = new PastedEntity()
                {
                    status = EPastedStatus.NEW,
                    type = EPastedType.BELT,
                    sourceBelt = belt,
                    buildPreview = bp,
                };

                pastedEntities.TryAdd(pasteId, pastedEntity);

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

            int segments = (int)(GameMain.localPlanet.realRadius / 4f + 0.1f) * 4;
            float rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * GameMain.localPlanet.realRadius;
            int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            int newSegmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, segments);

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
            if (!pastedEntities.TryGetValue(pasteId, out PastedEntity pastedEntity))
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

                pastedEntities.TryAdd(pasteId, pastedEntity);

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

            if (pastedEntities.TryGetValue(positionData.inputPastedId, out PastedEntity inputPastedEntity))
            {
                pastedEntity.buildPreview.input = inputPastedEntity.buildPreview;
            }
            else
            {
                pastedEntity.buildPreview.inputObjId = positionData.inputEntityId;
            }

            if (pastedEntities.TryGetValue(positionData.outputPastedId, out PastedEntity outputPastedEntity))
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
            var pastedEntity = pastedEntities[pasteId];

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
                pastedEntities.TryGetValue(pastedOutputId, out PastedEntity otherPastedEntity) &&
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
                pastedEntities.TryGetValue(pastedConnectedBuildingId, out PastedEntity otherBuilding) &&
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

            bool beltHasInput = pastedEntities.ContainsKey(pastedBackInputId) || pastedEntities.ContainsKey(pastedLeftInputId) || pastedEntities.ContainsKey(pastedRightInputId);
            bool beltHasOutput = pastedEntities.ContainsKey(pastedOutputId);

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

        [HarmonyPostfix, HarmonyPatch(typeof(ConnGizmoRenderer), "Update")]
        public static void ConnGizmoRenderer_Update_Postfix(ref ConnGizmoRenderer __instance)
        {
            if (BlueprintManager.pastedEntities.Count > 1)
            {
                PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;
                foreach (BuildPreview preview in actionBuild.buildPreviews)
                {
                    if (preview.desc.beltSpeed <= 0)
                    {
                        continue;
                    }

                    ConnGizmoObj item = default;
                    item.pos = preview.lpos;
                    item.rot = Quaternion.FromToRotation(Vector3.up, preview.lpos.normalized);
                    item.color = 3u;
                    item.size = 1f;

                    if (preview.condition != EBuildCondition.Ok)
                    {
                        item.color = 0u;
                    }

                    if (preview.ignoreCollider)
                    {
                        __instance.objs_0.Add(item);
                    }
                    else
                    {
                        __instance.objs_1.Add(item);
                    }


                    if (preview.output != null)
                    {
                        Vector3 vector2 = preview.output.lpos - preview.lpos;
                        if (vector2 != Vector3.zero)
                        {
                            item.rot = Quaternion.LookRotation(vector2.normalized, preview.lpos.normalized);
                            item.size = vector2.magnitude;
                            __instance.objs_2.Add(item);
                        }
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

                        __instance.objs_0.Add(item);

                        Vector3 vector2 = preview.lpos - preview.input.lpos;
                        if (vector2 != Vector3.zero)
                        {
                            item.rot = Quaternion.LookRotation(vector2.normalized, preview.input.lpos.normalized);
                            item.size = vector2.magnitude;
                            __instance.objs_2.Add(item);
                        }
                    }
                }

                __instance.cbuffer_0.SetData(__instance.objs_0);
                __instance.cbuffer_1.SetData(__instance.objs_1, 0, 0,
                    (__instance.objs_1.Count >= __instance.cbuffer_1.count) ? __instance.cbuffer_1.count : __instance.objs_1.Count);
                __instance.cbuffer_2.SetData(__instance.objs_2, 0, 0,
                    (__instance.objs_2.Count >= __instance.cbuffer_2.count) ? __instance.cbuffer_2.count : __instance.objs_2.Count);
                __instance.cbuffer_3.SetData(__instance.objs_3, 0, 0,
                    (__instance.objs_3.Count >= __instance.cbuffer_3.count) ? __instance.cbuffer_3.count : __instance.objs_3.Count);
                __instance.cbuffer_4.SetData(__instance.objs_4, 0, 0,
                    (__instance.objs_4.Count >= __instance.cbuffer_4.count) ? __instance.cbuffer_4.count : __instance.objs_4.Count);
            }
        }
    }
}
