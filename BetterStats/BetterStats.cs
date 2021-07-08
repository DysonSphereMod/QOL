using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;

namespace BetterStats
{
    // TODO: button to next producer/consumer
    [BepInPlugin("com.brokenmass.plugin.DSP.BetterStats", "BetterStats", "1.2.0")]
    public class BetterStats : BaseUnityPlugin
    {
        public class EnhancedUIProductEntryElements
        {
            public Text maxProductionLabel;
            public Text maxProductionValue;
            public Text maxProductionUnit;

            public Text maxConsumptionLabel;
            public Text maxConsumptionValue;
            public Text maxConsumptionUnit;

            public Text counterProductionLabel;
            public Text counterProductionValue;

            public Text counterConsumptionLabel;
            public Text counterConsumptionValue;
        }
        Harmony harmony;
        private static Dictionary<int, ProductMetrics> counter = new Dictionary<int, ProductMetrics>();
        private static bool displaySec = true;
        private static GameObject txtGO, chxGO, filterGO;
        private static Texture2D texOff = Resources.Load<Texture2D>("ui/textures/sprites/icons/checkbox-off");
        private static Texture2D texOn = Resources.Load<Texture2D>("ui/textures/sprites/icons/checkbox-on");
        private static Sprite sprOn;
        private static Sprite sprOff;
        private static Image checkBoxImage;

        private static string filterStr = "";

        private const int initialXOffset = 70;
        private const int valuesWidth = 90;
        private const int unitsWidth = 20;
        private const int labelsWidth = valuesWidth + unitsWidth;
        private const int margin = 10;
        private const int maxOffset = labelsWidth + margin;

        private static int lastStatTimer;

        private static Dictionary<UIProductEntry, EnhancedUIProductEntryElements> enhancements = new Dictionary<UIProductEntry, EnhancedUIProductEntryElements>();
        private static UIProductionStatWindow statWindow;

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
            if (txtGO != null)
            {
                Destroy(txtGO);
                Destroy(chxGO);
                Destroy(filterGO);
                Destroy(sprOn);
                Destroy(sprOff);
            }
            var favoritesLabel = GameObject.Find("UI Root/Overlay Canvas/In Game/Windows/Production Stat Window/product-bg/top/favorite-text");
            if (favoritesLabel != null)
            {
                favoritesLabel.SetActive(true);
            }

            ClearEnhancedUIProductEntries();

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

        private static void ClearEnhancedUIProductEntries()
        {
            if (statWindow == null) return;

            //wipe all productentry as we have heavily modified the layout
            foreach (var entry in statWindow.entryPool)
            {
                entry.Destroy();
            }


            enhancements.Clear();
            statWindow.entryPool.Clear();
        }

        private static Text CopyText(Text original, Vector2 positionDelta)
        {
            var copied = Instantiate(original);
            copied.transform.SetParent(original.transform.parent, false);
            var copiedRectTransform = copied.GetComponent<RectTransform>();
            var originalRectTransform = original.GetComponent<RectTransform>();

            copiedRectTransform.anchorMin = originalRectTransform.anchorMin;
            copiedRectTransform.anchorMax = originalRectTransform.anchorMax;
            copiedRectTransform.sizeDelta = originalRectTransform.sizeDelta;
            copiedRectTransform.anchoredPosition = originalRectTransform.anchoredPosition + positionDelta;

            return copied;
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

        private static float ReverseFormat(string value)
        {
            string[] parts = value.Split(' ');
            float multiplier = 1;

            if (parts.Length > 1)
                multiplier = parts[1] == "k" ? 1000 : (parts[1] == "M" ? 1000000 : (parts[1] == "G" ? 1000000000 : 1));

            try{
                return float.Parse(parts[0], CultureInfo.InvariantCulture) * multiplier;
            }catch(FormatException ex){
                throw new ArgumentException("Invalid format String : " + value, nameof(value), ex);
            }
        }

        private static EnhancedUIProductEntryElements EnhanceUIProductEntry(UIProductEntry __instance)
        {
            __instance.itemIcon.transform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 80);
            __instance.itemIcon.transform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(22, 12);

            __instance.favoriteBtn1.GetComponent<RectTransform>().anchoredPosition = new Vector2(26, -32);
            __instance.favoriteBtn2.GetComponent<RectTransform>().anchoredPosition = new Vector2(49, -32);
            __instance.favoriteBtn3.GetComponent<RectTransform>().anchoredPosition = new Vector2(72, -32);
            __instance.itemName.transform.SetParent(__instance.itemIcon.transform.parent, false);
            var itemNameRect = __instance.itemName.GetComponent<RectTransform>();

            itemNameRect.pivot = new Vector2(0.5f, 0f);
            itemNameRect.anchorMin = new Vector2(0, 0);
            itemNameRect.anchorMax = new Vector2(1f, 0);

            itemNameRect.anchoredPosition = new Vector2(0, 0);
            __instance.itemIcon.transform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 80);

