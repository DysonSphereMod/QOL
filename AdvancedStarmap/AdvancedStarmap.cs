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
    // TODO: button to next producer/consumer
    [BepInPlugin("com.brokenmass.plugin.DSP.AdvancedStarmap", "AdvancedStarmap", "1.0.0")]
    public class AdvancedStarmap : BaseUnityPlugin
    {
        public class StarmapInfoEntry
        {
            public GameObject gameObject;
            public Text label;
            public Text value;
        }

        Harmony harmony;

        static Dictionary<string, StarmapInfoEntry> starInfos = new Dictionary<string, StarmapInfoEntry>();

        static readonly string STARINFO_BG = "UI Root/Overlay Canvas/In Game/Planet & Star Details/star-detail-ui/black-bg";
        static readonly string STARINFO_LASTLABEL = "UI Root/Overlay Canvas/In Game/Planet & Star Details/star-detail-ui/param-group/label (6)";
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

            var blackBg = GameObject.Find(STARINFO_BG).GetComponent<RectTransform>();
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

        private class productMetrics
        {
            public ItemProto itemProto;
            public float production = 0;
            public float consumption = 0;
            public int producers = 0;
            public int consumers = 0;
        }

        private static void EnsureId(ref Dictionary<int, productMetrics> dict, int id)
        {
            if (!dict.ContainsKey(id))
            {
                ItemProto itemProto = LDB.items.Select(id);

                dict.Add(id, new productMetrics()
                {
                    itemProto = itemProto
                });
            }
        }

        private static Dictionary<int, productMetrics> counter;

        private static string formatMetric (float metric, int statTimeLevel)
        {
            float[] multipliers = { 600f, 60f, 1f, 0.1f };


            var value = (multipliers[statTimeLevel] * metric);

            if (value >= 10000.0)
            {
                return ((long)value).ToString();
            }
            else if (value > 1000.0)
            {
                return value.ToString("F0");
            }
            else if (value > 0.0)
            {
                return value.ToString("F1");
            }
            else
            {
                return value.ToString();
            }
            
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductEntry), "UpdateProduct")]
        public static void UIProductEntry_UpdateProduct_Postfix(UIProductEntry __instance)
        {
            //default font size 34
            //Debug.Log(__instance.consumeText.);

            int statTimeLevel = __instance.productionStatWindow.statTimeLevel;
            if (statTimeLevel != 5)
            {
                __instance.productLabel.text += "\n Theoretical max";
                Debug.Log($"{__instance.productText.GetComponent<RectTransform>().rect}, {__instance.productText.GetComponent<RectTransform>().offsetMin} , {__instance.productText.GetComponent<RectTransform>().offsetMax}, {__instance.productText.GetComponent<RectTransform>().anchoredPosition}, {__instance.productText.GetComponent<RectTransform>().anchorMax}, {__instance.productText.GetComponent<RectTransform>().anchorMin}");
                __instance.productLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 24);
                __instance.productText.GetComponent<RectTransform>().offsetMin = new Vector2(-100, -6);
                //__instance.productText.GetComponent<RectTransform>().wi

                //__instance.consumeText.fontSize = 30;
                __instance.consumeLabel.text += " / Theoretical max";
                //__instance.consumeLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 24);
                //__instance.consumeText.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 56);

                if ( counter.ContainsKey(__instance.itemId))
                {
                    var productMetrics = counter[__instance.itemId];

                    __instance.productText.text += $" / {formatMetric(productMetrics.production, statTimeLevel)}";
                    __instance.consumeText.text += $" / {formatMetric(productMetrics.consumption, statTimeLevel)}";
                } else
                {
                    __instance.productText.text += $" / 0";
                    __instance.consumeText.text += $" / 0";
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductionStatWindow), "_OnOpen")]
        public static void UIProductionStatWindow__OnOpen_Postfix(UIProductionStatWindow __instance)
        {
            //default font size 34
            //Debug.Log(__instance.consumeText.);
            //__instance.productText.fontSize = 10;

            if(__instance.gameData.localPlanet == null)
            {
                return;
            }

            var planet = __instance.gameData.localPlanet;
            counter = new Dictionary<int, productMetrics>();
            var factorySystem = planet.factory.factorySystem;

            var veinPool = planet.factory.veinPool;
            for (int i = 1; i < factorySystem.minerCursor; i++)
            {
                var miner = factorySystem.minerPool[i];
                if (i != miner.id) continue;

                var productId = miner.productId;
                var veinId = (miner.veinCount != 0) ? miner.veins[miner.currentVeinIndex] : 0;

                if (miner.type == EMinerType.Water)
                {
                    productId = planet.waterItemId;
                }
                else if (productId == 0)
                {
                    productId = veinPool[veinId].productId;
                }

                if (productId == 0) continue;


                EnsureId(ref counter, productId);

                float frequency = 1f / (float)((double)miner.period / 600000.0);
                float speed = (float)(0.0001 * (double)miner.speed * (double)GameMain.history.miningSpeedScale);

                float production = 0f;
                if (factorySystem.minerPool[i].type == EMinerType.Water)
                {
                    production = frequency * speed;
                }
                if (factorySystem.minerPool[i].type == EMinerType.Oil)
                {
                    production = frequency * speed * (float)((double)veinPool[veinId].amount * (double)VeinData.oilSpeedMultiplier); ;
                }
                if (factorySystem.minerPool[i].type == EMinerType.Vein)
                {
                    production = frequency * speed * miner.veinCount;
                }

                counter[productId].production += production;
                counter[productId].producers++;

            }
            for (int j = 1; j < factorySystem.assemblerCursor; j++)
            {
                var assembler = factorySystem.assemblerPool[j];
                if (assembler.id != j || assembler.recipeId == 0) continue;

                var period = 1 / (float)((double)assembler.timeSpend / 600000.0);
                var speed = (float)(0.0001 * (double)assembler.speed);

                for (int k = 0; k < assembler.requires.Length; k++)
                {
                    var productId = assembler.requires[k];
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += period * speed * assembler.requireCounts[k];
                    counter[productId].consumers++;
                }

                for (int k = 0; k < assembler.products.Length; k++)
                {
                    var productId = assembler.products[k];
                    EnsureId(ref counter, productId);

                    counter[productId].production += period * speed * assembler.productCounts[k];
                    counter[productId].producers++;
                }
            }
            for (int k = 1; k < factorySystem.fractionateCursor; k++)
            {
                var fractionator = factorySystem.fractionatePool[k];
                if (fractionator.id != k) continue;

                if (fractionator.need != 0)
                {
                    var productId = fractionator.need;
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += 30f * fractionator.produceProb;
                    counter[productId].consumers++;
                }
                if (fractionator.product != 0)
                {
                    var productId = fractionator.product;
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += 30f * fractionator.produceProb;
                    counter[productId].consumers++;
                }

            }
            for (int l = 1; l < factorySystem.ejectorCursor; l++)
            {
                if (factorySystem.ejectorPool[l].id != l) continue;
            }
            for (int m = 1; m < factorySystem.siloCursor; m++)
            {
                if (factorySystem.siloPool[m].id != m) continue;
            }
            for (int n = 1; n < factorySystem.labCursor; n++)
            {
                if (factorySystem.labPool[n].id != n) continue;
            }

            Debug.Log($"--------------------------");

            foreach (var entry in counter)
            {
                Debug.Log($"{entry.Value.itemProto.name} : {entry.Value.production} [{entry.Value.producers}]  |   {entry.Value.consumption} [{entry.Value.consumers}]");
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIPlanetDetail), "_OnOpen")]
        public static void UIPlanetDetail___OnOpen_Prefix(UIPlanetDetail __instance)
        {
            
        }


        public static StarmapInfoEntry duplicateStarmapEntry(string path, string id)
        {
            var originalDetailLabel = GameObject.Find(path);
            if (originalDetailLabel == null)
            {
                throw new InvalidOperationException("Star detail info base entry is not present");
            }

            var originalDetailLabelText = originalDetailLabel.GetComponent<Text>();

            GameObject gameObject = Instantiate(originalDetailLabel, originalDetailLabel.transform.position, Quaternion.identity);
            Destroy(gameObject.GetComponentInChildren<Localizer>());

            gameObject.name = id;
            gameObject.transform.SetParent(originalDetailLabel.transform.parent);

            var textComponents = gameObject.GetComponentsInChildren<Text>();
            var label = textComponents[0];

            label.rectTransform.offsetMax = originalDetailLabelText.rectTransform.offsetMax;
            label.rectTransform.offsetMin = originalDetailLabelText.rectTransform.offsetMin;

            gameObject.transform.localScale = new Vector3(1f, 1f, 1f);
            gameObject.transform.localPosition = originalDetailLabel.transform.localPosition + new Vector3(0f, -20f * (1 + starInfos.Count), 0f);
            gameObject.transform.right = originalDetailLabel.transform.right;


            return new StarmapInfoEntry()
            {
                gameObject = gameObject,
                label = label,
                value = textComponents[1]
            };
        }

        public static void AddStarInfo(string id, string labelText)
        {
            StarmapInfoEntry newStarInfoEntry = duplicateStarmapEntry(STARINFO_LASTLABEL, id);
            newStarInfoEntry.label.text = labelText;

            var blackBg = GameObject.Find(STARINFO_BG).GetComponent<RectTransform>();
            blackBg.offsetMin += new Vector2(0f, -20f);

            starInfos.Add(id, newStarInfoEntry);
        }
    }
}