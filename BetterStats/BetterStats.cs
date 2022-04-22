using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;

namespace BetterStats
{
    // TODO: button to next producer/consumer
    [BepInPlugin("com.brokenmass.plugin.DSP.BetterStats", "BetterStats", "1.3.2")]
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
        private static ConfigEntry<float> lackOfProductionRatioTrigger;
        private static ConfigEntry<float> consumptionToProductionRatioTrigger;
        private static ConfigEntry<bool> displayPerSecond;
        private static Dictionary<int, ProductMetrics> counter = new Dictionary<int, ProductMetrics>();
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
        private static UIStatisticsWindow statWindow;

        internal void Awake()
        {
            InitConfig();
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

        internal void InitConfig()
        {
            lackOfProductionRatioTrigger = Config.Bind("General", "lackOfProductionRatio", 0.9f, //
                    "When consumption rises above the given ratio of max production, flag the text in red." +//
                    " (e.g. if set to '0.9' then you will be warned if you consume more than 90% of your max production)");
            consumptionToProductionRatioTrigger = Config.Bind("General", "consumptionToProductionRatio", 1.5f, //
                    "If max consumption raises above the given max production ratio, flag the text in red." +//
                    " (e.g. if set to '1.5' then you will be warned if your max consumption is more than 150% of your max production)");
            displayPerSecond = Config.Bind("General", "displayPerSecond", false,
                    "Used by UI to persist the last selected value for checkbox");
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

            foreach (EnhancedUIProductEntryElements enhancement in enhancements.Values)
            {
                Destroy(enhancement.maxProductionLabel.gameObject);
                Destroy(enhancement.maxProductionValue.gameObject);
                Destroy(enhancement.maxProductionUnit.gameObject);

                Destroy(enhancement.maxConsumptionLabel.gameObject);
                Destroy(enhancement.maxConsumptionValue.gameObject);
                Destroy(enhancement.maxConsumptionUnit.gameObject);

                Destroy(enhancement.counterProductionLabel.gameObject);
                Destroy(enhancement.counterProductionValue.gameObject);

                Destroy(enhancement.counterConsumptionLabel.gameObject);
                Destroy(enhancement.counterConsumptionValue.gameObject);
            }
            enhancements.Clear();
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
            float multiplier;
            string numericValue;

            if (parts.Length > 1)
            {
                multiplier = parts[1] == "k" ? 1000 : (parts[1] == "M" ? 1000000 : (parts[1] == "G" ? 1000000000 : 1));
                numericValue = parts[0];
            }
            else
            {
                multiplier = 1;
                numericValue = parts[0].Replace(",", ".");
            }

            try
            {
                return float.Parse(numericValue, CultureInfo.InvariantCulture) * multiplier;
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("Invalid format String : '" + value + "' (parsed as " + numericValue + " * " + multiplier + ")", nameof(value), ex);
            }
        }

        private static EnhancedUIProductEntryElements EnhanceUIProductEntry(UIProductEntry __instance)
        {
            var parent = __instance.itemIcon.transform.parent;
            parent.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 80);
            parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(22, 12);

            __instance.favoriteBtn1.GetComponent<RectTransform>().anchoredPosition = new Vector2(26, -32);
            __instance.favoriteBtn2.GetComponent<RectTransform>().anchoredPosition = new Vector2(49, -32);
            __instance.favoriteBtn3.GetComponent<RectTransform>().anchoredPosition = new Vector2(72, -32);
            __instance.itemName.transform.SetParent(parent, false);
            var itemNameRect = __instance.itemName.GetComponent<RectTransform>();

            itemNameRect.pivot = new Vector2(0.5f, 0f);
            itemNameRect.anchorMin = new Vector2(0, 0);
            itemNameRect.anchorMax = new Vector2(1f, 0);

            itemNameRect.anchoredPosition = new Vector2(0, 0);
            parent.GetComponent<RectTransform>().sizeDelta = new Vector2(80, 80);

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
            maxProductionLabel.text = "Theoretical max";
            var maxProductionValue = CopyText(__instance.productText, new Vector2(maxOffset, 0));
            maxProductionValue.text = "0";
            var maxProductionUnit = CopyText(__instance.productUnitLabel, new Vector2(maxOffset, 0));
            maxProductionUnit.text = "/min";

            var maxConsumptionLabel = CopyText(__instance.consumeLabel, new Vector2(maxOffset, 0));
            maxConsumptionLabel.text = "Theoretical max";
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

            var enhancement = new EnhancedUIProductEntryElements
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
                counterConsumptionValue = counterConsumptionValue,
            };