            __instance.itemName.resizeTextForBestFit = true;
            __instance.itemName.resizeTextMaxSize = 14;
            __instance.itemName.alignment = TextAnchor.MiddleCenter;
            __instance.itemName.alignByGeometry = true;
            __instance.itemName.horizontalOverflow = HorizontalWrapMode.Wrap;
            __instance.itemName.lineSpacing = 0.6f;

            var sepLine = __instance.consumeUnitLabel.transform.parent.Find("sep-line");
            sepLine.GetComponent<RectTransform>().pivot = new Vector2(0f, 1f);
            sepLine.GetComponent<RectTransform>().rotation = Quaternion.Euler(0f, 0f, 90f);
            sepLine.GetComponent<RectTransform>().sizeDelta = new Vector2(1, 336);
            sepLine.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -50);


            __instance.productLabel.alignment = TextAnchor.UpperRight;
            __instance.productLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(labelsWidth, 24);
            __instance.productLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(initialXOffset, 0);
            __instance.productLabel.GetComponent<RectTransform>().ForceUpdateRectTransforms();

            __instance.productText.alignByGeometry = true;
            __instance.productText.resizeTextForBestFit = true;
            __instance.productText.resizeTextMaxSize = 34;
            __instance.productText.alignment = TextAnchor.LowerRight;
            __instance.productText.GetComponent<RectTransform>().sizeDelta = new Vector2(valuesWidth, 40);
            __instance.productText.GetComponent<RectTransform>().anchoredPosition = new Vector2(initialXOffset, 56);

            __instance.productUnitLabel.alignByGeometry = true;
            __instance.productUnitLabel.alignment = TextAnchor.LowerLeft;
            __instance.productUnitLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(unitsWidth, 24);
            __instance.productUnitLabel.GetComponent<RectTransform>().pivot = new Vector2(0f, 0f);
            __instance.productUnitLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(initialXOffset + valuesWidth + 4, -42);

            __instance.consumeLabel.alignment = TextAnchor.UpperRight;
            __instance.consumeLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(labelsWidth, 24);
            __instance.consumeLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(initialXOffset, -60);

            __instance.consumeText.alignByGeometry = true;
            __instance.consumeText.resizeTextForBestFit = true;
            __instance.consumeText.resizeTextMaxSize = 34;
            __instance.consumeText.alignment = TextAnchor.LowerRight;
            __instance.consumeText.GetComponent<RectTransform>().sizeDelta = new Vector2(valuesWidth, 40);
            __instance.consumeText.GetComponent<RectTransform>().anchoredPosition = new Vector2(initialXOffset, -4);

