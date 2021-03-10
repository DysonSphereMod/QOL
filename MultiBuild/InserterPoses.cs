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

        public Vector3 posDelta;
        public Vector3 pos2Delta;

        public int startSlot;
        public int endSlot;

        public short pickOffset;
        public short insertOffset;

        public int inputOriginalId;
        public int outputOriginalId;

        public int inputObjId;
        public int outputObjId;

        public EBuildCondition? condition;
    }

    internal class BuildPreviewOverride
    {
        public Pose pose;
        public ItemProto itemProto;
    }

    internal class InserterPoses : BaseUnityPlugin
    {
        private const int INITIAL_OBJ_ID = 2000000000;

        public static List<BuildPreviewOverride> overrides = new List<BuildPreviewOverride>();

        public static void ResetBuildPreviewsData()
        {
            overrides.Clear();
        }

        public static int addOverride(Pose pose, ItemProto itemProto)
        {
            overrides.Add(new BuildPreviewOverride()
            {
                pose = pose,
                itemProto = itemProto
            });

            return INITIAL_OBJ_ID + overrides.Count - 1;
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

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetObjectPose")]
        public static bool GetObjectPose_Prefix(int objId, ref Pose __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                __result = overrides[objId - INITIAL_OBJ_ID].pose;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetObjectProtoId")]
        public static bool GetObjectProtoId_Prefix(int objId, ref int __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                __result = overrides[objId - INITIAL_OBJ_ID].itemProto.ID;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "ObjectIsBelt")]
        public static bool ObjectIsBelt_Prefix(int objId, ref bool __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                __result = false;// overrides[objId - INITIAL_OBJ_ID].itemProto.prefabDesc.isBelt;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "ObjectIsInserter")]
        public static bool ObjectIsInserter_Prefix(int objId, ref bool __result)
        {
            if (objId >= INITIAL_OBJ_ID)
            {
                __result = overrides[objId - INITIAL_OBJ_ID].itemProto.prefabDesc.isInserter;
                return false;
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Build), "GetLocalInserts")]
        public static bool GetLocalInserts_Prefix(int objId, ref Pose[] __result)
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