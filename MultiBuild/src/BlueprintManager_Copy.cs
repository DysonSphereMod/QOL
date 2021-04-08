using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    class BlueprintManager_Copy
    {
        public static bool Copy(BlueprintData data, List<int> entityIds)
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
                return false;
            }

            EntityData globalReference = buildings.Count > 0 ? buildings.First() : belts.Values.First();
            foreach (EntityData building in buildings) CopyBuilding(data, building, globalReference);
            foreach (EntityData belt in belts.Values) CopyBelt(data, belt, globalReference);

            return true;
        }

        public static BeltCopy CopyBelt(BlueprintData data, EntityData sourceEntity, EntityData referenceEntity)
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
                    CopyInserter(data, inserterEntity, sourceEntity);
                }
            }

            return copiedBelt;
        }

        public static BuildingCopy CopyBuilding(BlueprintData data, EntityData sourceEntity, EntityData referenceEntity)
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

                copiedBuilding.stationConfig = new StationConfig()
                {
                    workEnergyPerTick = factory.powerSystem.consumerPool[stationComponent.pcId].workEnergyPerTick,
                    tripRangeDrones = stationComponent.tripRangeDrones,
                    tripRangeShips = stationComponent.tripRangeShips,
                    warpEnableDist = stationComponent.warpEnableDist,
                    warperNecessary = stationComponent.warperNecessary,
                    includeOrbitCollector = stationComponent.includeOrbitCollector,
                    deliveryDrones = stationComponent.deliveryDrones,
                    deliveryShips = stationComponent.deliveryShips
                };

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
                var splitterComponent = factory.cargoTraffic.splitterPool[sourceEntity.splitterId];
                copiedBuilding.splitterSettings = new SplitterSettings()
                {
                    inPriority = splitterComponent.inPriority,
                    outPriority = splitterComponent.outPriority,
                    outFilter = splitterComponent.outFilter

                };

                var slots = new List<int>(4) { splitterComponent.beltA, splitterComponent.beltB, splitterComponent.beltC, splitterComponent.beltD };
                if (copiedBuilding.splitterSettings.inPriority)
                {
                    copiedBuilding.splitterSettings.inPrioritySlot = slots.IndexOf(splitterComponent.input0);
                }
                if (copiedBuilding.splitterSettings.outPriority)
                {
                    copiedBuilding.splitterSettings.outPrioritySlot = slots.IndexOf(splitterComponent.output0);
                }
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
                    CopyInserter(data, inserterEntity, sourceEntity);
                }
            }

            return copiedBuilding;
        }

        public static InserterCopy CopyInserter(BlueprintData data, EntityData sourceEntity, EntityData referenceEntity)
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

    }
}
