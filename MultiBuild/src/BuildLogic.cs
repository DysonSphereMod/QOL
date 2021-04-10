using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using UnityEngine;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [HarmonyPatch]
    public class BuildLogic
    {
        public static bool multiBuildEnabled = false;
        public static bool multiBuildPossible = true;
        public static bool multiBuildInserters = true;
        public static int path = 0;
        public static Vector3 startPos = Vector3.zero;
        public static Vector3 lastPosition = Vector3.zero;

        public static Dictionary<int, int> spacingStore = new Dictionary<int, int>();
        public static int spacingIndex = 0;
        public static int spacingPeriod = 1;

        public static bool forceRecalculation = false;
        public static bool runUpdate = false;

        public static bool lastFlag;
        public static bool lastCursorWarning;
        public static bool lastRunOriginal;
        public static string lastCursorText;
        public static Vector3[] snaps = new Vector3[1024];

        public static Dictionary<int, PastedEntity> toPostProcess = new Dictionary<int, PastedEntity>();


        public static bool IsMultiBuildAvailable()
        {
            return UIGame.viewMode == EViewMode.Build && GameMain.mainPlayer.controller.cmd.mode == 1 && multiBuildPossible;
        }

        public static bool IsMultiBuildEnabled()
        {
            return IsMultiBuildAvailable() && multiBuildEnabled;
        }

        public static bool IsMultiBuildRunning()
        {
            return IsMultiBuildEnabled() && startPos != Vector3.zero;
        }

        public static void ResetMultiBuild()
        {
            spacingIndex = 0;
            path = 0;
            forceRecalculation = true;
            lastRunOriginal = true;
            multiBuildInserters = true;
            multiBuildEnabled = false;
            startPos = Vector3.zero;
            spacingPeriod = 1;
            if (!MultiBuild.itemSpecificSpacing.Value)
            {
                spacingStore[spacingIndex] = 0;
            }
        }

        public static void OnUpdate()
        {
            var isEnabled = IsMultiBuildEnabled();

            if (Input.GetKeyUp(KeyCode.LeftAlt) && IsMultiBuildAvailable())
            {
                multiBuildEnabled = !multiBuildEnabled;
                if (multiBuildEnabled)
                {
                    startPos = Vector3.zero;
                }
                forceRecalculation = true;
            }

            if (Input.GetKeyUp(KeyCode.Tab) && IsMultiBuildAvailable())
            {
                multiBuildInserters = !multiBuildInserters;
                forceRecalculation = true;
            }

            if (VFInput.control && (Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && isEnabled)
            {
                spacingPeriod++;
                forceRecalculation = true;
            }

            if (VFInput.control && (Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isEnabled && spacingPeriod > 1)
            {
                spacingPeriod--;
                forceRecalculation = true;
            }

            if (!VFInput.control && (Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && isEnabled)
            {
                spacingStore[spacingIndex]++;
                forceRecalculation = true;
            }

            if (!VFInput.control && (Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && isEnabled && spacingStore[spacingIndex] > 0)
            {
                spacingStore[spacingIndex]--;
                forceRecalculation = true;
            }

            if ((Input.GetKeyUp(KeyCode.Alpha0) || Input.GetKeyUp(KeyCode.Keypad0)) && isEnabled)
            {
                spacingStore[spacingIndex] = 0;
                spacingPeriod = 1;
                forceRecalculation = true;
            }
            if (Input.GetKeyUp(KeyCode.Z) && IsMultiBuildRunning())
            {
                path = 1 - path;
                forceRecalculation = true;
            }
        }

        public static bool IsInserterConnected(BuildPreview bp)
        {
            return (bp.input != null || bp.inputObjId != 0) && (bp.output != null || bp.outputObjId != 0);
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "CreatePrebuilds")]
        public static bool PlayerAction_Build_CreatePrebuilds_Prefix(ref PlayerAction_Build __instance)
        {
            if (!__instance.waitConfirm || !VFInput._buildConfirm.onDown) return true;

            var runOriginal = true;
            if (IsMultiBuildEnabled() &&
                __instance.buildPreviews.Count > 0 &&
                !__instance.multiLevelCovering)
            {
                if (startPos == Vector3.zero)
                {
                    startPos = __instance.groundSnappedPos;
                    lastPosition = Vector3.zero;
                    runOriginal = false;
                }
                else
                {
                    startPos = Vector3.zero;
                    runOriginal = true;
                }
            }

            if (runOriginal && __instance.buildPreviews.Count > 1)
            {
                for (var i = 0; i < __instance.buildPreviews.Count; i++)
                {
                    var bp = __instance.buildPreviews[i];
                    if (bp.desc.isInserter && !IsInserterConnected(bp))
                    {
                        __instance.RemoveBuildPreview(bp);
                        --i;
                    }
                }
            }

            return runOriginal;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "AfterPrebuild")]
        public static void PlayerAction_Build_AfterPrebuilds_Prefix(ref PlayerAction_Build __instance)
        {
            foreach (var pastedEntity in BlueprintManager.pastedEntities.Values)
            {
                var buildPreview = pastedEntity.buildPreview;
                var sourceBuilding = pastedEntity.sourceBuilding;

                if (buildPreview.objId >= 0)
                {
                    continue;
                }
                pastedEntity.objId = buildPreview.objId;

                if (pastedEntity.type == EPastedType.BUILDING)
                {
                    if (sourceBuilding.itemProto.prefabDesc.isStation && sourceBuilding.slotFilters.Count + sourceBuilding.stationSettings.Count > 0)
                    {
                        toPostProcess.Add(buildPreview.objId, pastedEntity);
                    }
                    if (sourceBuilding.itemProto.prefabDesc.isSplitter)
                    {
                        toPostProcess.Add(buildPreview.objId, pastedEntity);
                        foreach (var otherPastedEntity in pastedEntity.connectedEntities.Values)
                        {
                            // postpocess the splitter after every belt has been built
                            toPostProcess.Add(otherPastedEntity.buildPreview.objId, pastedEntity);
                        }
                    }
                }
            }
            if (BlueprintManager.pastedEntities.Count > 0)
            {
                __instance.ClearBuildPreviews();
            }

            forceRecalculation = true;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "NotifyBuilt")]
        public static void PlayerAction_Build_NotifyBuilt_Prefix(ref PlayerAction_Build __instance, int preObjId, int postObjId)
        {
            forceRecalculation = true;
            if (toPostProcess.TryGetValue(preObjId, out PastedEntity pastedEntity))
            {
                var factory = __instance.factory;
                if (pastedEntity.objId == preObjId)
                {
                    pastedEntity.postObjId = postObjId;
                }

                var entity = factory.entityPool[postObjId];

                if (entity.stationId > 0)
                {
                    var sourceBuilding = pastedEntity.sourceBuilding;
                    var stationConfig = sourceBuilding.stationConfig;
                    var stationComponent = __instance.factory.transport.GetStationComponent(entity.stationId);

                    if (stationConfig != null)
                    {
                        factory.powerSystem.consumerPool[stationComponent.pcId].workEnergyPerTick = stationConfig.workEnergyPerTick;

                        stationComponent.tripRangeDrones = stationConfig.tripRangeDrones;
                        stationComponent.tripRangeShips = stationConfig.tripRangeShips;
                        stationComponent.warpEnableDist = stationConfig.warpEnableDist;
                        stationComponent.warperNecessary = stationConfig.warperNecessary;
                        stationComponent.includeOrbitCollector = stationConfig.includeOrbitCollector;
                        stationComponent.deliveryDrones = stationConfig.deliveryDrones;
                        stationComponent.deliveryShips = stationConfig.deliveryShips;
                    }

                    foreach (var settings in sourceBuilding.stationSettings)
                    {
                        factory.transport.SetStationStorage(entity.stationId, settings.index, settings.itemId, settings.max, settings.localLogic, settings.remoteLogic, GameMain.mainPlayer);
                    }
                    foreach (var slotFilter in sourceBuilding.slotFilters)
                    {
                        stationComponent.slots[slotFilter.slotIndex].storageIdx = slotFilter.storageIdx;
                    }

                }

                if (pastedEntity.type == EPastedType.BUILDING && pastedEntity.buildPreview.desc.isSplitter && pastedEntity.postObjId != 0)
                {
                    var splitterPool = factory.cargoTraffic.splitterPool;
                    var splitterEntity = factory.entityPool[pastedEntity.postObjId];
                    var splitterSettings = pastedEntity.sourceBuilding.splitterSettings;

                    if (splitterSettings != null)
                    {
                        splitterPool[splitterEntity.splitterId].SetPriority(splitterSettings.inPrioritySlot, splitterSettings.inPriority, 0);
                        splitterPool[splitterEntity.splitterId].SetPriority(splitterSettings.outPrioritySlot, splitterSettings.outPriority, splitterSettings.outFilter);
                    }
                }

                toPostProcess.Remove(preObjId);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), "CreateEntityLogicComponents")]
        public static void PlanetFactory_CreateEntityLogicComponents_Postfix(PlanetFactory __instance, int entityId, PrefabDesc desc)
        {
            // this ensure that buildings built AFTER a connected belt are correctly configured
            // the game by default already does something like this but only for belts to belts / splitter / logistic stations
            // See PlanetFactory.CreateEntityLogicComponents
            var entity = __instance.entityPool[entityId];
            for (var i = 0; i < 4; i++)
            {
                __instance.ReadObjectConn(entityId, i, out bool isOutput, out int otherId, out _);

                // ignore unbuilt or connections that are not belts
                if (otherId <= 0 || __instance.entityPool[otherId].beltId <= 0) continue;

                var beltId = __instance.entityPool[otherId].beltId;

                if (desc.isTank)
                {
                    __instance.factoryStorage.SetTankBelt(entity.tankId, beltId, i, isOutput);
                }
                if (desc.isFractionate)
                {
                    __instance.factorySystem.SetFractionateBelt(entity.fractionateId, beltId, i, isOutput);
                }
                if (desc.isPowerExchanger)
                {
                    __instance.powerSystem.SetExchangerBelt(entity.powerExcId, beltId, i, isOutput);
                }
            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "BuildMainLogic")]
        public static bool PlayerAction_Build_BuildMainLogic_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.handPrefabDesc == null ||
                __instance.handPrefabDesc.minerType != EMinerType.None ||
                __instance.player.planetData.type == EPlanetType.Gas ||
                BlueprintManager.data.copiedBuildings.Count > 1
                )
            {
                multiBuildPossible = false;
            }
            else
            {
                multiBuildPossible = true;
            }

            if (MultiBuild.itemSpecificSpacing.Value && __instance.handItem != null && spacingIndex != __instance.handItem.ID)
            {
                spacingIndex = __instance.handItem.ID;
                if (!spacingStore.ContainsKey(spacingIndex))
                {
                    spacingStore[spacingIndex] = 0;
                }
            }


            runUpdate = true;

            __instance.DetermineBuildPreviews();


            if (runUpdate)
            {
                if (BlueprintManager.pastedEntities.Count > 0)
                {
                    lastFlag = CheckBuildConditionsFast();
                }
                else
                {
                    lastFlag = __instance.CheckBuildConditions();
                }

                __instance.UpdatePreviews();
                __instance.UpdateGizmos();


                lastCursorText = __instance.cursorText;
                lastCursorWarning = __instance.cursorWarning;
            }
            else
            {
                __instance.cursorText = lastCursorText;
                __instance.cursorWarning = lastCursorWarning;
            }

            if (lastFlag)
            {
                __instance.CreatePrebuilds();
            }

            return false;
        }

        public static bool CheckBuildConditionsFast()
        {
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            bool flag = true;
            foreach (var buildPreview in actionBuild.buildPreviews)
            {
                if (buildPreview.condition != EBuildCondition.Ok)
                {
                    flag = false;
                    if (!actionBuild.cursorWarning)
                    {
                        actionBuild.cursorWarning = true;
                        actionBuild.cursorText = buildPreview.conditionText;
                    }
                }
            }
            if (flag && actionBuild.waitConfirm)
            {
                actionBuild.cursorText = "点击鼠标建造".Translate();
            }
            if (!flag && !VFInput.onGUI)
            {
                UICursor.SetCursor(ECursor.Ban);
            }

            return flag;
        }

        [HarmonyReversePatch, HarmonyPatch(typeof(PlayerAction_Build), "CheckBuildConditions")]