            enhancements.Add(__instance, enhancement);

            return enhancement;
        }
        [HarmonyPostfix, HarmonyPatch(typeof(UIStatisticsWindow), "_OnOpen")]
        public static void UIStatisticsWindow__OnOpen_Postfix(UIStatisticsWindow __instance)
        {
            if (statWindow == null)
            {
                statWindow = __instance;
            }

            if (chxGO != null) return;

            var favoritesLabel = GameObject.Find("UI Root/Overlay Canvas/In Game/Windows/Statistics Window/product-bg/top/favorite-text");
            if (favoritesLabel != null)
            {
                favoritesLabel.SetActive(false);
            }

            sprOn = Sprite.Create(texOn, new Rect(0, 0, texOn.width, texOn.height), new Vector2(0.5f, 0.5f));
            sprOff = Sprite.Create(texOff, new Rect(0, 0, texOff.width, texOff.height), new Vector2(0.5f, 0.5f));

            chxGO = new GameObject("displaySec");

            RectTransform rect = chxGO.AddComponent<RectTransform>();
            rect.SetParent(__instance.productSortBox.transform.parent, false);

            rect.anchorMax = new Vector2(0, 1);
            rect.anchorMin = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(20, 20);
            rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = new Vector2(250, -33);

            Button _btn = rect.gameObject.AddComponent<Button>();
            _btn.onClick.AddListener(() =>
            {
                displayPerSecond.Value = !displayPerSecond.Value;
                checkBoxImage.sprite = displayPerSecond.Value ? sprOn : sprOff;
            });

            checkBoxImage = _btn.gameObject.AddComponent<Image>();
            checkBoxImage.color = new Color(0.8f, 0.8f, 0.8f, 1);

            checkBoxImage.sprite = displayPerSecond.Value ? sprOn : sprOff;


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

            rectFilter.SetParent(__instance.productSortBox.transform.parent, false);

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

            chxGO.transform.SetParent(__instance.productSortBox.transform.parent, false);
            txtGO.transform.SetParent(chxGO.transform, false);
            filterGO.transform.SetParent(__instance.productSortBox.transform.parent, false);

        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductEntryList), "FilterEntries")]
        public static void UIProductEntryList_FilterEntries_Postfix(UIProductEntryList __instance)
        {
            if (filterStr == "") return;
            var uiProductEntryList = __instance;
            for (int pIndex = uiProductEntryList.entryDatasCursor - 1; pIndex >= 0; --pIndex)
            {
                UIProductEntryData entryData = uiProductEntryList.entryDatas[pIndex];
                var proto = LDB.items.Select(entryData.itemId);
                if (proto.name.IndexOf(filterStr, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    uiProductEntryList.Swap(pIndex, uiProductEntryList.entryDatasCursor - 1);
                    --uiProductEntryList.entryDatasCursor;
                }
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIStatisticsWindow), "_OnUpdate")]
        public static void UIStatisticsWindow__OnUpdate_Prefix(UIStatisticsWindow __instance)
        {
            if (statWindow == null)
            {
                statWindow = __instance;
            }
            lastStatTimer = __instance.timeLevel;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIProductEntry), "_OnUpdate")]
        public static void UIProductEntry__OnUpdate_Postfix(UIProductEntry __instance)
        {
            if (__instance.productionStatWindow == null || !__instance.productionStatWindow.isProductionTab) return;

            if (!enhancements.TryGetValue(__instance, out EnhancedUIProductEntryElements enhancement))
            {
                enhancement = EnhanceUIProductEntry(__instance);
            }

            bool isTotalTimeWindow = __instance.productionStatWindow.timeLevel == 5;

            string originalProductText = __instance.productText.text.Trim();
            string originalConsumeText = __instance.consumeText.text.Trim();


            float originalProductValue = ReverseFormat(originalProductText);
            float originalConsumeValue = ReverseFormat(originalConsumeText);

            string producers = "0";
            string consumers = "0";
            string maxProduction = "0";
            string maxConsumption = "0";
            string unitRate = displayPerSecond.Value ? "/sec" : "/min";
            string unit = isTotalTimeWindow ? "" : "/min";
            int divider = 1;
            bool alertOnLackOfProduction = false;
            bool warnOnHighMaxConsumption = false;

            //add values per second
            if (displayPerSecond.Value)
            {
                divider = 60;
                unit = !isTotalTimeWindow ? "/sec" : unit;

                if (!isTotalTimeWindow)
                {
                    originalProductValue = originalProductValue / divider;
                    originalConsumeValue = originalConsumeValue / divider;


                    originalProductText = $"{FormatMetric(originalProductValue)}";
                    originalConsumeText = $"{FormatMetric(originalConsumeValue)}";
                }
            }

            __instance.productUnitLabel.text =
                __instance.consumeUnitLabel.text = unit;
            enhancement.maxProductionUnit.text =
                enhancement.maxConsumptionUnit.text = unitRate;

            if (counter.ContainsKey(__instance.entryData.itemId))
            {
                var productMetrics = counter[__instance.entryData.itemId];
                float maxProductValue = productMetrics.production / divider;
                float maxConsumeValue = productMetrics.consumption / divider;
                maxProduction = FormatMetric(maxProductValue);
                maxConsumption = FormatMetric(maxConsumeValue);

                producers = productMetrics.producers.ToString();
                consumers = productMetrics.consumers.ToString();

                if (originalConsumeValue >= (maxProductValue * BetterStats.lackOfProductionRatioTrigger.Value))
                    alertOnLackOfProduction = true;

                if (maxConsumeValue >= (maxProductValue * BetterStats.consumptionToProductionRatioTrigger.Value))
                    warnOnHighMaxConsumption = true;
            }

            __instance.productText.text = $"{originalProductText}";
            __instance.consumeText.text = $"{originalConsumeText}";

            enhancement.maxProductionValue.text = maxProduction;
            enhancement.maxConsumptionValue.text = maxConsumption;

            enhancement.counterProductionValue.text = producers;
            enhancement.counterConsumptionValue.text = consumers;

            enhancement.maxProductionValue.color = enhancement.counterProductionValue.color = __instance.productText.color;
            enhancement.maxConsumptionValue.color = enhancement.counterConsumptionValue.color = __instance.consumeText.color;

            if (alertOnLackOfProduction && !isTotalTimeWindow)
                enhancement.maxProductionValue.color = __instance.consumeText.color = new Color(1f, .25f, .25f, .5f);

            if (warnOnHighMaxConsumption && !isTotalTimeWindow)
                enhancement.maxConsumptionValue.color = new Color(1f, 1f, .25f, .5f);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UIStatisticsWindow), "ComputeDisplayEntries")]
        public static void UIProductionStatWindow_ComputeDisplayEntries_Prefix(UIStatisticsWindow __instance)
        {
            if (Time.frameCount % 10 != 0)
            {
                return;
            }
            counter.Clear();
            if (__instance.astroFilter == -1)
            {
                int factoryCount = __instance.gameData.factoryCount;
                for (int i = 0; i < factoryCount; i++)
                {
                    AddPlanetFactoryData(__instance.gameData.factories[i]);
                }
            }
            else if (__instance.astroFilter == 0)
            {
                AddPlanetFactoryData(__instance.gameData.localPlanet.factory);
            }
            else if (__instance.astroFilter % 100 > 0)
            {
                PlanetData planetData = __instance.gameData.galaxy.PlanetById(__instance.astroFilter);
                AddPlanetFactoryData(planetData.factory);
            }
            else if (__instance.astroFilter % 100 == 0)
            {
                int starId = __instance.astroFilter / 100;
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

        // speed of fastest belt(mk3 belt) is 1800 items per minute
        public const float BELT_MAX_ITEMS_PER_MINUTE = 1800;
        public const float TICKS_PER_SEC = 60.0f;
        private const float RAY_RECEIVER_GRAVITON_LENS_CONSUMPTION_RATE_PER_MIN = 0.1f;

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
                    production = frequency * speed * (float)((double)veinPool[veinId].amount * (double)VeinData.oilSpeedMultiplier);
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
            for (int i = 1; i < factorySystem.fractionatorCursor; i++)
            {
                var fractionator = factorySystem.fractionatorPool[i];
                if (fractionator.id != i) continue;

                if (fractionator.fluidId != 0)
                {
                    var productId = fractionator.fluidId;
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += 60f * 30f * fractionator.produceProb;
                    counter[productId].consumers++;
                }
                if (fractionator.productId != 0)
                {
                    var productId = fractionator.productId;
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
                if (lab.id != i) continue;
                float frequency = 60f / (float)((double)lab.timeSpend / 600000.0);

                if (lab.matrixMode)
                {
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
                else if (lab.researchMode && lab.techId > 0)
                {
                    // In this mode we can't just use lab.timeSpend to figure out how long it takes to consume 1 item (usually a cube)
                    // So, we figure out how many hashes a single cube represents and use the research mode research speed to come up with what is basically a research rate
                    var techProto = LDB.techs.Select(lab.techId);
                    if (techProto == null)
                        continue;
                    TechState techState = GameMain.history.TechState(techProto.ID);
                    for (int index = 0; index < techProto.itemArray.Length; ++index)
                    {
                        var item = techProto.Items[index];
                        var cubesNeeded = techProto.GetHashNeeded(techState.curLevel) * techProto.ItemPoints[index] / 3600L;
                        var researchRate = GameMain.history.techSpeed * 60.0f;
                        var hashesPerCube = (float) techState.hashNeeded / cubesNeeded;
                        var researchFreq = hashesPerCube / researchRate;
                        EnsureId(ref counter, item);
                        counter[item].consumers++;
                        counter[item].consumption += researchFreq * GameMain.history.techSpeed;
                    }
                }
            }
            double gasTotalHeat = planetFactory.planet.gasTotalHeat;
            var collectorsWorkCost = transport.collectorsWorkCost;
            for (int i = 1; i < transport.stationCursor; i++)
            {
                var station = transport.stationPool[i];
                if (station == null || station.id != i || !station.isCollector) continue;

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
                var isFuelConsumer = generator.fuelHeat > 0 && generator.fuelId > 0 && generator.productId == 0;
                if ((generator.productId == 0 || generator.productHeat == 0) && !isFuelConsumer)
                {
                    continue;
                }

                if (isFuelConsumer)
                {
                    // account for fuel consumption by power generator
                    var productId = generator.fuelId;
                    EnsureId(ref counter, productId);

                    counter[productId].consumption += 60.0f * TICKS_PER_SEC * generator.useFuelPerTick / generator.fuelHeat;
                    counter[productId].consumers++;
                }
                else
                {
                    var productId = generator.productId;
                    EnsureId(ref counter, productId);

                    counter[productId].production += 60.0f * TICKS_PER_SEC * generator.capacityCurrentTick / generator.productHeat;
                    counter[productId].producers++;
                    if (generator.catalystId > 0)
                    {
                        // account for consumption of critical photons by ray receivers
                        EnsureId(ref counter, generator.catalystId);
                        counter[generator.catalystId].consumption += RAY_RECEIVER_GRAVITON_LENS_CONSUMPTION_RATE_PER_MIN;
                        counter[generator.catalystId].consumers++;
                    }
                }
            }
        }
    }
}