            __instance.consumeUnitLabel.alignByGeometry = true;
            __instance.consumeUnitLabel.alignment = TextAnchor.LowerLeft;
            __instance.consumeUnitLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(unitsWidth, 24);
            __instance.consumeUnitLabel.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);
            __instance.consumeUnitLabel.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 0f);
            __instance.consumeUnitLabel.GetComponent<RectTransform>().pivot = new Vector2(0f, 0f);
            __instance.consumeUnitLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(initialXOffset + valuesWidth + 4, -4);

            var maxProductionLabel = CopyText(__instance.productLabel, new Vector2(maxOffset, 0));
            maxProductionLabel.text = "Theoretical Max";
            var maxProductionValue = CopyText(__instance.productText, new Vector2(maxOffset, 0));
            maxProductionValue.text = "0";
            var maxProductionUnit = CopyText(__instance.productUnitLabel, new Vector2(maxOffset, 0));
            maxProductionUnit.text = "/min";

            var maxConsumptionLabel = CopyText(__instance.consumeLabel, new Vector2(maxOffset, 0));
            maxConsumptionLabel.text = "Theoretical Max";
            var maxConsumptionValue = CopyText(__instance.consumeText, new Vector2(maxOffset, 0));
            maxConsumptionValue.text = "0";
            var maxConsumptionUnit = CopyText(__instance.consumeUnitLabel, new Vector2(maxOffset, 0));
            maxConsumptionUnit.text = "/min";

            var counterProductionLabel = CopyText(__instance.productLabel, new Vector2(-initialXOffset, 0));
            counterProductionLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 40);
            counterProductionLabel.text = "Producers";
            var counterProductionValue = CopyText(__instance.productText, new Vector2(-initialXOffset, 0));
            counterProductionValue.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 40);
            counterProductionValue.text = "0";

            var counterConsumptionLabel = CopyText(__instance.consumeLabel, new Vector2(-initialXOffset, 0));
            counterConsumptionLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 40);
            counterConsumptionLabel.text = "Consumers";
            var counterConsumptionValue = CopyText(__instance.consumeText, new Vector2(-initialXOffset, 0));
            counterConsumptionValue.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 40);
            counterConsumptionValue.text = "0";

            var enhancement = new EnhancedUIProductEntryElements()
            {
                maxProductionLabel = maxProductionLabel,
                maxProductionValue = maxProductionValue,
                maxProductionUnit = maxProductionUnit,

                maxConsumptionLabel = maxConsumptionLabel,
                maxConsumptionValue = maxConsumptionValue,
                maxConsumptionUnit = maxConsumptionUnit,

                counterProductionLabel = counterProductionLabel,
                counterProductionValue = counterProductionValue,

                counterConsumptionLabel = counterConsumptionLabel,
                counterConsumptionValue = counterConsumptionValue
            };

            enhancements.Add(__instance, enhancement);

            return enhancement;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductionStatWindow), "_OnOpen")]
        public static void UIProductionStatWindow__OnOpen_Postfix(UIProductionStatWindow __instance)
        {
            if (statWindow == null)
            {
                statWindow = __instance;
            }

            if (chxGO != null) return;

            var favoritesLabel = GameObject.Find("UI Root/Overlay Canvas/In Game/Windows/Production Stat Window/product-bg/top/favorite-text");
            if (favoritesLabel != null)
            {
                favoritesLabel.SetActive(false);
            }

            sprOn = Sprite.Create(texOn, new Rect(0, 0, texOn.width, texOn.height), new Vector2(0.5f, 0.5f));
            sprOff = Sprite.Create(texOff, new Rect(0, 0, texOff.width, texOff.height), new Vector2(0.5f, 0.5f));

            chxGO = new GameObject("displaySec");

            RectTransform rect = chxGO.AddComponent<RectTransform>();
            rect.SetParent(__instance.productRankBox.transform.parent, false);

            rect.anchorMax = new Vector2(0, 1);
            rect.anchorMin = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(20, 20);
            rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = new Vector2(250, -33);

            Button _btn = rect.gameObject.AddComponent<Button>();
            _btn.onClick.AddListener(() =>
            {
                displaySec = !displaySec;
                checkBoxImage.sprite = displaySec ? sprOn : sprOff;
            });

            checkBoxImage = _btn.gameObject.AddComponent<Image>();
            checkBoxImage.color = new Color(0.8f, 0.8f, 0.8f, 1);

            checkBoxImage.sprite = displaySec ? sprOn : sprOff;


            txtGO = new GameObject("displaySecTxt");
            RectTransform rectTxt = txtGO.AddComponent<RectTransform>();

            rectTxt.SetParent(chxGO.transform, false);

            rectTxt.anchorMax = new Vector2(0, 0.5f);
            rectTxt.anchorMin = new Vector2(0, 0.5f);
            rectTxt.sizeDelta = new Vector2(100, 20);
            rectTxt.pivot = new Vector2(0, 0.5f);
            rectTxt.anchoredPosition = new Vector2(20, 0);

            Text text = rectTxt.gameObject.AddComponent<Text>();
            text.text = "Display /sec";
            text.fontStyle = FontStyle.Normal;
            text.fontSize = 14;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.color = new Color(0.8f, 0.8f, 0.8f, 1);
            Font fnt = Resources.Load<Font>("ui/fonts/SAIRASB");
            if (fnt != null)
                text.font = fnt;

            filterGO = new GameObject("filterGo");
            RectTransform rectFilter = filterGO.AddComponent<RectTransform>();

            rectFilter.SetParent(__instance.productRankBox.transform.parent, false);

            rectFilter.anchorMax = new Vector2(0, 1);
            rectFilter.anchorMin = new Vector2(0, 1);
            rectFilter.sizeDelta = new Vector2(100, 30);
            rectFilter.pivot = new Vector2(0, 0.5f);
            rectFilter.anchoredPosition = new Vector2(120, -33);

            var _image = filterGO.AddComponent<Image>();
            _image.transform.SetParent(rectFilter, false);
            _image.color = new Color(0f, 0f, 0f, 0.5f);

            GameObject textContainer = new GameObject();
            textContainer.name = "Text";
            textContainer.transform.SetParent(rectFilter, false);
            var _text = textContainer.AddComponent<Text>();
            _text.supportRichText = false;
            _text.color = new Color(0.8f, 0.8f, 0.8f, 1);
            _text.font = fnt;
            _text.fontSize = 16;
            _text.alignment = TextAnchor.MiddleLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            (_text.transform as RectTransform).sizeDelta = new Vector2(90, 30);
            (_text.transform as RectTransform).anchoredPosition = new Vector2(5, 0);

            GameObject placeholderContainer = new GameObject();
            placeholderContainer.name = "Placeholder";
            placeholderContainer.transform.SetParent(rectFilter, false);
            var _placeholder = placeholderContainer.AddComponent<Text>();
            _placeholder.color = new Color(0.8f, 0.8f, 0.8f, 1);
            _placeholder.font = fnt;
            _placeholder.fontSize = 16;
            _placeholder.fontStyle = FontStyle.Italic;
            _placeholder.alignment = TextAnchor.MiddleLeft;
            _placeholder.supportRichText = false;
            _placeholder.horizontalOverflow = HorizontalWrapMode.Overflow;
            _placeholder.text = "Filter";
            (_placeholder.transform as RectTransform).sizeDelta = new Vector2(90, 30);
            (_placeholder.transform as RectTransform).anchoredPosition = new Vector2(5, 0);

            var _inputField = filterGO.AddComponent<InputField>();
            _inputField.transform.SetParent(rectFilter, false);
            _inputField.targetGraphic = _image;
            _inputField.textComponent = _text;
            _inputField.placeholder = _placeholder;


            _inputField.onValueChanged.AddListener((string value) =>
            {
                filterStr = value;
                __instance.ComputeDisplayEntries();
            });

            chxGO.transform.SetParent(__instance.productRankBox.transform.parent, false);
            txtGO.transform.SetParent(chxGO.transform, false);
            filterGO.transform.SetParent(__instance.productRankBox.transform.parent, false);

        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductionStatWindow), "AddToDisplayEntries")]
        public static void UIProductionStatWindow_AddToDisplayEntries_Prefix(UIProductionStatWindow __instance)
        {
            if (filterStr == "") return;

            __instance.displayEntries.RemoveAll((data) =>
            {
                var proto = LDB.items.Select(data[0]);

                if (proto.name.IndexOf(filterStr, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
                return true;
            });

        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIProductionStatWindow), "_OnUpdate")]
        public static void UIProductionStatWindow__OnUpdate_Prefix(UIProductionStatWindow __instance)
        {
            if (statWindow == null)
            {
                statWindow = __instance;
            }
            if (lastStatTimer != __instance.statTimeLevel && (__instance.statTimeLevel == 5 || lastStatTimer == 5))
            {
                ClearEnhancedUIProductEntries();
            }

            lastStatTimer = __instance.statTimeLevel;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductEntry), "UpdateProduct")]
        public static void UIProductEntry_UpdateProduct_Postfix(UIProductEntry __instance)
        {
            if (__instance.productionStatWindow.statTimeLevel == 5) return;

            if (!enhancements.TryGetValue(__instance, out EnhancedUIProductEntryElements enhancement))
            {
                enhancement = EnhanceUIProductEntry(__instance);

            }


            string originalProductText = __instance.productText.text.Trim();
            string originalConsumeText = __instance.consumeText.text.Trim();

            string producers = "0";
            string consumers = "0";
            string maxProduction = "0";
            string maxConsumption = "0";
            string unit = "/min";
            int divider = 1;

            //add values per second
            if (displaySec)
            {
                divider = 60;
                unit = "/sec";


                originalProductText = $"{FormatMetric(ReverseFormat(originalProductText) / divider)}";
                originalConsumeText = $"{FormatMetric(ReverseFormat(originalConsumeText) / divider)}";
            }

            __instance.productUnitLabel.text =
                __instance.consumeUnitLabel.text =
                enhancement.maxProductionUnit.text =
                enhancement.maxConsumptionUnit.text = unit;

            if (counter.ContainsKey(__instance.itemId))
            {
                var productMetrics = counter[__instance.itemId];
                maxProduction = FormatMetric(productMetrics.production / divider);
                maxConsumption = FormatMetric(productMetrics.consumption / divider);

                producers = productMetrics.producers.ToString();
                consumers = productMetrics.consumers.ToString();
            }

            __instance.productText.text = $"{originalProductText}";
            __instance.consumeText.text = $"{originalConsumeText}";

            enhancement.maxProductionValue.text = maxProduction;
            enhancement.maxConsumptionValue.text = maxConsumption;

            enhancement.counterProductionValue.text = producers;
            enhancement.counterConsumptionValue.text = consumers;

            enhancement.maxProductionValue.color = enhancement.counterProductionValue.color = __instance.productText.color;
            enhancement.maxConsumptionValue.color = enhancement.counterConsumptionValue.color = __instance.consumeText.color;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIProductionStatWindow), "ComputeDisplayEntries")]
        public static void UIProductionStatWindow_ComputeDisplayEntries_Prefix(UIProductionStatWindow __instance)
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

        // speed of fasted belt(mk3 belt) is 1800 items per minute
        public const float BELT_MAX_ITEMS_PER_MINUTE = 1800;
        public const float TICKS_PER_SEC = 60.0f;

        public static void AddPlanetFactoryData(PlanetFactory planetFactory)
        {
            var factorySystem = planetFactory.factorySystem;
            var transport = planetFactory.transport;
            var veinPool = planetFactory.planet.factory.veinPool;
            var miningSpeedScale = (double)GameMain.history.miningSpeedScale;

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
                float speed = (float)(0.0001 * (double)miner.speed * miningSpeedScale);

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
                production = Math.Min(BELT_MAX_ITEMS_PER_MINUTE, production);

                counter[productId].production += production;
                counter[productId].producers++;

            }
            for (int i = 1; i < factorySystem.assemblerCursor; i++)
            {
                var assembler = factorySystem.assemblerPool[i];
                if (assembler.id != i || assembler.recipeId == 0) continue;

                var frequency = 60f / (float)((double)assembler.timeSpend / 600000.0);
                var speed = (float)(0.0001 * (double)assembler.speed);

                for (int j = 0; j < assembler.requires.Length; j++)
                {
                    var productId = assembler.requires[j];
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += frequency * speed * assembler.requireCounts[j];
                    counter[productId].consumers++;
                }

                for (int j = 0; j < assembler.products.Length; j++)
                {
                    var productId = assembler.products[j];
                    EnsureId(ref counter, productId);

                    counter[productId].production += frequency * speed * assembler.productCounts[j];
                    counter[productId].producers++;
                }
            }
            for (int i = 1; i < factorySystem.fractionateCursor; i++)
            {
                var fractionator = factorySystem.fractionatePool[i];
                if (fractionator.id != i) continue;

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

                    counter[productId].production += 60f * 30f * fractionator.produceProb;
                    counter[productId].producers++;
                }

            }
            for (int i = 1; i < factorySystem.ejectorCursor; i++)
            {
                var ejector = factorySystem.ejectorPool[i];
                if (ejector.id != i) continue;

                EnsureId(ref counter, ejector.bulletId);

                counter[ejector.bulletId].consumption += 60f / (float)(ejector.chargeSpend + ejector.coldSpend) * 600000f;
                counter[ejector.bulletId].consumers++;
            }
            for (int i = 1; i < factorySystem.siloCursor; i++)
            {
                var silo = factorySystem.siloPool[i];
                if (silo.id != i) continue;

                EnsureId(ref counter, silo.bulletId);

                counter[silo.bulletId].consumption += 60f / (float)(silo.chargeSpend + silo.coldSpend) * 600000f;
                counter[silo.bulletId].consumers++;
            }
            for (int i = 1; i < factorySystem.labCursor; i++)
            {
                var lab = factorySystem.labPool[i];
                if (lab.id != i || !lab.matrixMode) continue;
                float frequency = 60f / (float)((double)lab.timeSpend / 600000.0);

                for (int j = 0; j < lab.requires.Length; j++)
                {
                    var productId = lab.requires[j];
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += frequency * lab.requireCounts[j];
                    counter[productId].consumers++;
                }

                for (int j = 0; j < lab.products.Length; j++)
                {
                    var productId = lab.products[j];
                    EnsureId(ref counter, productId);

                    counter[productId].production += frequency * lab.productCounts[j];
                    counter[productId].producers++;
                }

            }
            for (int i = 1; i < transport.stationCursor; i++)
            {
                var station = transport.stationPool[i];
                if (station == null || station.id != i || !station.isCollector) continue;

                double collectorsWorkCost = transport.collectorsWorkCost;
                double gasTotalHeat = planetFactory.planet.gasTotalHeat;
                float collectSpeedRate = (gasTotalHeat - collectorsWorkCost > 0.0) ? ((float)((miningSpeedScale * gasTotalHeat - collectorsWorkCost) / (gasTotalHeat - collectorsWorkCost))) : 1f;

                for (int j = 0; j < station.collectionIds.Length; j++)
                {
                    var productId = station.collectionIds[j];
                    EnsureId(ref counter, productId);

                    counter[productId].production += 60f * TICKS_PER_SEC * station.collectionPerTick[j] * collectSpeedRate;
                    counter[productId].producers++;
                }
            }
            for (int i = 1; i < planetFactory.powerSystem.genCursor; i++)
            {
                var generator = planetFactory.powerSystem.genPool[i];
                if (generator.id != i)
                {
                    continue;
                }
                if (generator.productId == 0 || generator.productHeat == 0)
                {
                    continue;
                }
                var productId = generator.productId;
                EnsureId(ref counter, productId);

                counter[productId].production += 60.0f * TICKS_PER_SEC * generator.capacityCurrentTick / generator.productHeat;
                counter[productId].producers++;
            }
        }
    }
}
