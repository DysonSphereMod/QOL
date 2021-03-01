using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedStarmap
{
    [BepInPlugin("com.brokenmass.plugin.DSP.AdvancedStarmap", "AdvancedStarmap", "1.0.0")]
    public class AdvancedStarmap : BaseUnityPlugin
    {
        internal class StarInfoEntry
        {
            public GameObject gameObject;
            public Text label;
            public Text value;
        }

        Harmony harmony;

        static Dictionary<string, StarInfoEntry> starInfos = new Dictionary<string, StarInfoEntry>();

        static readonly string BG_PATH = "UI Root/Overlay Canvas/In Game/Planet & Star Details/star-detail-ui/black-bg";
        static readonly string LAST_LABEL_PATH = "UI Root/Overlay Canvas/In Game/Planet & Star Details/star-detail-ui/param-group/label (6)";
        void Start()
        {

            harmony = new Harmony("com.brokenmass.plugin.DSP.AdvancedStarmap");
            try
            {
                harmony.PatchAll(typeof(AdvancedStarmap));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        internal void OnDestroy()
        {
            // For ScriptEngine hot-reloading
            harmony.UnpatchSelf();

            foreach (var item in starInfos.Values)
            {
                Destroy(item.gameObject);
            }

            var blackBg = GameObject.Find(BG_PATH).GetComponent<RectTransform>();
            blackBg.offsetMin -= new Vector2(0f, -20f * starInfos.Count);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIStarDetail), "_OnOpen")]
        public static void UIStarDetail__OnOpen_Prefix(UIStarDetail __instance)
        {
            if (!starInfos.ContainsKey("star-details-min-radius"))
            {
                AddStarInfo("star-details-min-radius", "Dyson sphere min radius");
            }
            if (!starInfos.ContainsKey("star-details-max-radius"))
            {
                AddStarInfo("star-details-max-radius", "Dyson sphere max radius");
            }

            var minSphereRadius = Mathf.Max(4000f, __instance.star.physicsRadius * 1.5f);
            if (__instance.star.type == EStarType.GiantStar)
            {
                minSphereRadius *= 0.6f;
            }
            minSphereRadius = Mathf.Ceil(minSphereRadius / 100f) * 100f;
            starInfos["star-details-min-radius"].value.text = minSphereRadius.ToString("0");

            var maxSphereRadius = Mathf.Round((float)((double)__instance.star.dysonRadius * 40000.0) * 2f / 100f) * 100f;
            starInfos["star-details-max-radius"].value.text = maxSphereRadius.ToString("0");

            Debug.Log(JsonUtility.ToJson(GameMain.data.galacticTransport.stationPool, true));
        }


        [HarmonyPrefix, HarmonyPatch(typeof(UIStarDetail), "_OnUpdate")]
        public static void UIStarDetail__OnUpdate_Prefix(UIStarDetail __instance)
        {

        }

        public static void AddStarInfo(string id, string labelText)
        {
            var originalDetailLabel = GameObject.Find(LAST_LABEL_PATH);
            if (originalDetailLabel == null)
            {
                throw new InvalidOperationException("Star detail info base entry is not present");
            }

            GameObject gameObject = null;
            var originalDetailLabelText = originalDetailLabel.GetComponent<Text>();

            gameObject = Instantiate(originalDetailLabel, originalDetailLabel.transform.position, Quaternion.identity);
            Destroy(gameObject.GetComponentInChildren<Localizer>());

            gameObject.name = id;
            gameObject.transform.SetParent(originalDetailLabel.transform.parent);

            var textComponents = gameObject.GetComponentsInChildren<Text>();
            var label = textComponents[0];
            var value = textComponents[1];

            label.text = labelText;
            label.rectTransform.offsetMax = originalDetailLabelText.rectTransform.offsetMax;
            label.rectTransform.offsetMin = originalDetailLabelText.rectTransform.offsetMin;

            gameObject.transform.localScale = new Vector3(1f, 1f, 1f);
            gameObject.transform.localPosition = originalDetailLabel.transform.localPosition + new Vector3(0f, -20f * (1 + starInfos.Count), 0f);
            gameObject.transform.right = originalDetailLabel.transform.right;

            var blackBg = GameObject.Find(BG_PATH).GetComponent<RectTransform>();
            blackBg.offsetMin += new Vector2(0f, -20f);

            StarInfoEntry newStarInfoEntry = new StarInfoEntry()
            {
                gameObject = gameObject,
                label = label,
                value = value
            };
            starInfos.Add(id, newStarInfoEntry);
        }
    }
}