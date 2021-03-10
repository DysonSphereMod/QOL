using BepInEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public class BuildingCopy
    {
        public ItemProto itemProto;
        public int originalId = 0;
        public Vector3 originalPos;
        public Quaternion originalRot;
        public Quaternion inverseRot;

        public Vector3 cursorRelativePos = Vector3.zero;
        public float cursorRelativeYaw = 0f;
        public int snapCount = 0;
        public Vector3[] snapMoves;


        public int recipeId;
    }

    public class InserterCopy
    {
        public int pickTarget;
        public int insertTarget;

        public int originalId = 0;
        public int referenceBuildingId = 0;
        public ItemProto itemProto;

        public bool incoming;
        public int startSlot;
        public int endSlot;
        public Vector3 posDelta;
        public Vector3 pos2Delta;
        public Quaternion rot;
        public Quaternion rot2;
        public int findOtherSnapCount;
        public Vector3[] findOtherSnapMoves;
        public short pickOffset;
        public short insertOffset;
        public short t1;
        public short t2;
        public int filterId;
        public int refCount;
        public bool otherIsBelt;
    }

    public class InserterPosition
    {
        public InserterCopy copiedInserter;
        public Vector3 absoluteBuildingPos;
        public Quaternion absoluteBuildingRot;

        public Vector3 absoluteInserterPos;
        public Vector3 absoluteInserterPos2;
        public Quaternion absoluteInserterRot;
        public Quaternion absoluteInserterRot2;

        public Vector3 posDelta;
        public Vector3 pos2Delta;

        public int startSlot;
        public int endSlot;

        public short pickOffset;
        public short insertOffset;

        public int inputObjId;
        public int outputObjId;

        public EBuildCondition? condition;
    }

    

    public class Blueprint : BaseUnityPlugin
    {
        public static Vector3 referencePos = Vector3.zero;
        public static Quaternion inverseReferenceRot = Quaternion.identity;
        public static Dictionary<int, BuildingCopy> copiedBuildings = new Dictionary<int, BuildingCopy>();
        public static Dictionary<int, InserterCopy> copiedInserters = new Dictionary<int, InserterCopy>();

        public static Dictionary<int, BuildPreview> previews = new Dictionary<int, BuildPreview>();
        public static Dictionary<int, Vector3> positions = new Dictionary<int, Vector3>();
        public static Dictionary<int, Pose> poses = new Dictionary<int, Pose>();
        public static Dictionary<int, int> objIds = new Dictionary<int, int>();

        public static void Reset()
        {
            referencePos = Vector3.zero;
            inverseReferenceRot = Quaternion.identity;

            copiedBuildings.Clear();
            copiedInserters.Clear();
        }

        public static Queue<InserterPosition> currentPositionCache;
        public static Queue<InserterPosition> nextPositionCache;

        private static int[] _nearObjectIds = new int[4096];
        private static void SwapPositionCache()
        {
            currentPositionCache = nextPositionCache;
            nextPositionCache = new Queue<InserterPosition>();
        }

        private static InserterPosition GetPositions(InserterCopy copiedInserter, bool useCache = true)
        {
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;
            Vector3 absoluteBuildingPos;
            Quaternion absoluteBuildingRot;

            // When using AdvancedBuildDestruct mod, all buildPreviews are positioned 'absolutely' on the planet surface.
            // In 'normal' mode the buildPreviews are relative to __instance.previewPose.
            // This means that in 'normal' mode the (only) buildPreview is always positioned at {0,0,0}

            var buildPreview = previews[copiedInserter.referenceBuildingId];

            absoluteBuildingPos = poses[copiedInserter.referenceBuildingId].position;
            absoluteBuildingRot = poses[copiedInserter.referenceBuildingId].rotation;


            InserterPosition position = null;
/*            if (useCache && currentPositionCache.Count > 0)
            {
                position = currentPositionCache.Dequeue();
            }

            bool isCacheValid = position != null &&
                position.copiedInserter == copiedInserter &&
                position.absoluteBuildingPos == absoluteBuildingPos &&
                position.absoluteBuildingRot == absoluteBuildingRot;

            if (isCacheValid)
            {
                nextPositionCache.Enqueue(position);
                return position;
            }
*/

            var posDelta = copiedInserter.posDelta;
            var pos2Delta = copiedInserter.pos2Delta;

            Vector3 absoluteInserterPos = absoluteBuildingPos + absoluteBuildingRot * copiedInserter.posDelta;
            Vector3 absoluteInserterPos2 = absoluteBuildingPos + absoluteBuildingRot * copiedInserter.pos2Delta;

            Quaternion absoluteInserterRot = absoluteBuildingRot * copiedInserter.rot;
            Quaternion absoluteInserterRot2 = absoluteBuildingRot * copiedInserter.rot2;

            int startSlot = copiedInserter.startSlot;
            int endSlot = copiedInserter.endSlot;

            short pickOffset = copiedInserter.pickOffset;
            short insertOffset = copiedInserter.insertOffset;

            var buildingId = objIds[copiedInserter.referenceBuildingId];
            var otherId = 0;

            if (previews.ContainsKey(copiedInserter.pickTarget) && previews.ContainsKey(copiedInserter.insertTarget))
            {
                // cool we copied both source and target of the inserters

                var otherBuildingId = copiedInserter.pickTarget == copiedInserter.referenceBuildingId ? copiedInserter.insertTarget : copiedInserter.pickTarget;
                otherId = objIds[otherBuildingId];
            }
            else
            {
                // Find the other entity at the target location
                var planetAux = GameMain.data.mainPlayer.planetData.aux;
                var nearcdLogic = actionBuild.nearcdLogic;
                var factory = actionBuild.factory;
                // Find the desired belt/building position
                // As delta doesn't work over distance, re-trace the Grid Snapped steps from the original
                // to find the target belt/building for this inserters other connection
                var testPos = absoluteBuildingPos;
                // Note: rotates each move relative to the rotation of the new building
                for (int u = 0; u < copiedInserter.findOtherSnapCount; u++)
                    testPos = planetAux.Snap(testPos + absoluteBuildingRot * copiedInserter.findOtherSnapMoves[u], true, false);


                // find building nearby
                int found = nearcdLogic.GetBuildingsInAreaNonAlloc(testPos, 0.2f, _nearObjectIds);

                // find nearest building
                float maxDistance = 0.2f;
                for (int x = 0; x < found; x++)
                {
                    var id = _nearObjectIds[x];
                    float distance;
                    ItemProto proto;
                    if (id == 0 || id == buildPreview.objId)
                    {
                        continue;
                    }
                    else if (id > 0)
                    {
                        EntityData entityData = factory.entityPool[id];
                        proto = LDB.items.Select((int)entityData.protoId);
                        distance = Vector3.Distance(entityData.pos, testPos);
                    }
                    else
                    {
                        PrebuildData prebuildData = factory.prebuildPool[-id];
                        proto = LDB.items.Select((int)prebuildData.protoId);
                        if (proto.prefabDesc.isBelt)
                        {
                            // ignore unbuilt belts
                            continue;
                        }
                        distance = Vector3.Distance(prebuildData.pos, testPos);
                    }

                    // ignore entitites that ore not (built) belts or don't have inserterPoses
                    if ((proto.prefabDesc.isBelt == copiedInserter.otherIsBelt || proto.prefabDesc.insertPoses.Length > 0) && distance < maxDistance)
                    {
                        otherId = id;
                        maxDistance = distance;
                    }

                }

            }
            if (otherId != 0)
            {
                
                if (copiedInserter.incoming)
                {
                    InserterPoses.CalculatePose(actionBuild, otherId, buildingId);
                }
                else
                {
                    InserterPoses.CalculatePose(actionBuild, buildingId, otherId);
                }

                bool hasNearbyPose = false;
                if (actionBuild.posePairs.Count > 0)
                {
                    float minDistance = 1000f;
                    PlayerAction_Build.PosePair bestFit = new PlayerAction_Build.PosePair();

                    for (int j = 0; j < actionBuild.posePairs.Count; ++j)
                    {
                        var posePair = actionBuild.posePairs[j];
                        if (
                            (copiedInserter.incoming && copiedInserter.endSlot != posePair.endSlot) ||
                            (!copiedInserter.incoming && copiedInserter.startSlot != posePair.startSlot)
                            )
                        {
                            continue;
                        }
                        float startDistance = Vector3.Distance(posePair.startPose.position, absoluteInserterPos);
                        float endDistance = Vector3.Distance(posePair.endPose.position, absoluteInserterPos2);
                        float poseDistance = startDistance + endDistance;

                        if (poseDistance < minDistance)
                        {
                            minDistance = poseDistance;
                            bestFit = posePair;
                            hasNearbyPose = true;
                        }
                    }
                    if (hasNearbyPose)
                    {
                        // if we were able to calculate a close enough sensible pose
                        // use that instead of the (visually) imprecise default

                        absoluteInserterPos = bestFit.startPose.position;
                        absoluteInserterPos2 = bestFit.endPose.position;

                        absoluteInserterRot = bestFit.startPose.rotation;
                        absoluteInserterRot2 = bestFit.endPose.rotation * Quaternion.Euler(0.0f, 180f, 0.0f);

                        pickOffset = (short)bestFit.startOffset;
                        insertOffset = (short)bestFit.endOffset;

                        startSlot = bestFit.startSlot;
                        endSlot = bestFit.endSlot;


                        posDelta = Quaternion.Inverse(absoluteBuildingRot) * (absoluteInserterPos - absoluteBuildingPos);
                        pos2Delta = Quaternion.Inverse(absoluteBuildingRot) * (absoluteInserterPos2 - absoluteBuildingPos);
                    }
                }
            }

            position = new InserterPosition()
            {
                copiedInserter = copiedInserter,
                absoluteBuildingPos = absoluteBuildingPos,
                absoluteBuildingRot = absoluteBuildingRot,

                posDelta = posDelta,
                pos2Delta = pos2Delta,
                absoluteInserterPos = absoluteInserterPos,
                absoluteInserterPos2 = absoluteInserterPos2,

                absoluteInserterRot = absoluteInserterRot,
                absoluteInserterRot2 = absoluteInserterRot2,

                pickOffset = pickOffset,
                insertOffset = insertOffset,

                startSlot = startSlot,
                endSlot = endSlot,
            };

            position.inputObjId = copiedInserter.incoming ? otherId : buildingId;
            position.outputObjId = copiedInserter.incoming ? buildingId : otherId;

            /*if (useCache)
            {
                nextPositionCache.Enqueue(position);
            }*/
            return position;
        }

        public static BuildingCopy copyAssembler(int sourceEntityId)
        {
            if (copiedBuildings.ContainsKey(sourceEntityId))
            {
                return copiedBuildings[sourceEntityId];
            }
            var factory = GameMain.data.localPlanet.factory;
            var planetAux = GameMain.data.mainPlayer.planetData.aux;
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            var sourceEntity = factory.entityPool[sourceEntityId];

            var sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

            if (sourceEntityProto.prefabDesc.isBelt || sourceEntityProto.prefabDesc.isInserter)
            {
                return null;
            }

            var sourcePos = sourceEntity.pos;
            var sourceRot = sourceEntity.rot;

            var copiedBuilding = new BuildingCopy()
            {
                originalId = sourceEntityId,
                itemProto = sourceEntityProto,
                originalPos = sourcePos,
                originalRot = sourceRot,
            };


            if (!sourceEntityProto.prefabDesc.isAssembler)
            {
                copiedBuilding.recipeId = factory.factorySystem.assemblerPool[sourceEntity.assemblerId].recipeId;
            }

            if (copiedBuildings.Count == 0)
            {
                referencePos = sourcePos;
                inverseReferenceRot = Quaternion.Inverse(sourceRot);
            }
            else
            {

                copiedBuilding.cursorRelativePos = inverseReferenceRot * (copiedBuilding.originalPos - referencePos);
                int path = 0;
                Vector3[] snaps = new Vector3[1000];
                var snappedPointCount = planetAux.SnapLineNonAlloc(referencePos, copiedBuilding.originalPos, ref path, snaps);
                Vector3 lastSnap = referencePos;
                Vector3[] snapMoves = new Vector3[snappedPointCount];
                for (int s = 0; s < snappedPointCount; s++)
                {
                    // note: reverse rotation of the delta so that rotation works
                    Vector3 snapMove = inverseReferenceRot * (snaps[s] - lastSnap);
                    snapMoves[s] = snapMove;
                    lastSnap = snaps[s];
                }

                copiedBuilding.snapCount = snappedPointCount;
                copiedBuilding.snapMoves = snapMoves;
            }

            copiedBuildings.Add(copiedBuilding.originalId, copiedBuilding);

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

                    if (copiedInserters.ContainsKey(inserter.entityId)) continue;

                    var pickTarget = inserter.pickTarget;
                    var insertTarget = inserter.insertTarget;

                    if (pickTarget == sourceEntityId || insertTarget == sourceEntityId)
                    {
                        ItemProto itemProto = LDB.items.Select(inserterEntity.protoId);

                        bool incoming = insertTarget == sourceEntityId;
                        var otherId = incoming ? pickTarget : insertTarget; // The belt or other building this inserter is attached to
                        Vector3 otherPos;
                        ItemProto otherProto;

                        if (otherId > 0)
                        {
                            otherPos = entityPool[otherId].pos;
                            otherProto = LDB.items.Select((int)entityPool[otherId].protoId);
                        }
                        else
                        {
                            otherPos = prebuildPool[-otherId].pos;
                            otherProto = LDB.items.Select((int)entityPool[-otherId].protoId);
                        }

                        // Store the Grid-Snapped moves from assembler to belt/other
                        int path = 0;
                        Vector3[] snaps = new Vector3[6];
                        var snappedPointCount = planetAux.SnapLineNonAlloc(sourcePos, otherPos, ref path, snaps);
                        Vector3 lastSnap = sourcePos;
                        Vector3[] snapMoves = new Vector3[snappedPointCount];
                        for (int s = 0; s < snappedPointCount; s++)
                        {
                            // note: reverse rotation of the delta so that rotation works
                            Vector3 snapMove = Quaternion.Inverse(sourceRot) * (snaps[s] - lastSnap);
                            snapMoves[s] = snapMove;
                            lastSnap = snaps[s];
                        }

                        bool otherIsBelt = otherProto != null && otherProto.prefabDesc.isBelt;

                        // Cache info for this inserter
                        InserterCopy copiedInserter = new InserterCopy
                        {
                            itemProto = itemProto,
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
                            
                            findOtherSnapMoves = snapMoves,
                            findOtherSnapCount = snappedPointCount,

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
                                }
                            }
                        }

                        Debug.Log(copiedInserter.originalId);
                        copiedInserters.Add(copiedInserter.originalId, copiedInserter);
                    }
                }
            }

            return copiedBuilding;
        }

        public static List<BuildPreview> toBuildPreviews(Vector3 targetPos, float yaw, out List<Vector3> absolutePositions, int idMultiplier = 1)
        {
            previews.Clear();
            positions.Clear();
            objIds.Clear();
            poses.Clear();

            var planetAux = GameMain.data.mainPlayer.planetData.aux;
            // __instance.groundSnappedPos / __instance.yaw
            var inversePreviewRot = Quaternion.Inverse(Maths.SphericalRotation(targetPos, yaw));

            var absoluteTargetRot = Maths.SphericalRotation(targetPos, yaw);

            InserterPoses.ResetBuildPreviewsData();
            foreach (var building in copiedBuildings.Values)
            {
                
                var absoluteBuildingPos = planetAux.Snap(targetPos + absoluteTargetRot * building.cursorRelativePos, true, true);
                var absoluteBuildingRot = Maths.SphericalRotation(absoluteBuildingPos, yaw + building.cursorRelativeYaw);

                if (building.snapCount > 0)
                {
                    absoluteBuildingPos = targetPos;
                    // Note: rotates each move relative to the rotation of the new building
                    for (int u = 0; u < building.snapCount; u++)
                        absoluteBuildingPos = planetAux.Snap(absoluteBuildingPos + absoluteTargetRot * building.snapMoves[u], true, false);
                }

                
                BuildPreview bp = BuildPreview.CreateSingle(building.itemProto, building.itemProto.prefabDesc, true);
                bp.ResetInfos();
                bp.desc = building.itemProto.prefabDesc;
                bp.item = building.itemProto;
                bp.recipeId = building.recipeId;
                bp.lpos = inversePreviewRot * (absoluteBuildingPos - targetPos);
                bp.lrot = inversePreviewRot * absoluteBuildingRot;

                var pose = new Pose(absoluteBuildingPos, absoluteBuildingRot);

                var objId = InserterPoses.addOverride(pose, building.itemProto);

                positions.Add(building.originalId, absoluteBuildingPos);
                previews.Add(building.originalId, bp);
                objIds.Add(building.originalId, objId);
                poses.Add(building.originalId, pose);
            }
            foreach (var copiedInserter in copiedInserters.Values)
            {
                var positionData = GetPositions(copiedInserter);

                var bp = BuildPreview.CreateSingle(LDB.items.Select(copiedInserter.itemProto.ID), copiedInserter.itemProto.prefabDesc, true);
                bp.ResetInfos();

                var buildPreview = previews[copiedInserter.referenceBuildingId];

                bp.lrot = buildPreview.lrot * copiedInserter.rot;
                bp.lrot2 = buildPreview.lrot * copiedInserter.rot2;

                bp.lpos = buildPreview.lpos + buildPreview.lrot * positionData.posDelta;
                bp.lpos2 = buildPreview.lpos + buildPreview.lrot * positionData.pos2Delta;
                /* if (buildPreview.lpos == Vector3.zero)
                 {

                 }
                 else
                 {
                     bp.lpos = positionData.absoluteInserterPos;
                     bp.lpos2 = positionData.absoluteInserterPos2;
                 }*/

                bp.inputObjId = positionData.inputObjId;
                bp.outputObjId = positionData.outputObjId;
                previews.Add(copiedInserter.originalId, bp);
            }

            absolutePositions = positions.Values.ToList();
            return previews.Values.ToList();
        }

    }
}
