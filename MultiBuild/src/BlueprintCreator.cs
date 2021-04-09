using HarmonyLib;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    [HarmonyPatch]
    class BlueprintCreator
    {
        private static Color BP_GRID_COLOR = new Color(1f, 1f, 1f, 0.2f);
        private static Color ADD_SELECTION_GIZMO_COLOR = new Color(1f, 1f, 1f, 1f);
        private static Color REMOVE_SELECTION_GIZMO_COLOR = new Color(0.9433962f, 0.1843137f, 0.1646493f, 1f);
        private static CircleGizmo areaSelectionGizmo;
        private static BoxGizmo referenceGizmo;
        private static BoxGizmo referenceSelectionGizmo;

        public static bool bpMode = false;
        public static int selectionRadius = 5;

        private static int referenceId = 0;
        private static Dictionary<int, BoxGizmo> bpSelection = new Dictionary<int, BoxGizmo>();
        private static Collider[] _tmp_cols = new Collider[1024];

        public static void OnUpdate()
        {
            if ((Input.GetKeyUp(KeyCode.Equals) || Input.GetKeyUp(KeyCode.KeypadPlus)) && BlueprintCreator.bpMode && BlueprintCreator.selectionRadius < 14)
            {
                ++BlueprintCreator.selectionRadius;
            }

            if ((Input.GetKeyUp(KeyCode.Minus) || Input.GetKeyUp(KeyCode.KeypadMinus)) && BlueprintCreator.bpMode && BlueprintCreator.selectionRadius > 1)
            {
                --BlueprintCreator.selectionRadius;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIBuildingGrid), "Update")]
        public static void UIBuildingGrid_Update_Postfix(ref UIBuildingGrid __instance)
        {
            if (bpMode)
            {
                __instance.material.SetColor("_TintColor", BP_GRID_COLOR);
            }
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(PlayerAction_Inspect), "SetInspectee")]
        public static bool PlayerAction_Inspect_SetInspectee_Prefix()
        {
            return !bpMode;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "SetCopyInfo")]
        public static void PlayerAction_Build_SetCopyInfo_Postfix(ref PlayerAction_Build __instance, int objectId)
        {
            if (bpMode)
            {
                EndBpMode(true);
            }
            BlueprintManager.PrepareNew();
            if (objectId < 0)
                return;

            var itemProto = LDB.items.Select(__instance.factory.entityPool[objectId].protoId);

            if (itemProto.prefabDesc.insertPoses.Length > 0)
            {

                BlueprintManager.Copy(objectId);

                if (BlueprintManager.hasData)
                {
                    BlueprintManager.data.copiedBuildings[0].recipeId = __instance.copyRecipeId;
                    __instance.yaw = 0f;
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlayerAction_Build), "GameTick")]
        public static void PlayerAction_Build_GameTick_Postfix(ref PlayerAction_Build __instance)
        {
            if (!bpMode) return;
            bool referenceMode = VFInput.alt;
            if (referenceMode)
            {
                AlterReference(__instance);
            }
            else
            {
                AlterSelection(__instance);
            }
        }

        public static void AlterSelection(PlayerAction_Build __instance)
        {
            bool removeMode = VFInput.control;
            if (referenceSelectionGizmo != null)
            {
                referenceSelectionGizmo.Close();
                referenceSelectionGizmo = null;
            }
            if (areaSelectionGizmo == null)
            {
                areaSelectionGizmo = CircleGizmo.Create(6, Vector3.zero, 10);

                areaSelectionGizmo.fadeOutScale = areaSelectionGizmo.fadeInScale = 1.8f;
                areaSelectionGizmo.fadeOutTime = areaSelectionGizmo.fadeInTime = 0.15f;
                areaSelectionGizmo.autoRefresh = true;
                areaSelectionGizmo.Open();
            }

            areaSelectionGizmo.color = removeMode ? REMOVE_SELECTION_GIZMO_COLOR : ADD_SELECTION_GIZMO_COLOR;
            areaSelectionGizmo.radius = selectionRadius;

            if (__instance.groundTestPos != Vector3.zero)
            {
                areaSelectionGizmo.position = __instance.groundTestPos;
            }


            if (VFInput._buildConfirm.pressing)
            {
                areaSelectionGizmo.color = removeMode ? REMOVE_SELECTION_GIZMO_COLOR : ADD_SELECTION_GIZMO_COLOR;

                // target only buildings
                int mask = 131072;
                int found = Physics.OverlapBoxNonAlloc(__instance.groundTestPos, new Vector3(selectionRadius, 100f, selectionRadius), _tmp_cols, Maths.SphericalRotation(__instance.groundTestPos, 0f), mask, QueryTriggerInteraction.Collide);

                PlanetPhysics planetPhysics = __instance.player.planetData.physics;

                for (int i = 0; i < found; i++)
                {
                    planetPhysics.GetColliderData(_tmp_cols[i], out ColliderData colliderData);
                    if (colliderData.objId > 0)
                    {
                        var entityId = colliderData.objId;
                        if (removeMode)
                        {
                            if (bpSelection.ContainsKey(entityId))
                            {
                                bpSelection[entityId].Close();
                                bpSelection.Remove(entityId);

                            }
                            if (entityId == referenceId)
                            {
                                referenceId = 0;
                            }
                        }
                        else if (!bpSelection.ContainsKey(entityId))
                        {
                            var entityData = __instance.factory.entityPool[entityId];
                            ItemProto itemProto = LDB.items.Select((int)entityData.protoId);
                            var gizmo = BoxGizmo.Create(entityData.pos, entityData.rot, itemProto.prefabDesc.selectCenter, itemProto.prefabDesc.selectSize);
                            gizmo.multiplier = 1f;
                            gizmo.alphaMultiplier = itemProto.prefabDesc.selectAlpha;
                            gizmo.fadeInScale = gizmo.fadeOutScale = 1.3f;
                            gizmo.fadeInTime = gizmo.fadeOutTime = 0.05f;
                            gizmo.fadeInFalloff = gizmo.fadeOutFalloff = 0.5f;
                            gizmo.color = Color.white;

                            gizmo.Open();

                            bpSelection.Add(entityId, gizmo);
                        }
                    }
                }

                if (referenceId == 0)
                {
                    var belts = new List<int>();
                    var buildings = new List<int>();

                    foreach (var entityId in bpSelection.Keys)
                    {
                        var entityData = __instance.factory.entityPool[entityId];

                        if (entityId != entityData.id)
                        {
                            continue;
                        }

                        ItemProto itemProto = LDB.items.Select((int)entityData.protoId);
                        if (itemProto.prefabDesc.isBelt)
                        {
                            belts.Add(entityId);
                        }
                        else if (!itemProto.prefabDesc.isInserter)
                        {
                            buildings.Add(entityId);
                        }
                    }

                    if (buildings.Count > 0)
                    {
                        referenceId = buildings.First();
                    }
                    else if (belts.Count > 0)
                    {
                        referenceId = belts.First();
                    }
                }
            }

            if (referenceId != 0 && bpSelection.TryGetValue(referenceId, out BoxGizmo reference))
            {
                if (referenceGizmo == null)
                {
                    referenceGizmo = BoxGizmo.Create(Vector3.zero, Quaternion.identity, Vector3.zero, new Vector3(0.5f, 100f, 0.5f));
                    referenceGizmo.multiplier = 1f;
                    referenceGizmo.alphaMultiplier = 0.5f;
                    referenceGizmo.fadeInScale = referenceGizmo.fadeOutScale = 1.3f;
                    referenceGizmo.fadeInTime = referenceGizmo.fadeOutTime = 0.05f;
                    referenceGizmo.fadeInFalloff = referenceGizmo.fadeOutFalloff = 0.5f;
                    referenceGizmo.color = Color.green;

                    referenceGizmo.Open();
                }

                referenceGizmo.transform.position = reference.transform.position;
                referenceGizmo.transform.rotation = reference.transform.rotation;
                referenceGizmo.center = reference.center;

            }
        }

        public static void AlterReference(PlayerAction_Build __instance)
        {
            if (referenceSelectionGizmo == null)
            {
                referenceSelectionGizmo = BoxGizmo.Create(Vector3.zero, Quaternion.identity, Vector3.zero, new Vector3(0.5f, 100f, 0.5f));
                referenceSelectionGizmo.multiplier = 1f;
                referenceSelectionGizmo.alphaMultiplier = 0.5f;
                referenceSelectionGizmo.fadeInScale = referenceSelectionGizmo.fadeOutScale = 1.3f;
                referenceSelectionGizmo.fadeInTime = referenceSelectionGizmo.fadeOutTime = 0.05f;
                referenceSelectionGizmo.fadeInFalloff = referenceSelectionGizmo.fadeOutFalloff = 0.5f;
                referenceSelectionGizmo.color = Color.cyan;

            }

            if (areaSelectionGizmo != null)
            {
                areaSelectionGizmo.Close();
                areaSelectionGizmo = null;
            }

            bool isValidReference = false;
            if (__instance.castObjId != 0 && __instance.castObjId != referenceId && bpSelection.TryGetValue(__instance.castObjId, out BoxGizmo reference))
            {

                var entityData = __instance.factory.entityPool[__instance.castObjId];
                ItemProto itemProto = LDB.items.Select((int)entityData.protoId);
                if (__instance.castObjId == entityData.id &&
                    !itemProto.prefabDesc.isInserter &&
                    !itemProto.prefabDesc.isBelt &&
                    itemProto.prefabDesc.minerType == EMinerType.None)
                {
                    referenceSelectionGizmo.transform.position = reference.transform.position;
                    referenceSelectionGizmo.transform.rotation = reference.transform.rotation;
                    referenceSelectionGizmo.center = reference.center;

                    isValidReference = true;
                }
            }


            if (isValidReference)
            {
                referenceSelectionGizmo.Open();
            }
            else
            {
                referenceSelectionGizmo.Close();
                referenceSelectionGizmo = null;
            }

            if (VFInput._buildConfirm.pressing && isValidReference)
            {
                referenceId = __instance.castObjId;

                referenceGizmo.transform.position = referenceSelectionGizmo.transform.position;
                referenceGizmo.transform.rotation = referenceSelectionGizmo.transform.rotation;
                referenceGizmo.center = referenceSelectionGizmo.center;
            }
        }
        public static void StartBpMode()
        {
            if (bpMode) return;

            bpMode = true;
            var actionBuild = GameMain.data.mainPlayer.controller.actionBuild;
            actionBuild.altitude = 0;
            actionBuild.player.SetHandItems(0, 0, 0);
            actionBuild.controller.cmd.type = ECommand.Build;
            actionBuild.controller.cmd.mode = 0;

            BuildLogic.lastPosition = Vector3.zero;
            BlueprintManager.PrepareNew();
        }

        public static void EndBpMode(bool ignoreBlueprint = false)
        {
            if (!bpMode) return;

            if (!ignoreBlueprint)
            {
                var ids = bpSelection.Keys.ToList();

                BlueprintManager.Copy(ids, referenceId);

                if (BlueprintManager.hasData)
                {
                    BlueprintManager.EnterBuildModeAfterBp();
                }
            }

            bpMode = false;
            referenceId = 0;
            foreach (var selectionGizmo in bpSelection.Values)
            {
                selectionGizmo.Close();
            }

            bpSelection.Clear();
            if (areaSelectionGizmo != null)
            {
                areaSelectionGizmo.Close();
                areaSelectionGizmo = null;
            }
            if (referenceGizmo != null)
            {
                referenceGizmo.Close();
                referenceGizmo = null;
            }
            if (referenceSelectionGizmo != null)
            {
                referenceSelectionGizmo.Close();
                referenceSelectionGizmo = null;
            }



            GC.Collect();
        }

    }
}
