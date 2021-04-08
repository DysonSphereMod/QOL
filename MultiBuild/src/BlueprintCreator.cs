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
        private static CircleGizmo circleGizmo;

        public static bool bpMode = false;
        public static int selectionRadius = 5;

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
            BlueprintManager.Reset();
            if (objectId < 0)
                return;

            var itemProto = LDB.items.Select(__instance.factory.entityPool[objectId].protoId);

            if (itemProto.prefabDesc.insertPoses.Length > 0)
            {
                var toAdd = new List<int>() { objectId };
                BlueprintManager.Copy(toAdd);

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
            if (bpMode)
            {
                bool removeMode = VFInput.control;

                if (circleGizmo != null)
                {
                    circleGizmo.color = removeMode ? REMOVE_SELECTION_GIZMO_COLOR : ADD_SELECTION_GIZMO_COLOR;
                    circleGizmo.radius = selectionRadius;

                    if (__instance.groundTestPos != Vector3.zero)
                    {
                        circleGizmo.position = __instance.groundTestPos;
                    }
                }

                if (VFInput._buildConfirm.pressing)
                {
                    circleGizmo.color = removeMode ? REMOVE_SELECTION_GIZMO_COLOR : ADD_SELECTION_GIZMO_COLOR;

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

                }
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
            BlueprintManager.Reset();
            if (circleGizmo == null)
            {
                circleGizmo = CircleGizmo.Create(6, Vector3.zero, 10);

                circleGizmo.fadeOutScale = circleGizmo.fadeInScale = 1.8f;
                circleGizmo.fadeOutTime = circleGizmo.fadeInTime = 0.15f;
                circleGizmo.autoRefresh = true;
                circleGizmo.Open();
            }

        }

        public static void EndBpMode(bool ignoreBlueprint = false)
        {
            if (!bpMode) return;
            bpMode = false;


            BlueprintManager.Copy(bpSelection.Keys.ToList());
            foreach (var selectionGizmo in bpSelection.Values)
            {
                selectionGizmo.Close();
            }


            bpSelection.Clear();
            if (circleGizmo != null)
            {
                circleGizmo.Close();
                circleGizmo = null;
            }

            if (BlueprintManager.hasData && !ignoreBlueprint)
            {
                BlueprintManager.EnterBuildModeAfterBp();
            }

            GC.Collect();
        }

    }
}
