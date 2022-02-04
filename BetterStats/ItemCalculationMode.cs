using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using static BetterStats.BetterStats;

namespace BetterStats
{
    public enum ItemCalculationMode
    {
        None,
        Normal,
        ForceSpeed,
        ForceProductivity
    }

    /// <summary>
    /// Manages currently selected proliferator calculation options for each item
    /// </summary>
    public class ItemCalculationRuntimeSetting
    {
        private ItemCalculationMode _mode = ItemCalculationMode.Normal;
        private bool _enabled;
        public readonly int productId;
        public readonly bool productivitySupported;
        public readonly bool speedSupported;
        private ConfigEntry<string> _configEntry;
        private static readonly Dictionary<int, ConfigEntry<string>> ConfigEntries = new();
        private static readonly Dictionary<int, ItemCalculationRuntimeSetting> Pool = new();
        private static ConfigFile configFile;


        private ItemCalculationRuntimeSetting(int productId)
        {
            this.productId = productId;
            var itemProto = LDB.items.Select(productId);
            if (itemProto != null)
            {
                speedSupported = itemProto.recipes is { Count: > 0 };
                productivitySupported = speedSupported && itemProto.recipes.Any(r => r.productive);
                _enabled = speedSupported;
            }

            if (productivitySupported)
                Log.LogDebug($"productivity supported for {itemProto?.name}");
            else
                Log.LogDebug($"NO productivity mode supported for {itemProto?.name}");
        }

        public ItemCalculationMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                Pool[productId]._mode = value;
                Save();
            }
        }

        public bool Enabled
        {
            get => _enabled && speedSupported;
            set
            {
                _enabled = value;
                Pool[productId]._enabled = value;
                Save();
            }
        }

        private void Save()
        {
            _configEntry.Value = Serialize();
            Log.LogDebug($"saved {productId} entry {_configEntry.Value}");
        }

        private static ItemCalculationRuntimeSetting Deserialize(string strVal)
        {
            var serializableRuntimeState = JsonUtility.FromJson<SerializableRuntimeState>(strVal);

            return new ItemCalculationRuntimeSetting(serializableRuntimeState.productId)
            {
                _enabled = serializableRuntimeState.enabled,
                _mode = (ItemCalculationMode)serializableRuntimeState.mode,
            };
        }

        private string Serialize()
        {
            return JsonUtility.ToJson(SerializableRuntimeState.From(this));
        }

        private static void InitConfig(ItemProto itemProto)
        {
            if (configFile == null)
            {
                configFile = new ConfigFile($"{Paths.ConfigPath}/{PluginInfo.PLUGIN_NAME}/CustomProductSettings.cfg", true);
            }

            var defaultValue = new ItemCalculationRuntimeSetting(itemProto.ID);

            var configEntry = configFile.Bind("Internal", $"ProliferatorStatsSetting_{itemProto.ID}",
                defaultValue.Serialize(),
                "For internal use only");
            ConfigEntries[itemProto.ID] = configEntry;

            Pool[itemProto.ID] = Deserialize(ConfigEntries[itemProto.ID].Value);
            Pool[itemProto.ID]._configEntry = configEntry;
            Log.LogDebug($"Loaded {itemProto.name} runtime settings");
        }

        public static ItemCalculationRuntimeSetting ForItemId(int itemId)
        {
            return Pool[itemId];
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ItemProto), nameof(ItemProto.Preload))]
        public static void UIStatisticsWindow__OnOpen_Postfix(ItemProto __instance)
        {
            InitConfig(__instance);
        }
    }

    [Serializable]
    public class SerializableRuntimeState
    {
        [SerializeField] public int mode;
        [SerializeField] public bool enabled;
        [SerializeField] public int productId;

        public SerializableRuntimeState(int productId, bool enabled, ItemCalculationMode mode)
        {
            this.productId = productId;
            this.enabled = enabled;
            this.mode = (int)mode;
        }

        public static SerializableRuntimeState From(ItemCalculationRuntimeSetting setting)
        {
            return new SerializableRuntimeState(setting.productId, setting.Enabled, setting.Mode);
        }
    }
}
