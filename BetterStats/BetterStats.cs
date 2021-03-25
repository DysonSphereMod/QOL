using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BetterStats
{
    // TODO: button to next producer/consumer
    [BepInPlugin("com.brokenmass.plugin.DSP.BetterStats", "BetterStats", "1.0.0")]
    public class BetterStats : BaseUnityPlugin
    {
        Harmony harmony;
        private static Dictionary<int, ProductMetrics> counter = new Dictionary<int, ProductMetrics>();

        internal void Awake()
        {

            harmony = new Harmony("com.brokenmass.plugin.DSP.BetterStats");
            try
            {
                harmony.PatchAll(typeof(BetterStats));
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
        }


        private class ProductMetrics
        {
            public ItemProto itemProto;
            public float production = 0;
            public float consumption = 0;
            public int producers = 0;
            public int consumers = 0;
        }

        private static void EnsureId(ref Dictionary<int, ProductMetrics> dict, int id)
        {
            if (!dict.ContainsKey(id))
            {
                ItemProto itemProto = LDB.items.Select(id);

                dict.Add(id, new ProductMetrics()
                {
                    itemProto = itemProto
                });
            }
        }



        private static string FormatMetric(float value)
        {
            if (value >= 1000000.0)
                return (value / 1000000).ToString("F2") + " M";
            else if (value >= 10000.0)
                return (value / 1000).ToString("F2") + " k";
            else if (value > 1000.0)
                return value.ToString("F0");
            else if (value > 0.0)
                return value.ToString("F1");
            else
                return value.ToString();

        }

        private static string FormatMetricPerSec(float value)
        {
            value = value / 60;

            if (value >= 1000000.0)
                return (value / 1000000).ToString("F2") + " M";
            else if (value >= 10000.0)
                return (value / 1000).ToString("F2") + " k";
            else if (value > 1000.0)
                return value.ToString("F0");
            else if (value > 0.0)
                return value.ToString("F1");
            else
                return value.ToString();

        }

        private static float ReverseFormat(string value)
        {
            string[] parts = value.Split(' ');
            float multiplier=1;

            if (parts.Length > 1)
                multiplier = parts[1] == "k" ? 1000 : (parts[1] == "M" ? 1000000 : (parts[1] == "G" ? 1000000000 : 1));

            return float.Parse(parts[0]) * multiplier;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductEntry), "UpdateProduct")]
        public static void UIProductEntry_UpdateProduct_Postfix(UIProductEntry __instance)
        {
            int statTimeLevel = __instance.productionStatWindow.statTimeLevel;
            if (statTimeLevel != 5)
            {
                __instance.productLabel.text += " / Theoretical max";
                __instance.productLabel.resizeTextForBestFit = true;
                __instance.productLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 24);

                __instance.consumeLabel.text += " / Theoretical max";
                __instance.consumeLabel.resizeTextForBestFit = true;
                __instance.consumeLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 24);

                __instance.productText.resizeTextForBestFit = true;
                __instance.consumeText.resizeTextForBestFit = true;

                __instance.productUnitLabel.text += "\r\n/s";
                __instance.productUnitLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(36, 56);
                // Not sure about scalability
                __instance.productUnitLabel.GetComponent<RectTransform>().localPosition = new Vector3(116, 0, 0);
                __instance.productUnitLabel.lineSpacing = 1.4F;

                __instance.consumeUnitLabel.text += "\r\n/s";
                __instance.consumeUnitLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(36, 56);
                __instance.consumeUnitLabel.GetComponent<RectTransform>().localPosition = new Vector3(297, 0, 0);
                __instance.consumeUnitLabel.lineSpacing = 1.4F;


                string produce = "0";
                string produceSec = "0";
                string consume = "0";
                string consumeSec = "0";
                if (counter.ContainsKey(__instance.itemId))
                {
                    var productMetrics = counter[__instance.itemId];
                    produce = FormatMetric(productMetrics.production);
                    produceSec = FormatMetricPerSec(productMetrics.production);
                    consume = FormatMetric(productMetrics.consumption);
                    consumeSec = FormatMetricPerSec(productMetrics.consumption);
                }

                string originalProductText = __instance.productText.text.Trim();
                string originalConsumeText = __instance.consumeText.text.Trim();


                __instance.productText.text = $"{originalProductText} / {produce}";
                __instance.consumeText.text = $"{originalConsumeText} / {consume}";

                //add values per second
                __instance.productText.text += $"\r\n{FormatMetricPerSec(ReverseFormat(originalProductText))} / {produceSec}";
                __instance.consumeText.text += $"\r\n{FormatMetricPerSec(ReverseFormat(originalConsumeText))} / {consumeSec}";
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductionStatWindow), "ComputeDisplayEntries")]
        public static void UIProductionStatWindow_ComputeDisplayEntries_Postfix(UIProductionStatWindow __instance)
        {
            counter.Clear();


            if (__instance.targetIndex == -1)
            {
                int factoryCount = __instance.gameData.factoryCount;
                for (int i = 0; i < factoryCount; i++)
                {
                    AddPlanetFactoryData(__instance.gameData.factories[i]);
                }
            }
            else if (__instance.targetIndex == 0)
            {
                AddPlanetFactoryData(__instance.gameData.localPlanet.factory);
            }
            else if (__instance.targetIndex % 100 > 0)
            {
                PlanetData planetData = __instance.gameData.galaxy.PlanetById(__instance.targetIndex);
                AddPlanetFactoryData(planetData.factory);
            }
            else if (__instance.targetIndex % 100 == 0)
            {
                int starId = __instance.targetIndex / 100;
                StarData starData = __instance.gameData.galaxy.StarById(starId);
                for (int j = 0; j < starData.planetCount; j++)
                {
                    if (starData.planets[j].factory != null)
                    {
                        AddPlanetFactoryData(starData.planets[j].factory);
                    }
                }
            }

        }

        public static void AddPlanetFactoryData(PlanetFactory planetFactory)
        {
            var factorySystem = planetFactory.factorySystem;

            var veinPool = planetFactory.planet.factory.veinPool;
            for (int i = 1; i < factorySystem.minerCursor; i++)
            {
                var miner = factorySystem.minerPool[i];
                if (i != miner.id) continue;

                var productId = miner.productId;
                var veinId = (miner.veinCount != 0) ? miner.veins[miner.currentVeinIndex] : 0;

                if (miner.type == EMinerType.Water)
                {
                    productId = planetFactory.planet.waterItemId;
                }
                else if (productId == 0)
                {
                    productId = veinPool[veinId].productId;
                }

                if (productId == 0) continue;


                EnsureId(ref counter, productId);

                float frequency = 60f / (float)((double)miner.period / 600000.0);
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

                var frequency = 60f / (float)((double)assembler.timeSpend / 600000.0);
                var speed = (float)(0.0001 * (double)assembler.speed);

                for (int k = 0; k < assembler.requires.Length; k++)
                {
                    var productId = assembler.requires[k];
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += frequency * speed * assembler.requireCounts[k];
                    counter[productId].consumers++;
                }

                for (int k = 0; k < assembler.products.Length; k++)
                {
                    var productId = assembler.products[k];
                    EnsureId(ref counter, productId);

                    counter[productId].production += frequency * speed * assembler.productCounts[k];
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

                    counter[productId].consumption += 60f * 30f * fractionator.produceProb;
                    counter[productId].consumers++;
                }
                if (fractionator.product != 0)
                {
                    var productId = fractionator.product;
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += 60f * 30f * fractionator.produceProb;
                    counter[productId].consumers++;
                }

            }
            for (int l = 1; l < factorySystem.ejectorCursor; l++)
            {
                var ejector = factorySystem.ejectorPool[l];
                if (ejector.id != l) continue;

                EnsureId(ref counter, ejector.bulletId);

                counter[ejector.bulletId].consumption += 60f / (float)(ejector.chargeSpend + ejector.coldSpend) * 600000f;
                counter[ejector.bulletId].consumers++;
            }
            for (int m = 1; m < factorySystem.siloCursor; m++)
            {
                var silo = factorySystem.siloPool[m];
                if (silo.id != m) continue;

                EnsureId(ref counter, silo.bulletId);

                counter[silo.bulletId].consumption += 60f / (float)(silo.chargeSpend + silo.coldSpend) * 600000f;
                counter[silo.bulletId].consumers++;
            }
            for (int n = 1; n < factorySystem.labCursor; n++)
            {
                var lab = factorySystem.labPool[n];
                if (lab.id != n || !lab.matrixMode) continue;
                float frequency = 60f / (float)((double)lab.timeSpend / 600000.0);

                for (int k = 0; k < lab.requires.Length; k++)
                {
                    var productId = lab.requires[k];
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += frequency * lab.requireCounts[k];
                    counter[productId].consumers++;
                }

                for (int k = 0; k < lab.products.Length; k++)
                {
                    var productId = lab.products[k];
                    EnsureId(ref counter, productId);

                    counter[productId].production += frequency * lab.productCounts[k];
                    counter[productId].producers++;
                }

            }
        }
    }
}
