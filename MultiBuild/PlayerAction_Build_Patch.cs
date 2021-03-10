using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;


namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public class AssemblerCopy
    {
        public ItemProto itemProto;
        public EntityData originalEntity;
        public Vector3 originalPos;
        public Quaternion originalRot;

        public Vector3 cursorRelativePos = Vector3.zero;
        public float cursorRelativeYaw = 0f;
        public int snapCount = 0;
        public Vector3[] snapMoves;


        public int recipeId;
    }

    public class InserterCopy
    {
        public int fromID;
        public int toID;

        public int originalId;
        public ItemProto itemProto;
        public EntityData originalEntity;

        public bool incoming;
        public int startSlot;
        public int endSlot;
        public Vector3 posDelta;
        public Vector3 pos2Delta;
        public Quaternion rot;
        public Quaternion rot2;
        public int snapCount;
        public Vector3[] snapMoves;
        public short pickOffset;
        public short insertOffset;
        public short t1;
        public short t2;
        public int filterId;
        public int refCount;
        public bool otherIsBelt;
    }

    class PlayerAction_Build_Patch
    {

        public static bool lastFlag;
        public static string lastCursorText;
        public static bool lastCursorWarning;
        public static Vector3 lastPosition = Vector3.zero;

        public static bool executeBuildUpdatePreviews = true;

        public static int ignoredTicks = 0;
        public static int path = 0;

        public static Dictionary<int, AssemblerCopy> copiedAssemblers = new Dictionary<int, AssemblerCopy>();
        public static Dictionary<int, InserterCopy> copiedInserters = new Dictionary<int, InserterCopy>();

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
        public static bool CreatePrebuilds_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.waitConfirm && VFInput._buildConfirm.onDown && __instance.buildPreviews.Count > 0
                && MultiBuild.IsMultiBuildEnabled() && !__instance.multiLevelCovering)
            {
                if (MultiBuild.startPos == Vector3.zero)
                {
                    MultiBuild.startPos = __instance.groundSnappedPos;
                    return false;
                }
                else
                {
                    MultiBuild.startPos = Vector3.zero;
                    return true;
                }
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "BuildMainLogic")]
        public static bool BuildMainLogic_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.handPrefabDesc == null ||
                __instance.handPrefabDesc.minerType != EMinerType.None ||
                __instance.player.planetData.type == EPlanetType.Gas
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

            // As multibuild increase calculation exponentially (collision and rendering must be performed for every entity), we hijack the BuildMainLogic
            // and execute the relevant submethods only when needed
            executeBuildUpdatePreviews = true;
            /* if (MultiBuild.IsMultiBuildRunning())
             {
                 if (lastPosition != __instance.groundSnappedPos)
                 {
                     lastPosition = __instance.groundSnappedPos;
                     executeBuildUpdatePreviews = true;
                 }
                 else
                 {
                     executeBuildUpdatePreviews = false;
                 }
             }
             else
             {
                 lastPosition = Vector3.zero;
             }*/

            // Run the preview methods if we have changed position, if we have received a relevant keyboard input or in any case every MAX_IGNORED_TICKS ticks.
            executeBuildUpdatePreviews = true; // executeBuildUpdatePreviews || VFInput._rotate || VFInput._counterRotate || ignoredTicks >= MultiBuild.MAX_IGNORED_TICKS;

            bool flag;
            if (executeBuildUpdatePreviews)
            {
                __instance.DetermineBuildPreviews();
                flag = __instance.CheckBuildConditions();
                __instance.UpdatePreviews();
                __instance.UpdateGizmos();

                lastCursorText = __instance.cursorText;
                lastCursorWarning = __instance.cursorWarning;
                lastFlag = flag;

                ignoredTicks = 0;
            }
            else
            {
                __instance.cursorText = lastCursorText;
                __instance.cursorWarning = lastCursorWarning;
                flag = lastFlag;
                ignoredTicks++;
            }

            if (flag)
            {
                __instance.CreatePrebuilds();

                if (__instance.waitConfirm && VFInput._buildConfirm.onDown)
                {
                    __instance.ClearBuildPreviews();
                    ignoredTicks = MultiBuild.MAX_IGNORED_TICKS;
                }
            }

            return false;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static bool DetermineBuildPreviews_Prefix(ref PlayerAction_Build __instance)
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
                if (__instance.multiLevelCovering && !MultiBuild.IsMultiBuildRunning())
                {
                    return true;
                }

                // full hijacking of DetermineBuildPreviews 
                runOriginal = false;

                if (VFInput._switchSplitter.onDown)
                {
                    __instance.modelOffset++;
                }

                if (VFInput._rotate.onDown)
                {
                    __instance.yaw += 90f;
                    __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                    __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                }
                if (VFInput._counterRotate.onDown)
                {
                    __instance.yaw -= 90f;
                    __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                    __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                }
                __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;

                /*__instance.previewPose.position = Vector3.zero;
                __instance.previewPose.rotation = Quaternion.identity;*/

                __instance.previewPose.position = __instance.cursorTarget;
                __instance.previewPose.rotation = Maths.SphericalRotation(__instance.previewPose.position, __instance.yaw);

                var inversePreviewRot = Quaternion.Inverse(__instance.previewPose.rotation);
                if (copiedAssemblers.Count == 0)
                {
                    copiedAssemblers.Add(0, new AssemblerCopy()
                    {
                        itemProto = __instance.handItem,

                        recipeId = __instance.copyRecipeId
                    });
                }

                var previews = new List<BuildPreview>();

                if (lastPosition == __instance.groundSnappedPos)
                {
                    return false;
                }
                lastPosition = __instance.groundSnappedPos;
                if (copiedAssemblers.Count == 1 && MultiBuild.IsMultiBuildRunning())
                {
                    int snapPath = path;
                    Vector3[] snaps = new Vector3[1024];

                    var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(MultiBuild.startPos, __instance.groundSnappedPos, ref snapPath, snaps);

                    var desc = copiedAssemblers[0].itemProto.prefabDesc;
                    Collider[] colliders = new Collider[desc.buildColliders.Length];
                    Vector3 previousPos = Vector3.zero;

                    var usedSnaps = new List<Vector3>(10);

                    var maxSnaps = Math.Max(1, snappedPointCount - MultiBuild.spacingStore[MultiBuild.spacingIndex]);

                    for (int s = 0; s < maxSnaps; s++)
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

                        if (s > 0 && MultiBuild.spacingStore[MultiBuild.spacingIndex] > 0)
                        {
                            s += MultiBuild.spacingStore[MultiBuild.spacingIndex];
                            pos = snaps[s];
                            rot = Maths.SphericalRotation(snaps[s], __instance.yaw);
                        }

                        previousPos = pos;
                        usedSnaps.Add(pos);

                        var bp = BuildPreview.CreateSingle(copiedAssemblers[0].itemProto, copiedAssemblers[0].itemProto.prefabDesc, true);
                        bp.ResetInfos();
                        bp.desc = copiedAssemblers[0].itemProto.prefabDesc;
                        bp.item = copiedAssemblers[0].itemProto;
                        bp.lpos = inversePreviewRot * (pos - __instance.previewPose.position);
                        bp.lrot = inversePreviewRot * rot;
                        bp.recipeId = copiedAssemblers[0].recipeId;

                        //pose.position - this.previewPose.position =  this.previewPose.rotation * buildPreview.lpos;
                        //pose.rotation = this.previewPose.rotation * buildPreview.lrot;
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

                        previews.Add(bp);

                    }

                    foreach (var collider in colliders)
                    {
                        if (collider != null)
                        {
                            ColliderPool.PutCollider(collider);
                        }
                    }

                    ActivateColliders(ref __instance.nearcdLogic, usedSnaps);
                }
                else
                {
                    foreach (var copiedAssembler in copiedAssemblers.Values)
                    {
                        var absoluteBuildingRot = Maths.SphericalRotation(__instance.groundSnappedPos, __instance.yaw);
                        var absolutePosition = __instance.planetAux.Snap(__instance.groundSnappedPos + absoluteBuildingRot * copiedAssembler.cursorRelativePos, true, true);

                        if (copiedAssembler.snapCount > 0)
                        {
                            absolutePosition = __instance.groundSnappedPos;
                            // Note: rotates each move relative to the rotation of the new building
                            for (int u = 0; u < copiedAssembler.snapCount; u++)
                                absolutePosition = __instance.planetAux.Snap(absolutePosition + absoluteBuildingRot * copiedAssembler.snapMoves[u], true, false);
                        }

                        BuildPreview bp = BuildPreview.CreateSingle(copiedAssembler.itemProto, copiedAssembler.itemProto.prefabDesc, true);
                        bp.ResetInfos();
                        bp.desc = copiedAssembler.itemProto.prefabDesc;
                        bp.item = copiedAssembler.itemProto;
                        bp.recipeId = copiedAssembler.recipeId;
                        bp.lpos = inversePreviewRot * (absolutePosition - __instance.previewPose.position);
                        bp.lrot = inversePreviewRot * Maths.SphericalRotation(absolutePosition, __instance.yaw + copiedAssembler.cursorRelativeYaw);

                        previews.Add(bp);
                    }
                }

                for (var i = 0; i < previews.Count; i++)
                {
                    if (i >= __instance.buildPreviews.Count)
                    {
                        __instance.AddBuildPreview(previews[i]);
                    }
                    else
                    {
                        var original = __instance.buildPreviews[i];
                        var updated = previews[i];
                        if (original.desc != updated.desc || original.item != updated.item)
                        {
                            original.ResetInfos();
                            original.desc = updated.desc;
                            original.item = updated.item;

                        }
                        original.recipeId = updated.recipeId;
                        original.filterId = updated.filterId;
                        original.condition = EBuildCondition.Ok;

                        original.lpos = updated.lpos;
                        original.lrot = updated.lrot;

                        original.lpos2 = updated.lpos2;
                        original.lrot2 = updated.lrot2;
                    }
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
        public static bool UpdatePreviews_Prefix(ref PlayerAction_Build __instance)
        {
            return executeBuildUpdatePreviews;
        }

        public static void ActivateColliders(ref NearColliderLogic nearCdLogic, List<Vector3> snaps)
        {
            for (int s = 0; s < snaps.Count; s++)
            {
                nearCdLogic.activeColHashCount = 0;
                var center = snaps[s];

                Vector3 vector = Vector3.Cross(center, center - GameMain.mainPlayer.position).normalized * (5f);
                Vector3 vector2 = Vector3.Cross(vector, center).normalized * (5f);

                nearCdLogic.MarkActivePos(center);
                nearCdLogic.MarkActivePos(center + vector);
                nearCdLogic.MarkActivePos(center - vector);
                nearCdLogic.MarkActivePos(center + vector2);
                nearCdLogic.MarkActivePos(center - vector2);
                nearCdLogic.MarkActivePos(center + vector + vector2);
                nearCdLogic.MarkActivePos(center - vector + vector2);
                nearCdLogic.MarkActivePos(center + vector - vector2);
                nearCdLogic.MarkActivePos(center - vector - vector2);

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
                                if ((colliderPool[j].pos - center).sqrMagnitude <= 25f * 4f + colliderPool[j].ext.sqrMagnitude)
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

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "SetCopyInfo")]
        public static void SetCopyInfo_Postfix(ref PlayerAction_Build __instance, int objectId, int protoId)
        {
            copiedAssemblers.Clear();
            copiedInserters.Clear();
            if (objectId < 0)
                return;


            var sourceEntityProto = LDB.items.Select(protoId);

            if (sourceEntityProto.prefabDesc.insertPoses.Length == 0)
                return;

            var sourceEntityId = objectId;
            var sourceEntity = __instance.factory.entityPool[sourceEntityId];
            var sourcePos = sourceEntity.pos;
            var sourceRot = sourceEntity.rot;

            copiedAssemblers.Add(sourceEntityId, new AssemblerCopy()
            {
                itemProto = sourceEntityProto,
                originalEntity = sourceEntity,
                originalPos = sourcePos,
                originalRot = sourceRot,

                recipeId = __instance.copyRecipeId
            });

            // Set the current build rotation to the copied building rotation
            Quaternion zeroRot = Maths.SphericalRotation(sourcePos, 0f);
            float yaw = Vector3.SignedAngle(zeroRot.Forward(), sourceRot.Forward(), zeroRot.Up());
            if (sourceEntityProto.prefabDesc.minerType != EMinerType.Vein)
            {
                yaw = Mathf.Round(yaw / 90f) * 90f;
            }
            __instance.yaw = yaw;

            // Ignore building without inserter slots


            // Find connected inserters
            var inserterPool = __instance.factory.factorySystem.inserterPool;
            var entityPool = __instance.factory.entityPool;
            var prebuildPool = __instance.factory.prebuildPool;

            for (int i = 1; i < __instance.factory.factorySystem.inserterCursor; i++)
            {
                if (inserterPool[i].id != i) continue;

                var inserter = inserterPool[i];
                var inserterEntity = entityPool[inserter.entityId];

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
                    var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(sourcePos, otherPos, ref path, snaps);
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
                        fromID = pickTarget,
                        toID = insertTarget,
                        incoming = incoming,

                        originalId = inserter.entityId,
                        itemProto = itemProto,
                        originalEntity = inserterEntity,

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
                        snapMoves = snapMoves,
                        snapCount = snappedPointCount,

                        startSlot = -1,
                        endSlot = -1,

                        otherIsBelt = otherIsBelt
                    };


                    // compute the start and end slot that the cached inserter uses
                    CalculatePose(__instance, pickTarget, insertTarget);

                    if (__instance.posePairs.Count > 0)
                    {
                        float minDistance = 1000f;
                        for (int j = 0; j < __instance.posePairs.Count; ++j)
                        {
                            var posePair = __instance.posePairs[j];
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

                    copiedInserters.Add(copiedInserter.originalId, copiedInserter);
                }
            }
        }

        [HarmonyReversePatch, HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static void CalculatePose(PlayerAction_Build __instance, int startObjId, int castObjId)
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


        private static Color COPY_GIZMO_COLOR = new Color(1f, 1f, 1f, 1f);
        private static CircleGizmo circleGizmo;
        private static int[] _nearObjectIds = new int[4096];

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "GameTick")]
        public static void GameTick(PlayerAction_Build __instance)
        {
            if(VFInput.shift && __instance.controller.cmd.mode == 0)
            {
                if (circleGizmo == null)
                {
                    circleGizmo = CircleGizmo.Create(1, __instance.groundTestPos, 10);

                    circleGizmo.fadeOutScale = circleGizmo.fadeInScale = 1.8f;
                    circleGizmo.fadeOutTime = circleGizmo.fadeInTime = 0.15f;
                    circleGizmo.color = COPY_GIZMO_COLOR;
                    circleGizmo.autoRefresh = true;
                    circleGizmo.Open();
                }

                circleGizmo.position = __instance.groundTestPos;
                circleGizmo.radius = 1.2f * 10;

                if(VFInput._buildConfirm.onDown)
                {
                    copiedAssemblers.Clear();
                    copiedInserters.Clear();
                    int found = __instance.nearcdLogic.GetBuildingsInAreaNonAlloc(__instance.groundTestPos, 10, _nearObjectIds);

                    AssemblerCopy firstItem = null;
                    for (int i = 0; i< found; i++)
                    {
                        

                        var sourceEntityId = _nearObjectIds[i];
                        var sourceEntity = __instance.factory.entityPool[sourceEntityId];
                        var sourcePos = sourceEntity.pos;
                        var sourceRot = sourceEntity.rot;

                        var sourceEntityProto = LDB.items.Select(sourceEntity.protoId);

                        if (sourceEntityProto.prefabDesc.insertPoses.Length == 0)
                            continue;
                        
                        var assemblerCopy = new AssemblerCopy()
                        {
                            itemProto = sourceEntityProto,
                            originalEntity = sourceEntity,
                            originalPos = sourcePos,
                            originalRot = sourceRot
                        };

                        if (!sourceEntityProto.prefabDesc.isAssembler)
                        {
                            assemblerCopy.recipeId = __instance.factory.factorySystem.assemblerPool[sourceEntity.assemblerId].recipeId;
                        }

                        if (firstItem == null)
                        {
                            firstItem = assemblerCopy;
                        } else
                        {
                            
                            var inverseRot = Quaternion.Inverse(firstItem.originalRot);

                            assemblerCopy.cursorRelativePos = inverseRot * (assemblerCopy.originalPos - firstItem.originalPos);
                            int path = 0;
                            Vector3[] snaps = new Vector3[1000];
                            var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(firstItem.originalPos, assemblerCopy.originalPos, ref path, snaps);
                            Vector3 lastSnap = firstItem.originalPos;
                            Vector3[] snapMoves = new Vector3[snappedPointCount];
                            for (int s = 0; s < snappedPointCount; s++)
                            {
                                // note: reverse rotation of the delta so that rotation works
                                Vector3 snapMove = inverseRot * (snaps[s] - lastSnap);
                                snapMoves[s] = snapMove;
                                lastSnap = snaps[s];
                            }

                            assemblerCopy.snapCount = snappedPointCount;
                            assemblerCopy.snapMoves = snapMoves;
                            //assemblerCopy.cursorRelativeRot = Quaternion.Inverse(firstItem.originalRot) * assemblerCopy.originalRot;
                        }

                        copiedAssemblers.Add(sourceEntityId, assemblerCopy);

                    }

                    if(copiedAssemblers.Count > 0)
                    {
                        __instance.player.SetHandItems(firstItem.itemProto.ID, 0, 0);
                        __instance.controller.cmd.type = ECommand.Build;
                        __instance.controller.cmd.mode = 1;
                    }
                    
                }
            } else
            {
                if (circleGizmo != null)
                {
                    circleGizmo.Close();
                    circleGizmo = null;
                }
            }
        }
    }
}
