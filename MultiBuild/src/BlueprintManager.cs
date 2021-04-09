using UnityEngine;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Collections.Concurrent;

namespace com.brokenmass.plugin.DSP.MultiBuild
{
    public enum EPastedType
    {
        BUILDING,
        BELT,
        INSERTER
    }

    public enum EPastedStatus
    {
        NEW,
        UPDATE,
        REMOVE
    }

    public class PastedEntity
    {
        public int pasteId;
        public EPastedStatus status;
        public EPastedType type;
        public BuildingCopy sourceBuilding;
        public BeltCopy sourceBelt;
        public InserterCopy sourceInserter;
        public BuildPreview buildPreview;
        public ConcurrentDictionary<int, PastedEntity> connectedEntities = new ConcurrentDictionary<int, PastedEntity>();
        public Pose pose;
        public int objId;
        public int postObjId;
    }

    [HarmonyPatch]
    public class BlueprintManager
    {
        public static bool hasData = false;
        public static BlueprintData data = new BlueprintData();
        public static BlueprintData previousData = new BlueprintData();

        public static ConcurrentDictionary<int, PastedEntity> pastedEntities = new ConcurrentDictionary<int, PastedEntity>(Util.MAX_THREADS, 0);

        public static void PrepareNew()
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

        public static void Copy(int entityId)
        {
            Copy(new List<int>() { entityId }, entityId);
        }
        public static void Copy(List<int> entityIds, int referenceId)
        {
            hasData = false;
            data.Clear();
            pastedEntities.Clear();
            hasData = BlueprintManager_Copy.Copy(data, entityIds, referenceId);
        }


        public static void PreparePaste()
        {
            InserterPoses.ResetOverrides();

            BlueprintManager_Paste.PrepareThreads();

            foreach (var pastedEntity in pastedEntities.Values)
            {
                pastedEntity.status = EPastedStatus.REMOVE;
            }
        }
        public static void Paste(Vector3 targetPos, float yaw, bool pasteInserters = true, int copyIndex = 0)
        {
            BlueprintManager_Paste.Paste(data, targetPos, yaw, pasteInserters, copyIndex);
        }
        public static void AfterPaste()
        {
            PlayerAction_Build actionBuild = GameMain.data.mainPlayer.controller.actionBuild;

            var entitiesToRemove = pastedEntities.Where(entity => entity.Value.status == EPastedStatus.REMOVE).ToList();
            foreach (var pastedEntity in entitiesToRemove)
            {
                actionBuild.RemoveBuildPreview(pastedEntity.Value.buildPreview);
                pastedEntities.TryRemove(pastedEntity.Key, out _);
                pastedEntity.Value.buildPreview.Free();
            }
        }


        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "ClearBuildPreviews")]
        public static void PlayerAction_Build_ClearBuildPreviews_Prefix()
        {
            pastedEntities.Clear();
            InserterPoses.ResetOverrides();
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

                try
                {
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
                catch
                {
                    // ignore exception if the pasted buffer is bigger than the limit. the ui will miss some belt gizmos but no 'scary' error will be displayed
                }
            }
        }
    }
}