#pragma warning disable IDE0060 // Remove unused parameter
        public static void CheckBuildConditionsWorker(PlayerAction_Build __instance, BuildPreview bp)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                // modify the original CheckBuildConditions to only operate on a single item (so only the body of the for loop) injecting the BuildPreview passed as argument
                CodeMatcher matcher = new CodeMatcher(instructions)
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldc_I4_0),
                    new CodeMatch(OpCodes.Stloc_2)
                )
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_1))
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Stloc_3))
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.desc))),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(PrefabDesc), nameof(PrefabDesc.isBelt)))
                )
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldloc_3))
                .SetInstructionAndAdvance(Transpilers.EmitDelegate<Func<PlayerAction_Build, BuildPreview, bool>>((actionBuild, buildPreview) =>
                {
                    // remove checks for belts by stating that the current buildPreview is not a belt.
                    if (buildPreview.desc.isBelt)
                    {
                        // but we have to take care of collision checks
                        Vector3 testPos = buildPreview.lpos + buildPreview.lpos.normalized * 0.3f;
                        if (!buildPreview.ignoreCollider)
                        {
                            actionBuild.GetOverlappedObjectsNonAlloc(testPos, 0.34f, 3f, false);
                            if (actionBuild._overlappedCount > 0)
                            {
                                buildPreview.condition = EBuildCondition.Collide;
                            }

                            actionBuild.GetOverlappedVeinsNonAlloc(testPos, 0.6f, 3f);
                            if (actionBuild._overlappedCount > 0)
                            {
                                buildPreview.condition = EBuildCondition.Collide;
                            }
                        }

                        return false;
                    }
                    return buildPreview.desc.isBelt;
                }))
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.condition)))
                )
                .Advance(1)
                .SetInstructionAndAdvance(Transpilers.EmitDelegate<Func<BuildPreview, bool>>(buildPreview =>
                {
                    PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;
                    ConcurrentEnoughItemCheck(buildPreview);
                    // ignore checkbuildconditions for all inserters when copy pasting as we already took care of checking for collisions
                    if (buildPreview.desc.isInserter)
                    {

                        if (buildPreview.condition != EBuildCondition.Ok || !IsInserterConnected(buildPreview))
                        {
                            return true;
                        }

                        // must calculate correct refCount/refArr to ensure inserter moves has the right speed

                        Vector3 posR = Vector3.zero;
                        bool inputIsBelt;
                        Vector3 inputPos;
                        bool outputIsBelt;
                        Vector3 outputPos;
                        if (buildPreview.input == null)
                        {
                            inputPos = actionBuild.GetObjectPose(buildPreview.inputObjId).position;
                            inputIsBelt = actionBuild.ObjectIsBelt(buildPreview.inputObjId);
                        }
                        else
                        {
                            inputPos = buildPreview.input.lpos;
                            inputIsBelt = buildPreview.input.desc.isBelt;
                        }
                        if (buildPreview.output == null)
                        {
                            outputPos = actionBuild.GetObjectPose(buildPreview.outputObjId).position;
                            outputIsBelt = actionBuild.ObjectIsBelt(buildPreview.outputObjId);
                        }
                        else
                        {
                            outputPos = buildPreview.output.lpos;
                            outputIsBelt = buildPreview.output.desc.isBelt;
                        }

                        if (inputIsBelt && !outputIsBelt)
                        {
                            posR = outputPos;
                        }
                        else if (!inputIsBelt && outputIsBelt)
                        {
                            posR = inputPos;
                        }
                        else
                        {
                            posR = (inputPos + outputPos) * 0.5f;
                        }
                        float segmentsCount = actionBuild.planetAux.mainGrid.CalcSegmentsAcross(posR, buildPreview.lpos, buildPreview.lpos2);

                        if (!inputIsBelt && !outputIsBelt)
                        {
                            segmentsCount -= 0.3f;
                        }
                        buildPreview.refCount = Mathf.RoundToInt(Mathf.Clamp(segmentsCount, 1f, 3f));
                        buildPreview.refArr = new int[buildPreview.refCount];

                        return true;
                    }

                    return buildPreview.condition != EBuildCondition.Ok;
                }))
                .MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.coverObjId)))
                )
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetOpcodeAndAdvance(OpCodes.Nop)
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_0));



                // trim the code just before the for loop condition checks
                int endIdx = matcher
                    .MatchForward(false,
                    new CodeMatch(OpCodes.Ldloc_3),
                    new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.condition)))
                )
                    .SetOpcodeAndAdvance(OpCodes.Nop).Pos;


                List<CodeInstruction> instructionsList = matcher.InstructionEnumeration().ToList();
                List<CodeInstruction> code = new List<CodeInstruction>();

                for (int i = 0; i < endIdx; i++)
                {
                    code.Add(instructionsList[i]);
                }
                return code.AsEnumerable();
            }

            // make compiler happy
            _ = Transpiler(null);
            return;
        }


        public static void ConcurrentEnoughItemCheck(BuildPreview buildPreview)
        {
            if (buildPreview.condition != EBuildCondition.Ok) return;

            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            lock (actionBuild)
            {
                // only check that we have enough items
                if (buildPreview.coverObjId == 0 || buildPreview.willCover)
                {

                    int id = buildPreview.item.ID;
                    int num = 1;
                    if (actionBuild.tmpInhandId == id && actionBuild.tmpInhandCount > 0)
                    {
                        num = 1;
                        actionBuild.tmpInhandCount--;
                    }
                    else
                    {

                        actionBuild.tmpPackage.TakeTailItems(ref id, ref num, false);
                    }


                    if (num == 0)
                    {
                        buildPreview.condition = EBuildCondition.NotEnoughItem;
                    }
                }


            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(PlayerAction_Build), "DetermineBuildPreviews")]
        public static bool PlayerAction_Build_DetermineBuildPreviews_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.controller.cmd.mode != 1 ||
                __instance.player.planetData.type == EPlanetType.Gas ||
                !__instance.cursorValid ||
                __instance.groundSnappedPos == Vector3.zero ||
                (__instance.handPrefabDesc != null && __instance.handPrefabDesc.minerType != EMinerType.None)
                )
            {
                if (!lastRunOriginal)
                {
                    __instance.ClearBuildPreviews();
                }
                lastRunOriginal = true;
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

            if (!IsMultiBuildRunning() && (__instance.multiLevelCovering || !BlueprintManager.hasData))
            {
                if (!lastRunOriginal)
                {
                    __instance.ClearBuildPreviews();
                }
                lastRunOriginal = true;
                return true;
            }

            // full hijacking of DetermineBuildPreviews

            if (VFInput._switchSplitter.onDown)
            {
                __instance.modelOffset++;
                forceRecalculation = true;
            }

            if (VFInput._rotate.onDown)
            {
                __instance.yaw += 90f;
                __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                forceRecalculation = true;
            }
            if (VFInput._counterRotate.onDown)
            {
                __instance.yaw -= 90f;
                __instance.yaw = Mathf.Repeat(__instance.yaw, 360f);
                __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
                forceRecalculation = true;
            }

            __instance.yaw = Mathf.Round(__instance.yaw / 90f) * 90f;
            __instance.previewPose.position = Vector3.zero;
            __instance.previewPose.rotation = Quaternion.identity;

            if (lastPosition == __instance.groundSnappedPos && !forceRecalculation)
            {
                // no update necessary
                runUpdate = false;
                lastRunOriginal = false;
                return false;
            }
            lastPosition = __instance.groundSnappedPos;
            forceRecalculation = false;

            if (lastRunOriginal)
            {
                __instance.ClearBuildPreviews();
            }
            BlueprintManager.PreparePaste();
            if (IsMultiBuildRunning())
            {
                if (!BlueprintManager.hasData)
                {
                    BlueprintManager.data.copiedBuildings.Add(new BuildingCopy()
                    {
                        originalId = 0,
                        itemProto = __instance.handItem,
                        recipeId = __instance.copyRecipeId,
                        modelIndex = __instance.handPrefabDesc.modelIndex
                    });
                }
                var building = BlueprintManager.data.copiedBuildings[0];// BlueprintManager.data.copiedBuildings.First();

                int snapPath = path;

                var snappedPointCount = __instance.planetAux.SnapLineNonAlloc(startPos, __instance.groundSnappedPos, ref snapPath, snaps);

                var desc = BlueprintManager.GetPrefabDesc(building);
                var pastedPositions = new List<Vector3>();
                Collider[] colliders = new Collider[desc.buildColliders.Length];

                var copiesCounter = 0;
                for (int s = 0; s < snappedPointCount; s++)
                {
                    var pos = snaps[s];
                    var rot = Maths.SphericalRotation(snaps[s], __instance.yaw + building.cursorRelativeYaw);

                    if (s > 0)
                    {
                        var sqrDistance = (pastedPositions.Last() - pos).sqrMagnitude;

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

                    if (s > 0 && spacingStore[spacingIndex] > 0 && copiesCounter % spacingPeriod == 0)
                    {
                        s += spacingStore[spacingIndex];

                        if (s >= snappedPointCount) break;
                        pos = snaps[s];
                        rot = Maths.SphericalRotation(snaps[s], __instance.yaw);
                    }


                    BlueprintManager.Paste(pos, __instance.yaw, false, pastedPositions.Count);
                    pastedPositions.Add(pos);

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
                }
                if (multiBuildInserters)
                {
                    for (var i = 0; i < pastedPositions.Count; i++)
                    {
                        BlueprintManager.PasteInsertersOnly(pastedPositions[i], __instance.yaw, i, true);
                    }
                }

                if (!BlueprintManager.hasData)
                {
                    BlueprintManager.data.copiedBuildings.RemoveAt(0);
                }
                foreach (var collider in colliders)
                {
                    if (collider != null)
                    {
                        ColliderPool.PutCollider(collider);
                    }
                }
            }
            else
            {
                var pasteInserters = multiBuildInserters || (BlueprintManager.data.copiedBuildings.Count + BlueprintManager.data.copiedBelts.Count > 1);
                BlueprintManager.Paste(__instance.groundSnappedPos, __instance.yaw, pasteInserters);
            }
            BlueprintManager.AfterPaste();

            lastRunOriginal = false;
            return false;

        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Build), "UpdatePreviews")]
        public static bool PlayerAction_Build_UpdatePreviews_Prefix(ref PlayerAction_Build __instance)
        {
            if (__instance.upgrading || __instance.destructing)
            {
                return true;
            }
            int graphPoints = 0;
            int pointCount = __instance.connGraph.pointCount;
            __instance.connRenderer.ClearXSigns();
            __instance.connRenderer.ClearUpgradeArrows();
            for (int i = 0; i < __instance.buildPreviews.Count; i++)
            {
                BuildPreview buildPreview = __instance.buildPreviews[i];
                if (buildPreview.needModel)
                {
                    __instance.CreatePreviewModel(buildPreview);
                    int previewIndex = buildPreview.previewIndex;
                    if (previewIndex >= 0)
                    {
                        __instance.previewRenderers[previewIndex].transform.localPosition = __instance.previewPose.position + __instance.previewPose.rotation * buildPreview.lpos;
                        __instance.previewRenderers[previewIndex].transform.localRotation = __instance.previewPose.rotation * buildPreview.lrot;
                        bool isInserter = buildPreview.desc.isInserter;
                        Material material;
                        if (isInserter)
                        {
                            Material original = buildPreview.condition != EBuildCondition.Ok ? Configs.builtin.previewErrorMat_Inserter : (IsInserterConnected(buildPreview) ? Configs.builtin.previewOkMat_Inserter : Configs.builtin.previewGizmoMat_Inserter);
                            Material existingMaterial = __instance.previewRenderers[previewIndex].sharedMaterial;

                            if (existingMaterial != null && !existingMaterial.name.StartsWith(original.name))
                            {
                                UnityEngine.Object.Destroy(existingMaterial);
                                existingMaterial = null;
                            }

                            if (existingMaterial == null)
                            {
                                material = UnityEngine.Object.Instantiate<Material>(original);
                            }
                            else
                            {
                                material = existingMaterial;
                            }

                            __instance.GetInserterT1T2(buildPreview.objId, out bool t, out bool t2);
                            if (buildPreview.outputObjId != 0 && !__instance.ObjectIsBelt(buildPreview.outputObjId) && !__instance.ObjectIsInserter(buildPreview.outputObjId))
                            {
                                t2 = true;
                            }
                            if (buildPreview.inputObjId != 0 && !__instance.ObjectIsBelt(buildPreview.inputObjId) && !__instance.ObjectIsInserter(buildPreview.inputObjId))
                            {
                                t = true;
                            }

                            material.SetVector("_Position1", __instance.Vector3BoolToVector4(Vector3.zero, t));
                            material.SetVector("_Rotation1", __instance.QuaternionToVector4(Quaternion.identity));
                            material.SetVector("_Position2", __instance.Vector3BoolToVector4(Quaternion.Inverse(buildPreview.lrot) * (buildPreview.lpos2 - buildPreview.lpos), t2));
                            material.SetVector("_Rotation2", __instance.QuaternionToVector4(Quaternion.Inverse(buildPreview.lrot) * buildPreview.lrot2));
                            __instance.previewRenderers[previewIndex].enabled = (buildPreview.condition != EBuildCondition.NeedConn);
                        }
                        else
                        {
                            __instance.previewRenderers[previewIndex].enabled = true;
                            Material original = ((buildPreview.condition != EBuildCondition.Ok) ? Configs.builtin.previewErrorMat : Configs.builtin.previewOkMat); ;

                            Material existingMaterial = __instance.previewRenderers[previewIndex].sharedMaterial;

                            if (existingMaterial != null && !existingMaterial.name.StartsWith(original.name))
                            {
                                UnityEngine.Object.Destroy(existingMaterial);
                                existingMaterial = null;
                            }

                            if (existingMaterial == null)
                            {
                                material = UnityEngine.Object.Instantiate<Material>(original);
                            }
                            else
                            {
                                material = existingMaterial;
                            }
                        }
                        __instance.previewRenderers[previewIndex].sharedMaterial = material;
                    }
                }
                else if (buildPreview.previewIndex >= 0)
                {
                    __instance.FreePreviewModel(buildPreview);
                }
                if (buildPreview.isConnNode)
                {
                    uint color = 4U;
                    if (buildPreview.condition != EBuildCondition.Ok)
                    {
                        color = 0U;
                    }
                    if (graphPoints < pointCount)
                    {
                        __instance.connGraph.points[graphPoints] = buildPreview.lpos;
                        __instance.connGraph.colors[graphPoints] = color;
                    }
                    else
                    {
                        __instance.connGraph.AddPoint(buildPreview.lpos, color);
                    }
                    graphPoints++;
                }
            }
            __instance.connGraph.SetPointCount(graphPoints);
            if (graphPoints > 0)
            {
                __instance.showConnGraph = true;
            }

            return false;
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlanetFactory), "WriteObjectConn")]
        public static void WriteObjectConn_Prefix(ref PlanetFactory __instance, int otherObjId, ref int otherSlot)
        {
            // allow to write connection to prebuild object when the otherSlot is not known (equals to -1)
            if (otherSlot == -1 && otherObjId < 0)
            {
                for (int i = 4; i < 12; i++)
                {
                    if (__instance.prebuildConnPool[-otherObjId * 16 + i] == 0)
                    {
                        otherSlot = i;
                        break;
                    }
                }
            }
        }


    }
}
