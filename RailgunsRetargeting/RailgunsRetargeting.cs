using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RailgunsRetargeting
{
    [BepInPlugin("com.brokenmass.plugin.DSP.RailgunsRetargeting", "RailgunsRetargeting", "1.3.1")]
    public class RailgunsRetargeting : BaseUnityPlugin
    {
        Harmony harmony;

        public static ConfigEntry<bool> ignoreOrbit1;
        public static ConfigEntry<bool> forceRetargeting;

        static readonly int BATCH_COUNT = 60;

        static readonly Color TEXT_CYAN = new Color(0.3820755f, 0.8455189f, 1f, 0.7058824f);
        static readonly Color TEXT_ORANGE = new Color(0.990566f, 0.5896651f, 0.369126f, 0.7058824f);
        static readonly Color TEXT_WHITE = new Color(0.5882353f, 0.5882353f, 0.5882353f, 0.8196079f);
        static readonly Color BUTTON_CYAN = new Color(0.3820755f, 0.8455189f, 1f, 0.375f);

        internal class ManagedEjector
        {
            public int originalOrbitId;
        }
        public static int batch;
        private static Dictionary<int, ManagedEjector> managedEjectors = new Dictionary<int, ManagedEjector>();
        private static GameObject autoRetargetingGO;

        internal void Awake()
        {
            harmony = new Harmony("com.brokenmass.plugin.DSP.RailgunsRetargeting");
            ignoreOrbit1 = Config.Bind<bool>("General", "ignoreOrbit1", false, "If you set this to true the railguns will never retarget to orbit 1 (the default, undeletable, orbit) unless is the primary orbit of the railgun");
            forceRetargeting = Config.Bind<bool>("General", "forceRetargeting", true, "Retarget railguns without a defined orbit");

            try
            {
                harmony.PatchAll(typeof(RailgunsRetargeting));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            // For ScriptEngine hot-reloading
            if (autoRetargetingGO != null)
            {
                Destroy(autoRetargetingGO);
            }
            harmony.UnpatchSelf();
        }

        internal static int GetEjectorUID(EjectorComponent ejector)
        {
            return ejector.planetId * 10000 + ejector.id;
        }

        internal static ManagedEjector GetOrCreateManagedEjector(int ejectorUID, int originalOrbitId)
        {

            if (!managedEjectors.ContainsKey(ejectorUID))
            {
                var managedEjector = new ManagedEjector()
                {
                    originalOrbitId = originalOrbitId
                };
                managedEjectors.Add(ejectorUID, managedEjector);

            }

            return managedEjectors[ejectorUID];

        }

        public static bool IsOrbitValid(int orbitId, DysonSwarm swarm)
        {
            return orbitId > 0 && orbitId < swarm.orbitCursor && swarm.orbits[orbitId].id == orbitId && swarm.orbits[orbitId].enabled;
        }

        public static void SetOrbit(ref EjectorComponent ejector, int orbitId)
        {
            ejector.orbitId = orbitId;
            if (ejector.direction == 1)
            {
                ejector.direction = -1;
                ejector.time = (int)((long)ejector.time * (long)ejector.coldSpend / (long)ejector.chargeSpend);
            }
        }

        public static bool IsOrbitReachable(EjectorComponent ejector, DysonSwarm swarm, AstroPose[] astroPoses, int orbitId)
        {
            if (!IsOrbitValid(orbitId, swarm))
            {
                return false;
            }

            int planetIndex = ejector.planetId / 100 * 100;
            float num4 = ejector.localAlt + ejector.pivotY + (ejector.muzzleY - ejector.pivotY) / Mathf.Max(0.1f, Mathf.Sqrt(1f - ejector.localDir.y * ejector.localDir.y));
            Vector3 vector = new Vector3(ejector.localPosN.x * num4, ejector.localPosN.y * num4, ejector.localPosN.z * num4);
            VectorLF3 vectorLF = astroPoses[ejector.planetId].uPos + Maths.QRotateLF(astroPoses[ejector.planetId].uRot, vector);
            Quaternion q = astroPoses[ejector.planetId].uRot * ejector.localRot;
            VectorLF3 uPos = astroPoses[planetIndex].uPos;
            VectorLF3 b = uPos - vectorLF;
            VectorLF3 vectorLF2 = uPos + VectorLF3.Cross(swarm.orbits[orbitId].up, b).normalized * (double)swarm.orbits[orbitId].radius;
            VectorLF3 vec = vectorLF2 - vectorLF;
            var targetDist = vec.magnitude;
            vec.x /= targetDist;
            vec.y /= targetDist;
            vec.z /= targetDist;
            Vector3 vector2 = Maths.QInvRotate(q, vec);

            if ((double)vector2.y < 0.08715574 || vector2.y > 0.8660254f)
            {
                return false;
            }

            for (int i = planetIndex + 1; i <= ejector.planetId + 2; i++)
            {
                if (i != ejector.planetId)
                {
                    double num5 = (double)astroPoses[i].uRadius;
                    if (num5 > 1.0)
                    {
                        VectorLF3 vectorLF3 = astroPoses[i].uPos - vectorLF;
                        double num6 = vectorLF3.x * vectorLF3.x + vectorLF3.y * vectorLF3.y + vectorLF3.z * vectorLF3.z;
                        double num7 = vectorLF3.x * vec.x + vectorLF3.y * vec.y + vectorLF3.z * vec.z;
                        if (num7 > 0.0)
                        {
                            double num8 = num6 - num7 * num7;
                            num5 += 120.0;
                            if (num8 < num5 * num5)
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(FactorySystem), "GameTick")]
        public static void EjectorComponent_InternalUpdate_Prefix(long time)
        {
            batch = (int)(time % BATCH_COUNT);
        }
        [HarmonyPrefix, HarmonyPatch(typeof(FactorySystem), "RemoveEjectorComponent")]
        public static void FactorySystem_RemoveEjectorComponent_Prefix(FactorySystem __instance, int id)
        {
            if (__instance.ejectorPool[id].id != 0)
            {
                var ejector = __instance.ejectorPool[id];
                var ejectorUID = GetEjectorUID(ejector);

                managedEjectors.Remove(ejectorUID);
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(EjectorComponent), "Export")]
        public static void EjectorComponent_Export_Prefix(ref EjectorComponent __instance, ref int __state)
        {
            // at save time we store the current orbitId , replace it with the originalOne, and restore it after the save.
            var ejectorUID = GetEjectorUID(__instance);
            ManagedEjector managedEjector = GetOrCreateManagedEjector(ejectorUID, __instance.orbitId);
            __state = __instance.orbitId;
            __instance.orbitId = managedEjector.originalOrbitId;
        }
        [HarmonyPostfix, HarmonyPatch(typeof(EjectorComponent), "Export")]
        public static void EjectorComponent_Export_Postfix(ref EjectorComponent __instance, ref int __state)
        {
            __instance.orbitId = __state;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(EjectorComponent), "SetOrbit")]
        public static void EjectorComponent_SetOrbit_Postfix(ref EjectorComponent __instance, int _orbitId)
        {
            var ejectorUID = GetEjectorUID(__instance);

            ManagedEjector managedEjector = GetOrCreateManagedEjector(ejectorUID, _orbitId);

            managedEjector.originalOrbitId = _orbitId;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(EjectorComponent), "InternalUpdate")]
        public static void EjectorComponent_InternalUpdate_Postfix(ref EjectorComponent __instance, DysonSwarm swarm, AstroPose[] astroPoses)
        {
            var ejectorUID = GetEjectorUID(__instance);

            if (ejectorUID % BATCH_COUNT != batch)
            {
                return;
            }

            ManagedEjector managedEjector = GetOrCreateManagedEjector(ejectorUID, __instance.orbitId);

            if (!IsOrbitValid(managedEjector.originalOrbitId, swarm))
            {
                managedEjector.originalOrbitId = 0;
            }

            if (__instance.orbitId != managedEjector.originalOrbitId && IsOrbitReachable(__instance, swarm, astroPoses, managedEjector.originalOrbitId))
            {
                // by default we try to check if the original orbit is available
                SetOrbit(ref __instance, managedEjector.originalOrbitId);
            }
            else if (
                (__instance.targetState == EjectorComponent.ETargetState.AngleLimit ||
                __instance.targetState == EjectorComponent.ETargetState.Blocked ||
                (__instance.targetState == EjectorComponent.ETargetState.None && forceRetargeting.Value))

                && swarm.orbitCursor > 1)
            {
                // if the current orbit is not reachable activate auto targeting
                var testOrbit = __instance.orbitId;
                var orbitsCount = swarm.orbitCursor;
                while (--orbitsCount > 0)
                {
                    testOrbit++;
                    if (testOrbit >= swarm.orbitCursor)
                    {
                        testOrbit = 1;
                    }

                    if (testOrbit == 1 && ignoreOrbit1.Value)
                    {
                        continue;
                    }

                    if (IsOrbitReachable(__instance, swarm, astroPoses, testOrbit))
                    {
                        SetOrbit(ref __instance, testOrbit);
                        return;
                    }
                }

                // no alternative orbit has been found. set the original as default
                SetOrbit(ref __instance, managedEjector.originalOrbitId);

            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIEjectorWindow), "_OnUpdate")]
        public static void UIEjectorWindow__OnUpdate_Postfix(ref UIEjectorWindow __instance, ref UIOrbitPicker ___orbitPicker)
        {
            if (__instance.ejectorId != 0 && __instance.factory != null)
            {
                if (autoRetargetingGO == null)
                {
                    var stateText = GameObject.Find("UI Root/Overlay Canvas/In Game/Windows/Ejector Window/state/state-text");
                    autoRetargetingGO = Instantiate(stateText, stateText.transform.position, Quaternion.identity);

                    autoRetargetingGO.name = "auto-retargeting";
                    autoRetargetingGO.transform.SetParent(stateText.transform.parent);


                    autoRetargetingGO.transform.localScale = new Vector3(1f, 1f, 1f);
                    autoRetargetingGO.transform.localPosition = stateText.transform.localPosition + new Vector3(0f, -24f, 0f);
                }

                EjectorComponent ejector = __instance.factorySystem.ejectorPool[__instance.ejectorId];
                var ejectorUID = GetEjectorUID(ejector);

                var text = "Retargeting - Original Orbit";
                var color = TEXT_WHITE;

                ManagedEjector managedEjector = GetOrCreateManagedEjector(ejectorUID, ejector.orbitId);
                bool isAlternativeOrbit = ejector.orbitId != managedEjector.originalOrbitId;

                if (ejector.targetState == EjectorComponent.ETargetState.AngleLimit)
                {
                    text = "Retargeting - No valid alternative";
                    color = TEXT_ORANGE;
                }

                if (isAlternativeOrbit)
                {
                    ___orbitPicker.orbitId = managedEjector.originalOrbitId;

                    text = $"Retargeting - Alternative Orbit [ {ejector.orbitId} ]";
                    color = TEXT_CYAN;
                }


                for (var i = 0; i < ___orbitPicker.orbitButtons.Length; i++)
                {
                    var orbitButton = ___orbitPicker.orbitButtons[i];
                    var transition = orbitButton.transitions[0];

                    if (orbitButton.highlighted || orbitButton.updating || orbitButton.isPointerEnter || orbitButton.isPointerDown || !orbitButton.button.interactable)
                    {
                        continue;
                    }

                    if (isAlternativeOrbit && i == ejector.orbitId)
                    {
                        transition.target.color = BUTTON_CYAN;
                    }
                    else if (transition.target.color != transition.normalColor)
                    {
                        transition.target.color = transition.normalColor;
                    }
                }

                var autoRetargetingText = autoRetargetingGO.GetComponent<Text>();
                autoRetargetingText.text = text;
                autoRetargetingText.color = color;
            }
        }


    }
}
