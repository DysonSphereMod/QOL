using UnityEngine;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public enum EPastedType
    {
        BUILDING,
        BELT,
        INSERTER
    }

    public class PastedEntity
    {
        public EPastedType type;
        public int index;
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
        public static Dictionary<int, PastedEntity> parallelPastedEntities = new Dictionary<int, PastedEntity>();

        public static List<BuildPreview> previews = new List<BuildPreview>();

        public static bool useExperimentalWidthFix = false;

        private static int[] _nearObjectIds = new int[128];
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
            parallelPastedEntities.Clear();
            previews.Clear();
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
            parallelPastedEntities.Clear();
            previews.Clear();
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

        public static List<BuildPreview> Paste(Vector3 targetPos, float yaw, bool pasteInserters = true)
        {
            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            pastedEntities.Clear();
            parallelPastedEntities.Clear();
            previews.Clear();
            InserterPoses.ResetOverrides();

            Vector2 targetSpr = targetPos.ToSpherical();
            float yawRad = yaw * Mathf.Deg2Rad;


            for (int i = 0; i < data.copiedBuildings.Count; i++)
            {
                PasteBuilding(i, data.copiedBuildings[i], targetSpr, yaw);
            }

            for (int i = 0; i < data.copiedBelts.Count; i++)
            {
                PasteBelt(i, data.copiedBelts[i], targetSpr, yaw);
            }

            if (pasteInserters && data.copiedInserters.Count > 16)
            {

                var maxThreads = Environment.ProcessorCount - 1;
                var runningThreads = 0;
                var next = -1;
                ManualResetEvent done = new ManualResetEvent(false);

                for (int i = 0; i < maxThreads; i++)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        int item;
                        Interlocked.Increment(ref runningThreads);

                        try
                        {
                            PlayerAction_Build ab = BuildLogic.ClonePlayerAction_Build(actionBuild);
                            while ((item = Interlocked.Increment(ref next)) < data.copiedInserters.Count)
                            {
                                PasteInserter(ab, item, data.copiedInserters[item], yaw);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.ToString());
                        }
                        finally
                        {
                            if (Interlocked.Decrement(ref runningThreads) == 0)
                            {
                                done.Set();
                            }
                        }
                    });
                }

                done.WaitOne();
            }
            else if (pasteInserters)
            {
                for (var i = 0; i < data.copiedInserters.Count; i++)
                {
                    PasteInserter(actionBuild, i, data.copiedInserters[i], yaw);
                }
            }
            // merge the inserter pastedEntities in the main dictionaries
            foreach (var item in parallelPastedEntities)
            {
                previews.Add(item.Value.buildPreview);
                pastedEntities.Add(item.Key, item.Value);
            }

            // after creating the belt previews this restore the correct connection to other belts and buildings
            foreach (BeltCopy belt in data.copiedBelts)
            {
                BuildPreview preview = pastedEntities[belt.originalId].buildPreview;

                if (belt.outputId != 0 &&
                    pastedEntities.TryGetValue(belt.outputId, out PastedEntity otherEntity) &&
                    Vector3.Distance(preview.lpos, otherEntity.buildPreview.lpos) < 10) // if the belts are too far apart ignore connection
                {

                    preview.output = otherEntity.buildPreview;
                    var otherBelt = data.copiedBelts[otherEntity.index];

                    if (otherBelt.backInputId == belt.originalId)
                    {
                        preview.outputToSlot = 1;
                    }
                    if (otherBelt.leftInputId == belt.originalId)
                    {
                        preview.outputToSlot = 2;
                    }
                    if (otherBelt.rightInputId == belt.originalId)
                    {
                        preview.outputToSlot = 3;
                    }
                }

                if (belt.connectedBuildingId != 0 && pastedEntities.TryGetValue(belt.connectedBuildingId, out PastedEntity otherBuilding))
                {
                    if (belt.connectedBuildingIsOutput)
                    {
                        preview.output = otherBuilding.buildPreview;
                        preview.outputToSlot = belt.connectedBuildingSlot;
                    }
                    else
                    {
                        preview.input = otherBuilding.buildPreview;
                        preview.inputFromSlot = belt.connectedBuildingSlot;
                    }
                }

                bool beltHasInput = pastedEntities.ContainsKey(belt.backInputId) || pastedEntities.ContainsKey(belt.leftInputId) || pastedEntities.ContainsKey(belt.rightInputId);
                bool beltHasOutput = pastedEntities.ContainsKey(belt.outputId);

                if (!beltHasInput || !beltHasOutput)
                {
                    int found = actionBuild.nearcdLogic.GetBuildingsInAreaNonAlloc(preview.lpos, 0.34f, _nearObjectIds, false);
                    for (int x = 0; x < found; x++)
                    {
                        int overlappingEntityId = _nearObjectIds[x];

                        if (overlappingEntityId <= 0) continue;

                        EntityData overlappingEntityData = actionBuild.factory.entityPool[overlappingEntityId];

                        if (overlappingEntityData.beltId <= 0) continue;

                        BeltComponent overlappingBelt = actionBuild.factory.cargoTraffic.beltPool[overlappingEntityData.beltId];

                        bool overlappingBeltHasInput = (overlappingBelt.backInputId + overlappingBelt.leftInputId + overlappingBelt.rightInputId) != 0;
                        bool overlappingBeltHasOutput = overlappingBelt.outputId != 0;

                        if ((beltHasOutput && !overlappingBeltHasOutput) || (beltHasInput && !overlappingBeltHasInput))
                        {
                            // found overlapping belt that can be 'replaced' to connect to existing belts
                            preview.coverObjId = overlappingEntityId;
                            preview.ignoreCollider = true;
                            preview.willCover = true;
                        }
                    }

                }
            }


            return previews;
        }

        public static void PasteBuilding(int index, BuildingCopy building, Vector2 targetSpr, float yaw)
        {

            Vector2 newRelative = building.cursorRelativePos.Rotate(yaw * Mathf.Deg2Rad, building.originalSegmentCount);
            Vector2 sprPos = newRelative + targetSpr;

            float rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * 200;
            int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            int newSegmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);

            float sizeDeviation = building.originalSegmentCount / (float)newSegmentCount;

            sprPos = new Vector2(newRelative.x, newRelative.y * sizeDeviation) + targetSpr;

            Vector3 absoluteBuildingPos = sprPos.SnapToGrid();

            Quaternion absoluteBuildingRot = Maths.SphericalRotation(absoluteBuildingPos, yaw + building.cursorRelativeYaw);
            PrefabDesc desc = GetPrefabDesc(building);
            BuildPreview bp = BuildPreview.CreateSingle(building.itemProto, desc, true);
            bp.ResetInfos();
            bp.desc = desc;
            bp.item = building.itemProto;
            bp.recipeId = building.recipeId;
            bp.lpos = absoluteBuildingPos;
            bp.lrot = absoluteBuildingRot;

            Pose pose = new Pose(absoluteBuildingPos, absoluteBuildingRot);

            int objId = InserterPoses.AddOverride(pose, building.itemProto);

            pastedEntities.Add(building.originalId, new PastedEntity()
            {
                type = EPastedType.BUILDING,
                index = index,
                sourceBuilding = building,
                pose = pose,
                objId = objId,
                buildPreview = bp
            });

            ActivateColliders(absoluteBuildingPos);
            previews.Add(bp);
        }

        public static void PasteBelt(int index, BeltCopy belt, Vector2 targetSpr, float yaw)
        {
            Vector2 newRelative = belt.cursorRelativePos.Rotate(yaw * Mathf.Deg2Rad, belt.originalSegmentCount);
            Vector2 sprPos = newRelative + targetSpr;

            float rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * 200;
            int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            int newSegmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);

            float sizeDeviation = belt.originalSegmentCount / (float)newSegmentCount;

            sprPos = new Vector2(newRelative.x, newRelative.y * sizeDeviation) + targetSpr;

            Vector3 absoluteBeltPos = sprPos.SnapToGrid(belt.altitude * 1.3333333f / 2);

            // belts have always 0 yaw
            Quaternion absoluteBeltRot = Maths.SphericalRotation(absoluteBeltPos, 0f);

            BuildPreview bp = BuildPreview.CreateSingle(belt.itemProto, belt.itemProto.prefabDesc, false);

            bp.lpos = absoluteBeltPos;
            bp.lrot = absoluteBeltRot;
            bp.outputToSlot = -1;
            bp.outputFromSlot = 0;

            bp.inputFromSlot = -1;
            bp.inputToSlot = 1;

            bp.outputOffset = 0;
            bp.inputOffset = 0;

            Pose pose = new Pose(absoluteBeltPos, absoluteBeltRot);

            int objId = InserterPoses.AddOverride(pose, belt.itemProto);

            pastedEntities.Add(belt.originalId, new PastedEntity()
            {
                type = EPastedType.BELT,
                index = index,
                pose = pose,
                objId = objId,
                buildPreview = bp,
            });

            previews.Add(bp);
        }

        public static void PasteInserter(PlayerAction_Build actionBuild, int index, InserterCopy inserter, float yaw)
        {
            InserterPosition positionData = InserterPoses.GetPositions(actionBuild, inserter, yaw * Mathf.Deg2Rad);

            BuildPreview bp = BuildPreview.CreateSingle(LDB.items.Select(inserter.itemProto.ID), inserter.itemProto.prefabDesc, true);

            bp.lpos = positionData.absoluteInserterPos;
            bp.lpos2 = positionData.absoluteInserterPos2;

            bp.lrot = positionData.absoluteInserterRot;
            bp.lrot2 = positionData.absoluteInserterRot2;

            bp.inputToSlot = 1;
            bp.outputFromSlot = 0;

            bp.inputOffset = positionData.pickOffset;
            bp.outputOffset = positionData.insertOffset;
            bp.outputToSlot = positionData.endSlot;
            bp.inputFromSlot = positionData.startSlot;
            bp.condition = positionData.condition;

            bp.filterId = inserter.filterId;

            if (pastedEntities.TryGetValue(positionData.inputOriginalId, out PastedEntity inputEntity))
            {
                bp.input = inputEntity.buildPreview;
            }
            else
            {
                bp.inputObjId = positionData.inputObjId;
            }

            if (pastedEntities.TryGetValue(positionData.outputOriginalId, out PastedEntity outputEntity))
            {
                bp.output = outputEntity.buildPreview;
            }
            else
            {
                bp.outputObjId = positionData.outputObjId;
            }

            lock (parallelPastedEntities)
            {
                parallelPastedEntities.Add(inserter.originalId, new PastedEntity()
                {
                    type = EPastedType.INSERTER,
                    index = index,
                    buildPreview = bp,
                });
            }
        }

        public static void ActivateColliders(Vector3 center)
        {
            NearColliderLogic nearCdLogic = GameMain.data.mainPlayer.controller.actionBuild.nearcdLogic;

            nearCdLogic.activeColHashCount = 0;
            nearCdLogic.MarkActivePos(center);

            const float sqrAreaRadius = 4f * 4f;

            for (int i = 0; i < nearCdLogic.activeColHashCount; i++)
            {
                int hash = nearCdLogic.activeColHashes[i];
                ColliderData[] colliderPool = nearCdLogic.colChunks[hash].colliderPool;

                for (int j = 1; j < nearCdLogic.colChunks[hash].cursor; j++)
                {
                    var collider = colliderPool[j];
                    if (collider.idType != 0 &&
                        (collider.pos - center).sqrMagnitude <= sqrAreaRadius + collider.ext.sqrMagnitude &&
                        (collider.usage != EColliderUsage.Physics || collider.objType != EObjectType.Entity))
                    {
                        int colliderObjId = hash << 20 | j;
                        if (nearCdLogic.colliderObjs.ContainsKey(colliderObjId))
                        {
                            nearCdLogic.colliderObjs[colliderObjId].live = true;
                        }
                        else
                        {
                            nearCdLogic.colliderObjs[colliderObjId] = new ColliderObject(colliderObjId, collider);
                        }
                    }
                }
            }
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
