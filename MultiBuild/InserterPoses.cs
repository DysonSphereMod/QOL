using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public class InserterPosition
    {
        public InserterCopy copiedInserter;
        public Vector3 absoluteBuildingPos;
        public Quaternion absoluteBuildingRot;

        public Vector3 absoluteInserterPos;
        public Vector3 absoluteInserterPos2;
        public Quaternion absoluteInserterRot;
        public Quaternion absoluteInserterRot2;

        public Vector2 posDelta;
        public Vector2 pos2Delta;

        public int startSlot;
        public int endSlot;

        public short pickOffset;
        public short insertOffset;

        public int inputOriginalId;
        public int outputOriginalId;

        public int inputObjId;
        public int outputObjId;

        public EBuildCondition condition;
    }

    internal class BuildPreviewOverride
    {
        public Pose pose;
        public ItemProto itemProto;
    }

    [HarmonyPatch]
    internal class InserterPoses
    {
        private const int INITIAL_OBJ_ID = 2000000000;
        private static Collider[] _tmp_cols = new Collider[256];
        private static int[] _nearObjectIds = new int[4096];

        public static List<BuildPreviewOverride> overrides = new List<BuildPreviewOverride>();

        public static void ResetOverrides()
        {
            overrides.Clear();
        }

        public static int AddOverride(Pose pose, ItemProto itemProto)
        {
            overrides.Add(new BuildPreviewOverride()
            {
                pose = pose,
                itemProto = itemProto
            });

            return INITIAL_OBJ_ID + overrides.Count - 1;
        }

        public static InserterPosition GetPositions(InserterCopy copiedInserter, float yawRad)
        {
            var pastedEntities = BlueprintManager.pastedEntities;
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;
            var player = actionBuild.player;
            var pastedReferenceEntity = pastedEntities[copiedInserter.referenceBuildingId];
            var pastedReferenceEntityBuildPreview = pastedReferenceEntity.buildPreview;


            Vector3 absoluteBuildingPos = pastedReferenceEntity.pose.position;
            Vector2 absoluteBuildingPosSpr = absoluteBuildingPos.ToSpherical();
            
            Quaternion absoluteBuildingRot = pastedReferenceEntity.pose.rotation;

            var posDelta = copiedInserter.posDelta.Rotate(yawRad, copiedInserter.posDeltaCount);
            Vector3 absoluteInserterPos = absoluteBuildingPosSpr
                .ApplyDelta(posDelta, copiedInserter.posDeltaCount)
                .ToCartesian(GameMain.localPlanet.realRadius + 0.2f);

            var pos2Delta = copiedInserter.pos2Delta.Rotate(yawRad, copiedInserter.pos2DeltaCount);
            Vector3 absoluteInserterPos2 = absoluteBuildingPosSpr
                .ApplyDelta(pos2Delta, copiedInserter.pos2DeltaCount)
                .ToCartesian(GameMain.localPlanet.realRadius + 0.2f);

            if (pastedReferenceEntity.sourceBuilding == null)
            {
               absoluteBuildingRot = Maths.SphericalRotation(absoluteBuildingPos,  yawRad * Mathf.Rad2Deg);
            }

            Quaternion absoluteInserterRot = absoluteBuildingRot * copiedInserter.rot ;
            Quaternion absoluteInserterRot2 = absoluteBuildingRot * copiedInserter.rot2 ;

            int startSlot = copiedInserter.startSlot;
            int endSlot = copiedInserter.endSlot;

            short pickOffset = copiedInserter.pickOffset;
            short insertOffset = copiedInserter.insertOffset;

            var referenceId = copiedInserter.referenceBuildingId;
            var referenceObjId = pastedReferenceEntity.objId;

            var otherId = 0;
            var otherObjId = 0;
            if (pastedEntities.ContainsKey(copiedInserter.pickTarget) && pastedEntities.ContainsKey(copiedInserter.insertTarget))
            {
                // cool we copied both source and target of the inserters
                otherId = copiedInserter.pickTarget == copiedInserter.referenceBuildingId ? copiedInserter.insertTarget : copiedInserter.pickTarget;
                otherObjId = pastedEntities[otherId].objId;
            }
            else
            {
                // Find the other entity at the target location
                var nearcdLogic = actionBuild.nearcdLogic;
                var factory = actionBuild.factory;
                // Find the desired belt/building position
                // As delta doesn't work over distance, re-trace the Grid Snapped steps from the original
                // to find the target belt/building for this inserters other connection

                var otherPosDelta = copiedInserter.otherPosDelta.Rotate(yawRad, copiedInserter.otherPosDeltaCount);
                Vector3 testPos = absoluteBuildingPosSpr
                    .ApplyDelta(otherPosDelta, copiedInserter.otherPosDeltaCount)
                    .SnapToGrid();

                // find building nearby
                int found = nearcdLogic.GetBuildingsInAreaNonAlloc(testPos, 0.2f, _nearObjectIds, false);

                // find nearest building
                float maxDistance = 1f;

                for (int x = 0; x < found; x++)
                {
                    var id = _nearObjectIds[x];
                    float distance;
                    ItemProto proto;
                    if (id == 0 || id == pastedReferenceEntityBuildPreview.objId)
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
                        otherId = otherObjId = id;
                        maxDistance = distance;
                    }
                }
            }
            if (otherObjId != 0)
            {
                if (copiedInserter.incoming)
                {
                    InserterPoses.CalculatePose(actionBuild, otherObjId, referenceObjId);
                }
                else
                {
                    InserterPoses.CalculatePose(actionBuild, referenceObjId, otherObjId);
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
                            (copiedInserter.incoming && copiedInserter.endSlot != posePair.endSlot && copiedInserter.endSlot != -1) ||
                            (!copiedInserter.incoming && copiedInserter.startSlot != posePair.startSlot && copiedInserter.startSlot != -1)
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

                        posDelta = bestFit.startPose.position.ToSpherical() - absoluteBuildingPosSpr;
                        pos2Delta = bestFit.endPose.position.ToSpherical() - absoluteBuildingPosSpr;
                    }
                }
            }

            InserterPosition position = new InserterPosition()
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

                condition = EBuildCondition.Ok
            };

            if (!pastedEntities.ContainsKey(otherId))
            {
                Vector3 forward = absoluteInserterPos2 - absoluteInserterPos;

                Pose pose;
                pose.position = Vector3.Lerp(absoluteInserterPos, absoluteInserterPos2, 0.5f);
                pose.rotation = Quaternion.LookRotation(forward, absoluteInserterPos.normalized);


                var colliderData = copiedInserter.itemProto.prefabDesc.buildColliders[0];
                colliderData.ext = new Vector3(colliderData.ext.x, colliderData.ext.y, Vector3.Distance(absoluteInserterPos2, absoluteInserterPos) * 0.5f + colliderData.ext.z - 0.5f);

                if (copiedInserter.otherIsBelt)
                {
                    if (copiedInserter.incoming)
                    {
                        colliderData.pos.z -= 0.4f;
                        colliderData.ext.z += 0.4f;
                    }
                    else
                    {
                        colliderData.pos.z += 0.4f;
                        colliderData.ext.z += 0.4f;
                    }
                }

                if (colliderData.ext.z < 0.1f)
                {
                    colliderData.ext.z = 0.1f;
                }
                colliderData.pos = pose.position + pose.rotation * colliderData.pos;
                colliderData.q = pose.rotation * colliderData.q;


                int mask = 165888;
                int collisionsFound = Physics.OverlapBoxNonAlloc(colliderData.pos, colliderData.ext, _tmp_cols, colliderData.q, mask, QueryTriggerInteraction.Collide);

                PlanetPhysics physics2 = player.planetData.physics;
                for (int j = 0; j < collisionsFound; j++)
                {
                    physics2.GetColliderData(_tmp_cols[j], out ColliderData colliderData2);
                    if (colliderData2.objId != 0 && colliderData2.objId != otherId && colliderData2.usage == EColliderUsage.Build)
                    {
                        position.condition = EBuildCondition.Collide;
                        otherId = 0;
                        otherObjId = 0;

                        break;
                    }
                }
            }

            position.inputObjId = copiedInserter.incoming ? otherObjId : referenceObjId;
            position.inputOriginalId = copiedInserter.incoming ? otherId : referenceId;

            position.outputObjId = copiedInserter.incoming ? referenceObjId : otherObjId;
            position.outputOriginalId = copiedInserter.incoming ? referenceId : otherId;

            return position;
        }

