using BepInEx;
using UnityEngine;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public enum EPastedType
    {
        building,
        belt,
        inserter
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

        const bool OPTIMISE_MOVES = true;
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
            data = new BlueprintData();
            pastedEntities.Clear();
            GC.Collect();

            UpdateUIText();
        }

        public static void Restore(BlueprintData newData = null)
        {
            if (hasData)
            {
                var temp = data;
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
                var name = previousData.name;
                if (name.Length > 25)
                {
                    name = name.Substring(0, 22) + "...";
                }
                UIFunctionPanelPatch.blueprintGroup.infoTitle.text += $" {name}";
            }
            var counter = new Dictionary<string, int>();

            foreach (var bulding in previousData.copiedBuildings)
            {
                var name = bulding.itemProto.name;
                if (!counter.ContainsKey(name)) counter.Add(name, 0);
                counter[name]++;
            }
            foreach (var belt in previousData.copiedBelts)
            {
                var name = "Belts";
                if (!counter.ContainsKey(name)) counter.Add(name, 0);
                counter[name]++;
            }
            foreach (var inserter in previousData.copiedInserters)
            {
                var name = "Inserters";
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

            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            // if no building use storage id as fake buildingId as we need something with buildmode == 1
            var firstItemProtoID = data.copiedBuildings.Count > 0 ?
                        data.copiedBuildings.First().itemProto.ID :
                        2101;

            actionBuild.yaw = data.referenceYaw;
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

        public static Vector3[] GetMovesBetweenPoints(Vector3 from, Vector3 to, Quaternion inverseFromRotation)
        {
            if (from == to)
            {
                return new Vector3[0];
            }

            int path = 0;

            var snappedPointCount = GameMain.data.mainPlayer.planetData.aux.SnapLineNonAlloc(from, to, ref path, _snaps);
            Vector3 lastSnap = from;
            var  snapMoves = new List<Vector3>();
            for (int s = 0; s < snappedPointCount; s++)
            {
                // note: reverse rotation of the delta so that rotation works
                Vector3 snapMove = inverseFromRotation * (_snaps[s] - lastSnap);

                snapMove.x = (float)Math.Round(snapMove.x * Vector3Converter.JSON_PRECISION) / Vector3Converter.JSON_PRECISION;
                snapMove.y = (float)Math.Round(snapMove.y * Vector3Converter.JSON_PRECISION) / Vector3Converter.JSON_PRECISION;
                snapMove.z = (float)Math.Round(snapMove.z * Vector3Converter.JSON_PRECISION) / Vector3Converter.JSON_PRECISION;

                if (snapMove != Vector3.zero)
                {
                    snapMoves.Add(snapMove);
                }
                lastSnap = _snaps[s];
            }
            return snapMoves.ToArray();
        }

        public static Vector3 GetPointFromMoves(Vector3 from, Vector3[] moves, Quaternion fromRotation)
        {
            var targetPos = from;
            var planetAux = GameMain.data.mainPlayer.planetData.aux;
            // Note: rotates each move relative to the rotation of the from
            for (int i = 0; i < moves.Length; i++)
                targetPos = planetAux.Snap(targetPos + fromRotation * moves[i], true, false);

            return targetPos;
        }

        public static int GetBeltInputEntityId(EntityData belt)
        {
            var factory = GameMain.data.localPlanet.factory;
            return factory.cargoTraffic.beltPool[factory.cargoTraffic.beltPool[belt.beltId].backInputId].entityId;
        }

        public static int GetBeltOutputEntityId(EntityData belt)
        {
            var factory = GameMain.data.localPlanet.factory;
            return factory.cargoTraffic.beltPool[factory.cargoTraffic.beltPool[belt.beltId].outputId].entityId;
        }

        public static void CopyEntities(List<int> entityIds)
        {
           

            var factory = GameMain.data.localPlanet.factory;

            var buildings = new List<EntityData>();
            var belts = new Dictionary<int, EntityData>();
            foreach (var id in entityIds)
            {
                var entity = factory.entityPool[id];
                var entityProto = LDB.items.Select(entity.protoId);

                if (entityProto.prefabDesc.isInserter || entityProto.prefabDesc.minerType != EMinerType.None) continue;

                if (!entityProto.prefabDesc.isBelt)
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

            if (OPTIMISE_MOVES)
            {

                if (buildings.Count > 0)
                {
                    int[,] distances = new int[buildings.Count, buildings.Count];
                    for (var i = 0; i < buildings.Count; i++)
                    {
                        for (var j = 0; j <= i; j++)
                        {
                            var distance = i == j ? 0 : (int)Math.Round(1000 * Vector3.Distance(buildings[i].pos, buildings[j].pos));
                            distances[i, j] = distance;
                            distances[j, i] = distance;
                        }
                    }

                    var parents = MinimunSpanningTree.Prim(distances, buildings.Count);

                    var stack = new Stack<int>();

                    CopyEntity(buildings[0], buildings[0]);
                    for (var i = 0; i < parents.Length; i++)
                    {
                        var id = i;
                        while (parents[id] != -1)
                        {
                            stack.Push(id);
                            id = parents[id];
                        }
                        while (stack.Count > 0)
                        {
                            var index = stack.Pop();

                            CopyEntity(buildings[index], buildings[parents[index]]);

                            parents[index] = -1;
                        }

                    }
                }

                if (belts.Count > 0)
                {
                    var found = new HashSet<int>();
                    var beltSegments = new List<LinkedList<EntityData>>();

                    foreach (var item in belts)
                    {
                        if (!found.Contains(item.Key))
                        {
                            var segment = new LinkedList<EntityData>();
                            found.Add(item.Value.id);
                            segment.AddFirst(item.Value);

                            bool next;

                            var currentBeltEntity = item.Value;
                            int inputEntityId;
                            do
                            {
                                inputEntityId = GetBeltInputEntityId(currentBeltEntity);
                                currentBeltEntity = factory.entityPool[inputEntityId];
                                if (belts.ContainsKey(inputEntityId) && !found.Contains(inputEntityId))
                                {
                                    found.Add(currentBeltEntity.id);
                                    segment.AddFirst(currentBeltEntity);

                                    next = true;
                                }
                                else
                                {
                                    next = false;
                                }
                            } while (next);

                            currentBeltEntity = item.Value;

                            int outputEntityId;
                            do
                            {
                                outputEntityId = GetBeltOutputEntityId(currentBeltEntity);
                                currentBeltEntity = factory.entityPool[outputEntityId];
                                if (belts.ContainsKey(outputEntityId) && !found.Contains(outputEntityId))
                                {
                                    found.Add(currentBeltEntity.id);
                                    segment.AddLast(currentBeltEntity);
                                    next = true;
                                }
                                else
                                {
                                    next = false;
                                }
                            } while (next);

                            beltSegments.Add(segment);

                            var mainReference = buildings.Count > 0 ? buildings[0] : beltSegments[0].First();

                            if (Vector3.Distance(mainReference.pos, segment.First().pos) < Vector3.Distance(mainReference.pos, segment.Last().pos))
                            {
                                var previousEntity = mainReference;
                                var currentItem = segment.First;
                                while (currentItem != null)
                                {
                                    CopyEntity(currentItem.Value, previousEntity);
                                    previousEntity = currentItem.Value;
                                    currentItem = currentItem.Next;
                                }
                                // from input to output
                            }
                            else
                            {
                                // from output to input
                                var previousEntity = mainReference;
                                var currentItem = segment.Last;
                                while (currentItem != null)
                                {
                                    CopyEntity(currentItem.Value, previousEntity);
                                    previousEntity = currentItem.Value;
                                    currentItem = currentItem.Previous;

                                }
                            }
                        }
                    }
                }

            } else
            {
                var globalReference = buildings.Count > 0 ? buildings.First() : belts.Values.First();
                foreach (var building in buildings) CopyBuilding(building, globalReference);
                foreach (var belt in belts.Values) CopyBelt(belt, globalReference);
            }
        }


        public static void CopyEntity(EntityData sourceEntity, EntityData referenceEntity)
        {
            var sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (sourceEntityProto.prefabDesc.isBelt)
            {
                //Debug.Log($"{referenceEntity.id} -> {sourceEntity.id} [BELT]");
                CopyBelt(sourceEntity, referenceEntity);
            } else {
                //Debug.Log($"{referenceEntity.id} -> {sourceEntity.id} [BUILDING]");
                CopyBuilding(sourceEntity, referenceEntity);
            }

        }
        public static BeltCopy CopyBelt(EntityData sourceEntity, EntityData referenceEntity)
        {

            var factory = GameMain.data.localPlanet.factory;

            var sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (!sourceEntityProto.prefabDesc.isBelt)
            {
                return null;
            }

            var belt = factory.cargoTraffic.beltPool[sourceEntity.beltId];

            var sourceRot = sourceEntity.rot;

            var copiedBelt = new BeltCopy()
            {
                originalId = sourceEntity.id,
                protoId = sourceEntityProto.ID,
                itemProto = sourceEntityProto,

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
                copiedBelt.referenceId = 0;
                copiedBelt.movesFromReference = new Vector3[0];

                data.inverseReferenceRot = Quaternion.Inverse(sourceRot);
            }
            else
            {
                copiedBelt.referenceId = referenceEntity.id;
                copiedBelt.movesFromReference = GetMovesBetweenPoints(referenceEntity.pos , sourceEntity.pos, data.inverseReferenceRot);
            }

            data.copiedBelts.Add(copiedBelt);
            hasData = true;

            return copiedBelt;
        }

        public static BuildingCopy CopyBuilding(EntityData sourceEntity, EntityData referenceEntity)
        {
            var factory = GameMain.data.localPlanet.factory;
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            var sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            var sourcePos = sourceEntity.pos;
            var sourceRot = sourceEntity.rot;

            Quaternion zeroRot = Maths.SphericalRotation(sourcePos, 0f);
            float yaw = Vector3.SignedAngle(zeroRot.Forward(), sourceRot.Forward(), zeroRot.Up());

            var copiedBuilding = new BuildingCopy()
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
                var stationComponent = factory.transport.stationPool[sourceEntity.stationId];

                for (var i = 0; i < stationComponent.slots.Length; i++)
                {
                    if (stationComponent.slots[i].storageIdx != 0)
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
            else if (sourceEntity.splitterId > 0)
            {
                var splitterComponent = factory.cargoTraffic.splitterPool[sourceEntity.splitterId];

                // TODO: find a way to restore splitter settings 
            }

            if (sourceEntity.id == referenceEntity.id)
            {
                copiedBuilding.referenceId = 0;
                copiedBuilding.movesFromReference = new Vector3[0];
                copiedBuilding.cursorRelativeYaw = 0;

                data.inverseReferenceRot = Quaternion.Inverse(sourceRot);
                data.referenceYaw = yaw;
            }
            else
            {
                copiedBuilding.referenceId = referenceEntity.id;
                copiedBuilding.movesFromReference = GetMovesBetweenPoints(referenceEntity.pos, sourceEntity.pos, data.inverseReferenceRot);
                copiedBuilding.cursorRelativeYaw = yaw - data.referenceYaw;
            }

            data.copiedBuildings.Add(copiedBuilding);

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

                    if (data.copiedInserters.FindIndex(x => x.originalId == inserter.entityId) != -1) continue;

                    var pickTarget = inserter.pickTarget;
                    var insertTarget = inserter.insertTarget;

                    if (pickTarget == sourceEntity.id || insertTarget == sourceEntity.id)
                    {
                        ItemProto itemProto = LDB.items.Select(inserterEntity.protoId);

                        bool incoming = insertTarget == sourceEntity.id;
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

                        data.copiedInserters.Add(copiedInserter);
                    }
                }
            }

            hasData = true;
            return copiedBuilding;
        }

        public static List<BuildPreview> Paste(Vector3 targetPos, float yaw, bool pasteInserters = true)
        {

            pastedEntities.Clear();
            InserterPoses.ResetOverrides();
            var totalEntities = data.copiedBuildings.Count + data.copiedBelts.Count + data.copiedInserters.Count;
            var absoluteTargetRot = Maths.SphericalRotation(targetPos, yaw);
            var previews = new List<BuildPreview>(totalEntities);
            var absolutePositions = new List<Vector3>(totalEntities);

            for (var i = 0; i < data.copiedBuildings.Count; i++)
            {
                var building = data.copiedBuildings[i];
                var referencePos = building.referenceId == 0 ? targetPos : pastedEntities[building.referenceId].pose.position;
                Vector3 absoluteBuildingPos = GetPointFromMoves(referencePos, building.movesFromReference, absoluteTargetRot);
                Quaternion absoluteBuildingRot = Maths.SphericalRotation(absoluteBuildingPos, yaw + building.cursorRelativeYaw);
                var desc = GetPrefabDesc(building);
                BuildPreview bp = BuildPreview.CreateSingle(building.itemProto, desc, true);
                bp.ResetInfos();
                bp.desc = desc;
                bp.item = building.itemProto;
                bp.recipeId = building.recipeId;
                bp.lpos = absoluteBuildingPos;
                bp.lrot = absoluteBuildingRot;

                var pose = new Pose(absoluteBuildingPos, absoluteBuildingRot);

                var objId = InserterPoses.AddOverride(pose, building.itemProto);

                pastedEntities.Add(building.originalId, new PastedEntity()
                {
                    type = EPastedType.building,
                    index = i,
                    sourceBuilding = building,
                    pose = pose,
                    objId = objId,
                    buildPreview = bp
                });
                absolutePositions.Add(absoluteBuildingPos);
                previews.Add(bp);
            }


            for (var i = 0; i < data.copiedBelts.Count; i++)
            {
                var belt = data.copiedBelts[i];
                var referencePos = belt.referenceId == 0 ? targetPos : pastedEntities[belt.referenceId].pose.position;
                var absoluteBeltPos = GetPointFromMoves(referencePos, belt.movesFromReference, absoluteTargetRot);

                // belts have always 0 yaw
                var absoluteBeltRot = Maths.SphericalRotation(absoluteBeltPos, 0f);

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

                var objId = InserterPoses.AddOverride(pose, belt.itemProto);

                pastedEntities.Add(belt.originalId, new PastedEntity()
                {
                    type = EPastedType.belt,
                    index = i,
                    pose = pose,
                    objId = objId,
                    buildPreview = bp
                });
                //absolutePositions.Add(absoluteBeltPos);
                previews.Add(bp);
            }

            // after creating the belt previews this restore the correct connection to other belts and buildings
            foreach (var belt in data.copiedBelts)
            {
                var preview = pastedEntities[belt.originalId].buildPreview;

                if (belt.outputId != 0 && pastedEntities.TryGetValue(belt.outputId, out PastedEntity otherEntity))
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
            }

            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            BuildLogic.ActivateColliders(ref actionBuild.nearcdLogic, absolutePositions);

            if (pasteInserters)
            {
                foreach (var copiedInserter in data.copiedInserters)
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
            }
            return previews;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ConnGizmoRenderer), "Update")]
        public static void ConnGizmoRenderer_Update_Postfix(ref ConnGizmoRenderer __instance)
        {
            if (BlueprintManager.pastedEntities.Count > 1)
            {
                var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;
                foreach (var preview in actionBuild.buildPreviews)
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

                    __instance.objs_1.Add(item);

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
                        __instance.objs_1.Add(item);

                        Vector3 vector2 = preview.lpos - preview.input.lpos;
                        if (vector2 != Vector3.zero)
                        {
                            item.rot = Quaternion.LookRotation(vector2.normalized, preview.input.lpos.normalized);
                            item.size = vector2.magnitude;
                            __instance.objs_2.Add(item);
                        }
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
}
