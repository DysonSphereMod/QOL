using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
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
        public static readonly ItemCalculationRuntimeSetting None = new(0)
        {
            _enabled = false,
            _mode = ItemCalculationMode.None,
        };
        private ItemCalculationMode _mode = ItemCalculationMode.Normal;
        private bool _enabled;

        public readonly int productId;

        private ConfigEntry<string> _configEntry;
        private static readonly Dictionary<int, ConfigEntry<string>> ConfigEntries = new();
        private static readonly Dictionary<int, ItemCalculationRuntimeSetting> Pool = new();
        private static ConfigFile configFile;
        private readonly ItemProto _itemProto;
        private ItemCalculationRuntimeSetting(int productId)
        {
            this.productId = productId;
            var proto = LDB.items.Select(productId);
            if (proto != null)
            {
                _itemProto = proto;
            }
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
            get => _enabled;
            set
            {
                _enabled = value;
                Pool[productId]._enabled = value;
                Save();
            }
        }

        public bool SpeedSupported => _itemProto is { recipes: { Count: > 0 } };

        public bool ProductivitySupported
        {
            get { return SpeedSupported && _itemProto.recipes.Any(r => r.productive); }
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

        public static void InitConfig()
        {
            configFile = new ConfigFile($"{Paths.ConfigPath}/{PluginInfo.PLUGIN_NAME}/CustomProductSettings.cfg", true);

            foreach (var itemProto in LDB.items.dataArray)
            {
                var defaultValue = new ItemCalculationRuntimeSetting(itemProto.ID)
                {
                    _enabled = true,
                    _mode = ItemCalculationMode.Normal
                };

                var configEntry = configFile.Bind("Internal", $"ProliferatorStatsSetting_{itemProto.ID}",
                    defaultValue.Serialize(),
                    "For internal use only");
                ConfigEntries[itemProto.ID] = configEntry;

                Pool[itemProto.ID] = Deserialize(ConfigEntries[itemProto.ID].Value);
                Pool[itemProto.ID]._configEntry = configEntry;
                Log.LogDebug($"Loaded {itemProto.name} runtime settings");
            }
        }

        public static ItemCalculationRuntimeSetting ForItemId(int itemId)
        {
            return disableProliferatorCalc.Value ? None : Pool[itemId];
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