#pragma warning disable IDE0060 // Remove unused parameter
        [HarmonyReversePatch, HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static void CalculatePose(PlayerAction_Build __instance, int startObjId, int castObjId)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> instructionsList = instructions.ToList();

                // Find the idx at which the "cargoTraffic" field of the PlanetFactory
                // Is first accessed since this is the start of the instructions that compute posing

                /* ex of the code in dotpeek:
                 * ```
                 * if (this.cursorValid && this.startObjId != this.castObjId && (this.startObjId > 0 && this.castObjId > 0))
                 * {
                 *   CargoTraffic cargoTraffic = this.factory.cargoTraffic; <- WE WANT TO START WITH THIS LINE (INCLUSIVE)
                 *   EntityData[] entityPool = this.factory.entityPool;
                 *   BeltComponent[] beltPool = cargoTraffic.beltPool;
                 *   this.posePairs.Clear();
                 *   this.startSlots.Clear();
                 * ```
                 */

                int startIdx = -1;
                for (int i = 0; i < instructionsList.Count; i++)
                {
                    if (instructionsList[i].LoadsField(typeof(PlanetFactory).GetField("cargoTraffic")))
                    {
                        startIdx = i - 2; // need the two proceeding lines that are ldarg.0 and ldfld PlayerAction_Build::factory
                        break;
                    }
                }
                if (startIdx == -1)
                {
                    throw new InvalidOperationException("Cannot patch sorter posing code b/c the start indicator isn't present");
                }

                // Find the idx at which the "posePairs" field of the PlayerAction_Build
                // Is first accessed and followed by a call to get_Count

                /*
                 * ex of the code in dotpeek:
                 * ```
                 *          else
                 *              flag6 = true;
                 *      }
                 *      else
                 *        flag6 = true;
                 *    }
                 *  }
                 *  if (this.posePairs.Count > 0) <- WE WANT TO END ON THIS LINE (EXCLUSIVE)
                 *  {
                 *    float num1 = 1000f;
                 *    float num2 = Vector3.Distance(this.currMouseRay.origin, this.cursorTarget) + 10f;
                 *    PlayerAction_Build.PosePair posePair2 = new PlayerAction_Build.PosePair();
                 * ```
                 */

                int endIdx = -1;
                for (int i = startIdx; i < instructionsList.Count - 1; i++) // go to the end - 1 b/c we need to check two instructions to find valid loc
                {
                    if (instructionsList[i].LoadsField(typeof(PlayerAction_Build).GetField("posePairs")))
                    {
                        if (instructionsList[i + 1].Calls(typeof(List<PlayerAction_Build.PosePair>).GetMethod("get_Count")))
                        {
                            endIdx = i - 1; // need the proceeding line that is ldarg.0
                            break;
                        }
                    }
                }
                if (endIdx == -1)
                {
                    throw new InvalidOperationException("Cannot patch sorter posing code b/c the end indicator isn't present");
                }

                // The first argument to an instance method (arg 0) is the instance itself
                // Since this is a static method, the instance will still need to be passed
                // For the IL instructions to work properly so manually pass the instance as
                // The first argument to the method.
                List<CodeInstruction> code = new List<CodeInstruction>()
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldarg_1),
                        CodeInstruction.StoreField(typeof(PlayerAction_Build), "startObjId"),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldarg_2),
                        CodeInstruction.StoreField(typeof(PlayerAction_Build), "castObjId"),
                    };

                for (int i = startIdx; i < endIdx; i++)
                {
                    code.Add(instructionsList[i]);
                }
                return code.AsEnumerable();
            }

            // make compiler happy
            _ = Transpiler(null);
            return;
        }

        // CalculatePose TODO :
        /*

        if (flag13)
        {
            foreach (Pose pose in __instance.belt_slots)
            {
                PlayerAction_Build.SlotPoint item;
                item.objId = __instance.startObjId;
                item.pose = pose.GetTransformedBy(objectPose8);
                item.slotIdx = -1;
                __instance.startSlots.Add(item);
            }

            // REMOVE OTHER STUFF
        }
        ...
        if (flag14)
        {
            foreach (Pose pose in __instance.belt_slots)
            {
                PlayerAction_Build.SlotPoint item;
                item.objId = __instance.castObjId;
                item.pose = pose.GetTransformedBy(objectPose9);
                item.slotIdx = -1;
                __instance.endSlots.Add(item);
            }

            // REMOVE OTHER STUFF
        }
        ...

        __instance.posePairs.Add(posePair2);
        if (false && num38 < 40f)
        {
            if (flag18 && flag19)
            {
                // fix stuff here to get pose offset (this is the code that moves the inserter head 'a bit' from the actual snap poit to keep it
                // perpendicular to the belt
            }
            else if (flag19)
            {
                // fix stuff here to get pose offset (this is the code that moves the inserter head 'a bit' from the actual snap poit to keep it
                // perpendicular to the belt
            }
            else if (flag18)
            {
                // fix stuff here to get pose offset (this is the code that moves the inserter head 'a bit' from the actual snap poit to keep it
				// perpendicular to the belt
            }
        }
        ...
        */

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetObjectPose")]
        public static bool PlayerAction_Build_GetObjectPose_Prefix(int objId, ref Pose __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                __result = overrides[objId - INITIAL_OBJ_ID].pose;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetObjectProtoId")]
        public static bool PlayerAction_Build_GetObjectProtoId_Prefix(int objId, ref int __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                __result = overrides[objId - INITIAL_OBJ_ID].itemProto.ID;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "ObjectIsBelt")]
        public static bool PlayerAction_Build_ObjectIsBelt_Prefix(int objId, ref bool __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                // we always ahve to return false otherwise calculatePose will throw (See TODO above)
                __result = false; // overrides[objId - INITIAL_OBJ_ID].itemProto.prefabDesc.isBelt;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "ObjectIsInserter")]
        public static bool PlayerAction_Build_ObjectIsInserter_Prefix(int objId, ref bool __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                __result = overrides[objId - INITIAL_OBJ_ID].itemProto.prefabDesc.isInserter;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetLocalInserts")]
        public static bool PlayerAction_Build_GetLocalInserts_Prefix(int objId, ref Pose[] __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                __result = overrides[objId - INITIAL_OBJ_ID].itemProto.prefabDesc.insertPoses;
                return false;
            }

            return true;
        }
    }
}
