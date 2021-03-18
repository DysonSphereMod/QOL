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

        private static float lastMaxWidth = 0;

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

            lastMaxWidth = 0;

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

            int snappedPointCount = GameMain.data.mainPlayer.planetData.aux.SnapLineNonAlloc(from, to, ref path, _snaps);
            Vector3 lastSnap = from;
            var snapMoves = new List<Vector3>();
            for (int s = 0; s < snappedPointCount; s++)
            {
                // note: reverse rotation of the delta so that rotation works
                Vector3 snapMove = inverseFromRotation * (_snaps[s] - lastSnap);

                snapMove.x = (float) Math.Round(snapMove.x * Vector3Converter.JSON_PRECISION) / Vector3Converter.JSON_PRECISION;
                snapMove.y = (float) Math.Round(snapMove.y * Vector3Converter.JSON_PRECISION) / Vector3Converter.JSON_PRECISION;
                snapMove.z = (float) Math.Round(snapMove.z * Vector3Converter.JSON_PRECISION) / Vector3Converter.JSON_PRECISION;

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
            Vector3 targetPos = from;
            PlanetAuxData planetAux = GameMain.data.mainPlayer.planetData.aux;
            // Note: rotates each move relative to the rotation of the from
            foreach (Vector3 move in moves)
                targetPos = planetAux.Snap(targetPos + fromRotation * move, true, false);

            return targetPos;
        }

        public static int GetBeltInputEntityId(EntityData belt)
        {
            PlanetFactory factory = GameMain.data.localPlanet.factory;
            return factory.cargoTraffic.beltPool[factory.cargoTraffic.beltPool[belt.beltId].backInputId].entityId;
        }

        public static int GetBeltOutputEntityId(EntityData belt)
        {
            PlanetFactory factory = GameMain.data.localPlanet.factory;
            return factory.cargoTraffic.beltPool[factory.cargoTraffic.beltPool[belt.beltId].outputId].entityId;
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

            EntityData globalReference = buildings.Count > 0 ? buildings.First() : belts.Values.First();
            foreach (EntityData building in buildings) CopyBuilding(building, globalReference);
            foreach (EntityData belt in belts.Values) CopyBelt(belt, globalReference);
        }

        public static void CopyEntity(EntityData sourceEntity, EntityData referenceEntity)
        {
            ItemProto sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (sourceEntityProto.prefabDesc.isBelt)
            {
                //Debug.Log($"{referenceEntity.id} -> {sourceEntity.id} [BELT]");
                CopyBelt(sourceEntity, referenceEntity);
            }
            else
            {
                //Debug.Log($"{referenceEntity.id} -> {sourceEntity.id} [BUILDING]");
                CopyBuilding(sourceEntity, referenceEntity);
            }
        }

        public static BeltCopy CopyBelt(EntityData sourceEntity, EntityData referenceEntity)
        {
            PlanetFactory factory = GameMain.data.localPlanet.factory;
            //PlanetAuxData planetAux = GameMain.data.mainPlayer.planetData.aux;
            //PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            ItemProto sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (!sourceEntityProto.prefabDesc.isBelt)
            {
                return null;
            }

            BeltComponent belt = factory.cargoTraffic.beltPool[sourceEntity.beltId];

            Vector3 sourcePos = sourceEntity.pos;
            Quaternion sourceRot = sourceEntity.rot;

            BeltCopy copiedBelt = new BeltCopy()
            {
                originalId = sourceEntity.id,
                protoId = sourceEntityProto.ID,
                itemProto = sourceEntityProto,
                //originalPos = sourcePos,
                //originalRot = sourceRot,

                backInputId = factory.cargoTraffic.beltPool[belt.backInputId].entityId,
                leftInputId = factory.cargoTraffic.beltPool[belt.leftInputId].entityId,
                rightInputId = factory.cargoTraffic.beltPool[belt.rightInputId].entityId,
                outputId = factory.cargoTraffic.beltPool[belt.outputId].entityId,
            };

            //Evaluate belt rotation
            /*Vector3 predictedDirection = Vector3.zero;

            if (copiedBelt.outputId != 0)
            {
                EntityData other = factory.entityPool[copiedBelt.outputId];
                predictedDirection = other.pos - sourcePos;
            }else if (copiedBelt.backInputId != 0)
            {
                EntityData other = factory.entityPool[copiedBelt.backInputId];
                predictedDirection = sourcePos - other.pos;
            }

            Quaternion originalRot = Quaternion.LookRotation(predictedDirection, sourcePos);
            
            Quaternion zeroRot = Maths.SphericalRotation(sourcePos, 0f);
            float yaw = -Vector3.SignedAngle(zeroRot.Forward(), originalRot.Forward(), zeroRot.Up());*/

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
                data.referencePos = sourcePos.ToSpherical();
                //data.inverseReferenceRot = Quaternion.Inverse(sourceRot);
                //copiedBelt.cursorRelativeYaw = yaw;
                
                Debug.Log($"Reference pos: {data.referencePos}, {data.referencePos.ToDegrees()}");
            }
            else
            {
                Vector2 sprPos = sourcePos.ToSpherical();
                float rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * 200;
                int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
                copiedBelt.originalSegmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);
                copiedBelt.cursorRelativePos = sprPos - data.referencePos;
                //copiedBelt.cursorRelativeYaw = yaw - data.referenceYaw;
            }

            data.copiedBelts.Add(copiedBelt);

            factory.ReadObjectConn(sourceEntity.id, 4, out isOutput, out otherId, out otherSlot);

            if (otherId != 0)
            {
                EntityData inserterEntity = factory.entityPool[otherId];
                CopyInserter(inserterEntity, sourceEntity);
            }


            hasData = true;

            return copiedBelt;
        }

        public static BuildingCopy CopyBuilding(EntityData sourceEntity, EntityData referenceEntity)
        {
            PlanetFactory factory = GameMain.data.localPlanet.factory;
            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            ItemProto sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (sourceEntityProto.prefabDesc.isBelt || sourceEntityProto.prefabDesc.isInserter || sourceEntityProto.prefabDesc.minerType != EMinerType.None)
            {
                return null;
            }

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


            if (sourceEntityProto.prefabDesc.isAssembler)
            {
                copiedBuilding.recipeId = factory.factorySystem.assemblerPool[sourceEntity.assemblerId].recipeId;
            }


            if (sourceEntityProto.prefabDesc.isStation)
            {
                StationComponent stationComponent = factory.transport.stationPool[sourceEntity.stationId];

                for (int i = 0; i < stationComponent.slots.Length; i++)
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

                for (int i = 0; i < stationComponent.storage.Length; i++)
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
                SplitterComponent splitterComponennt = factory.cargoTraffic.splitterPool[sourceEntity.splitterId];

                // TODO: find a way to restore splitter settings 
            }

            Vector2 sourceSpr = sourcePos.ToSpherical();

            if (sourceEntity.id == referenceEntity.id)
            {
                data.referencePos = sourceSpr;
                //data.inverseReferenceRot = Quaternion.Inverse(sourceRot);
                Debug.Log($"Reference pos: {data.referencePos}, {data.referencePos.ToDegrees()}");
            }
            else
            {
                float rawLatitudeIndex = (sourceSpr.x - Mathf.PI / 2) / 6.2831855f * 200;
                int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
                copiedBuilding.originalSegmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);

                copiedBuilding.cursorRelativePos = sourceSpr - data.referencePos;
                copiedBuilding.cursorRelativeYaw = yaw - data.referenceYaw;
            }

            data.copiedBuildings.Add(copiedBuilding);

            // Ignore building without inserter slots
            if (sourceEntityProto.prefabDesc.insertPoses.Length > 0)
            {
                for (int i = 0; i < sourceEntityProto.prefabDesc.insertPoses.Length; i++)
                {
                    factory.ReadObjectConn(sourceEntity.id, i, out bool _, out int otherObjId, out int _);

                    if (otherObjId != 0)
                    {
                        EntityData inserterEntity = factory.entityPool[otherObjId];
                        CopyInserter(inserterEntity, sourceEntity);
                    }
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

            Vector2 sourceSpr = referenceEntity.pos.ToSpherical();


            // The belt or other building this inserter is attached to
            ItemProto otherProto;

            if (otherId > 0)
            {
                otherProto = LDB.items.Select(factory.entityPool[otherId].protoId);
            }
            else if (otherId < 0)
            {
                otherProto = LDB.items.Select(factory.entityPool[-otherId].protoId);
            }
            else
            {
                otherProto = null;
            }

            // Store the Grid-Snapped moves from assembler to belt/other
            //Vector3[] movesFromReference = GetMovesBetweenPoints(sourcePos, otherPos, Quaternion.Inverse(sourceRot));

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
                posDelta = sourceEntity.pos.ToSpherical() - sourceSpr, // Delta from copied building to inserter pos
                pos2Delta = inserter.pos2.ToSpherical() - sourceSpr, // Delta from copied building to inserter pos2

                // store to restore inserter speed
                //refCount = Mathf.RoundToInt((inserter.stt - 0.499f) / itemProto.prefabDesc.inserterSTT),

                // not important?
                pickOffset = inserter.pickOffset,
                insertOffset = inserter.insertOffset,

                // needed for pose?
                //t1 = inserter.t1,
                //t2 = inserter.t2,

                filterId = inserter.filter,


                startSlot = -1,
                endSlot = -1,

                otherIsBelt = otherIsBelt
            };

            Vector2 sprPos = sourceEntity.pos.ToSpherical();
            float rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * 200;
            int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            copiedInserter.posDeltaCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);

            sprPos = inserter.pos2.ToSpherical();
            rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * 200;
            latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
            copiedInserter.pos2DeltaCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);


            // compute the start and end slot that the cached inserter uses
            InserterPoses.CalculatePose(actionBuild, pickTarget, insertTarget);

            if (actionBuild.posePairs.Count > 0)
            {
                float minDistance = 1000f;
                for (int j = 0; j < actionBuild.posePairs.Count; ++j)
                {
                    PlayerAction_Build.PosePair posePair = actionBuild.posePairs[j];
                    float startDistance = Vector3.Distance(posePair.startPose.position, sourceEntity.pos);
                    float endDistance = Vector3.Distance(posePair.endPose.position, inserter.pos2);
                    float poseDistance = startDistance + endDistance;

                    if (poseDistance < minDistance)
                    {
                        minDistance = poseDistance;
                        copiedInserter.startSlot = posePair.startSlot;
                        copiedInserter.endSlot = posePair.endSlot;

                        copiedInserter.pickOffset = (short) posePair.startOffset;
                        copiedInserter.insertOffset = (short) posePair.endOffset;
                    }
                }
            }

            data.copiedInserters.Add(copiedInserter);

            return copiedInserter;
        }

        public static List<BuildPreview> Paste(Vector3 targetPos, float yaw, bool pasteInserters = true)
        {
            pastedEntities.Clear();
            InserterPoses.ResetOverrides();

            //Quaternion absoluteTargetRot = Maths.SphericalRotation(targetPos, yaw);
            List<BuildPreview> previews = new List<BuildPreview>();
            List<Vector3> absolutePositions = new List<Vector3>();

            Vector2 targetSpr = targetPos.ToSpherical();
            float yawRad = yaw * Mathf.Deg2Rad;

            float currentMaxWidth = 0;

            for (int i = 0; i < data.copiedBuildings.Count; i++)
            {
                BuildingCopy building = data.copiedBuildings[i];
                Vector2 newRelative = building.cursorRelativePos.Rotate(yawRad, building.originalSegmentCount);
                Vector2 sprPos = newRelative + targetSpr;

                float rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * 200;
                int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
                int newSegmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);

                float sizeDeviation = building.originalSegmentCount / (float) newSegmentCount;
                if (sizeDeviation > currentMaxWidth)
                    currentMaxWidth = sizeDeviation;

                /* if (sizeDeviation < lastMaxWidth)
                    sizeDeviation = lastMaxWidth;*/

                sprPos = new Vector2(newRelative.x, newRelative.y * sizeDeviation) + targetSpr;

                Vector3 absoluteBuildingPos = sprPos.ToCartesian(GameMain.localPlanet.realRadius + 0.2f);

                absoluteBuildingPos = GameMain.data.mainPlayer.planetData.aux.Snap(absoluteBuildingPos, true, false);


                //Vector3 absoluteBuildingPos = GetPointFromMoves(targetPos, building.movesFromReference, absoluteTargetRot);
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


            for (int i = 0; i < data.copiedBelts.Count; i++)
            {
                BeltCopy belt = data.copiedBelts[i];
                Vector2 newRelative = belt.cursorRelativePos.Rotate(yawRad, belt.originalSegmentCount);
                Vector2 sprPos = newRelative + targetSpr;

                float rawLatitudeIndex = (sprPos.x - Mathf.PI / 2) / 6.2831855f * 200;
                int latitudeIndex = Mathf.FloorToInt(Mathf.Max(0f, Mathf.Abs(rawLatitudeIndex) - 0.1f));
                int newSegmentCount = PlanetGrid.DetermineLongitudeSegmentCount(latitudeIndex, 200);

                float sizeDeviation = belt.originalSegmentCount / (float) newSegmentCount;
                if (sizeDeviation > currentMaxWidth)
                    currentMaxWidth = sizeDeviation;

                /*if (sizeDeviation < lastMaxWidth)
                    sizeDeviation = lastMaxWidth;*/

                sprPos = new Vector2(newRelative.x, newRelative.y * sizeDeviation) + targetSpr;

                Vector3 absoluteBeltPos = sprPos.ToCartesian(GameMain.localPlanet.realRadius + 0.2f);

                absoluteBeltPos = GameMain.data.mainPlayer.planetData.aux.Snap(absoluteBeltPos, true, false);


                // belts have always 0 yaw
                Quaternion absoluteBeltRot = Maths.SphericalRotation(absoluteBeltPos, 0f);

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

                Pose pose = new Pose(absoluteBeltPos, absoluteBeltRot);

                int objId = InserterPoses.AddOverride(pose, belt.itemProto);

                pastedEntities.Add(belt.originalId, new PastedEntity()
                {
                    type = EPastedType.belt,
                    index = i,
                    pose = pose,
                    objId = objId,
                    buildPreview = bp,
                });
                //absolutePositions.Add(absoluteBeltPos);
                previews.Add(bp);
            }


            // after creating the belt previews this restore the correct connection to other belts and buildings
            foreach (BeltCopy belt in data.copiedBelts)
            {
                BuildPreview preview = pastedEntities[belt.originalId].buildPreview;

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

                if (belt.connectedBuildingId != 0 && pastedEntities.ContainsKey(belt.connectedBuildingId))
                {
                    if (belt.connectedBuildingIsOutput)
                    {
                        preview.output = pastedEntities[belt.connectedBuildingId].buildPreview;
                        preview.outputToSlot = belt.connectedBuildingSlot;
                    }
                    else
                    {
                        preview.input = pastedEntities[belt.connectedBuildingId].buildPreview;
                        preview.inputFromSlot = belt.connectedBuildingSlot;
                    }
                }
            }

            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            BuildLogic.ActivateColliders(ref actionBuild.nearcdLogic, absolutePositions);

            if (!pasteInserters)
            {
                lastMaxWidth = currentMaxWidth;
                return previews;
            }

            foreach (InserterCopy copiedInserter in data.copiedInserters)
            {
                InserterPosition positionData = InserterPoses.GetPositions(copiedInserter, yawRad);

                BuildPreview bp = BuildPreview.CreateSingle(LDB.items.Select(copiedInserter.itemProto.ID), copiedInserter.itemProto.prefabDesc, true);
                bp.ResetInfos();

                BuildPreview buildPreview = pastedEntities[copiedInserter.referenceBuildingId].buildPreview;

                bp.lpos = positionData
                    .absoluteInserterPos; //(buildPreview.lpos.ToSpherical() + positionData.posDelta).ToCartesian(GameMain.localPlanet.realRadius + 0.2f);
                bp.lpos2 = positionData
                    .absoluteInserterPos2; //(buildPreview.lpos.ToSpherical() + positionData.pos2Delta).ToCartesian(GameMain.localPlanet.realRadius + 0.2f);

                bp.lrot = positionData.absoluteInserterRot; //buildPreview.lrot * copiedInserter.rot;
                bp.lrot2 = positionData.absoluteInserterRot2; //buildPreview.lrot * copiedInserter.rot2;

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

            lastMaxWidth = currentMaxWidth;

            return previews;
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