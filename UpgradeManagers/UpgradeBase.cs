using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib.Modules;
using REPOLib.Objects.Sdk;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public abstract class UpgradeBase<T> {
    private string _name;
    private string _assetName;
    private AssetBundle _assetBundle;
    public ConfigEntry<bool> UpgradeEnabled { get; protected set; }
    public ConfigEntry<T> UpgradeAmount { get; protected set; }
    public ConfigEntry<bool> UpgradeExponential { get; protected set; }
    public ConfigEntry<T> UpgradeExpAmount { get; protected set; }
    public ConfigEntry<float> PriceMultiplier { get; protected set; }
    public ConfigEntry<int> StartingAmount { get; protected set; }
    public ConfigEntry<int> MaxLevel { get; protected set; }
    public PlayerUpgrade UpgradeRegister { get; protected set; }

    protected UpgradeBase(string name, string assetName, bool enabled, T upgradeAmount, bool exponential, T exponentialAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier, bool configureAmount, bool canBeExponential, int? maxLevel) {
        _name = name;
        _assetName = assetName;
        _assetBundle = assetBundle;

        UpgradeEnabled = config.Bind($"{_name} Upgrade", "Enabled", enabled,
                                     $"Should the {_name} Upgrade be enabled?");
        PriceMultiplier = config.Bind($"{_name} Upgrade", "Price multiplier", priceMultiplier, "Multiplier of upgrade base price");
        StartingAmount = config.Bind($"{_name} Upgrade", "Starting Amount", 0, $"How many levels of {_name} to start a game with");

        if (configureAmount) {
            UpgradeAmount = config.Bind($"{_name} Upgrade", $"{_name} Upgrade Power", upgradeAmount,
                                        $"How much the {_name} Upgrade increments");
            if (canBeExponential) {
                UpgradeExponential = config.Bind($"{_name} Upgrade", "Exponential upgrade", exponential,
                                                 $"Should the {_name} Upgrade stack exponentially?");
                UpgradeExpAmount = config.Bind($"{_name} Upgrade", $"{_name} Upgrade Exponential Power", exponentialAmount,
                                               $"How much the Exponential {_name} upgrade increments");
            }
        }

        if (maxLevel.HasValue) {
            MaxLevel = config.Bind<int>($"{_name} Upgrade", "Maximum Level", maxLevel.Value, new ConfigDescription("", new AcceptableValueRange<int>(0, maxLevel.Value)));
        }

        if (UpgradeEnabled.Value) {
            RegisterUpgrade();
        }

        UpgradeEnabled.SettingChanged += UpgradeEnabledOnSettingChanged;
    }

    private void RegisterUpgrade() {
        ItemContent upgradeItem = _assetBundle.LoadAsset<ItemContent>(_assetName);

        SLRUpgradePack.Logger.LogInfo($"Upgrade price range (default) {upgradeItem.Prefab.item.value.valueMin} - {upgradeItem.Prefab.item.value.valueMax}");
        var newVal = ScriptableObject.CreateInstance<Value>();
        newVal.valueMin = upgradeItem.Prefab.item.value.valueMin * PriceMultiplier.Value;
        newVal.valueMax = upgradeItem.Prefab.item.value.valueMax * PriceMultiplier.Value;
        upgradeItem.Prefab.item.value = newVal;

        var itemRef = Items.RegisterItem(upgradeItem);
        UpgradeRegister = Upgrades.RegisterUpgrade(_name.Replace(" ", ""), upgradeItem.Prefab.item,
                                                   InitUpgrade, UseUpgrade);
    }

    private void UpgradeEnabledOnSettingChanged(object sender, EventArgs e) {
        if (UpgradeEnabled.Value) {
            if (UpgradeRegister == null) {
                RegisterUpgrade();
            }

            UpgradeRegister.Item.disabled = false;
        } else {
            if (UpgradeRegister != null) UpgradeRegister.Item.disabled = true;
        }
    }

    internal virtual void InitUpgrade(PlayerAvatar player, int level) {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
        if (Traverse.Create(player).Field<bool>("isLocal").Value) {
            SLRUpgradePack.Logger
                          .LogInfo($"Init: {string.Join(",", UpgradeRegister.PlayerDictionary
                                                                            .Where(kvp => SemiFunc.PlayerAvatarGetFromSteamID(kvp.Key) != null)
                                                                            .Select(kvp => (SemiFunc.PlayerGetName(SemiFunc.PlayerAvatarGetFromSteamID(kvp.Key)), kvp.Value)))}");
        }
    }

    internal void UseUpgrade(PlayerAvatar player, int level) {
        if (Traverse.Create(player).Field<bool>("isLocal").Value) {
            SLRUpgradePack.Logger
                          .LogInfo($"Used: {string.Join(",", UpgradeRegister.PlayerDictionary
                                                                            .Where(kvp => SemiFunc.PlayerAvatarGetFromSteamID(kvp.Key) != null)
                                                                            .Select(kvp => (SemiFunc.PlayerGetName(SemiFunc.PlayerAvatarGetFromSteamID(kvp.Key)), kvp.Value)))}");
        }
    }

    public abstract T Calculate(T value, PlayerAvatar player, int level);

    public static float DefaultCalculateFloatReduce(UpgradeBase<float> instance, string name, float value,
                                                    PlayerAvatar player, int level) {
        if (level > 0)
            if (instance.UpgradeExponential.Value) return (float) (value / Math.Pow(instance.UpgradeExpAmount.Value, level));
            else return value / (1f + (instance.UpgradeAmount.Value * level));
        return value;
    }

    public static float DefaultCalculateFloatIncrease(UpgradeBase<float> instance, string name, float value,
                                                      PlayerAvatar player, int level) {
        if (level > 0)
            if (instance.UpgradeExponential.Value) return (float) (value * Math.Pow(instance.UpgradeExpAmount.Value, level));
            else return value * (1f + (instance.UpgradeAmount.Value * level));
        return value;
    }

    protected GameObject GetVisualsFromComponent(Component component) {
        GameObject visuals = null;
        if (component.GetType() == typeof(EnemyParent)) {
            EnemyParent enemyParent = component as EnemyParent;
            Enemy enemy = (Enemy) Field(typeof(EnemyParent), "Enemy").GetValue(component);
            try {
                visuals = enemyParent.EnableObject.gameObject.GetComponentInChildren<Animator>().gameObject;
            } catch { }

            if (visuals == null) {
                try {
                    visuals = enemy.GetComponent<EnemyVision>().VisionTransform.gameObject;
                } catch { }
            }

            if (visuals == null)
                visuals = enemy.gameObject;
        } else if (component.GetType() == typeof(PlayerAvatar)) {
            PlayerAvatar playerAvatar = component as PlayerAvatar;
            visuals = playerAvatar.playerAvatarVisuals.gameObject;
        }

        return visuals;
    }
}

public class EnumeratorWrapper : IEnumerable {
    public IEnumerator enumerator;
    public Action? prefixAction, postfixAction;
    public Action<object>? preItemAction, postItemAction;
    public Func<object, object>? itemAction;

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    public IEnumerator GetEnumerator() {
        prefixAction?.Invoke();
        while (enumerator.MoveNext()) {
            var item = enumerator.Current;
            preItemAction?.Invoke(item);

            if (itemAction != null) yield return itemAction(item);
            else yield return item;

            postItemAction?.Invoke(item);
        }

        postfixAction?.Invoke();
    }
}

[HarmonyPatch(typeof(StatsManager), nameof (StatsManager.FetchPlayerUpgrades))]
public class StatsManagerPatch {
    private static bool Prefix(StatsManager __instance, ref string _steamID, ref Dictionary<string, int> __result) {
        Dictionary<string, int> dictionary = new Dictionary<string, int>();
        Regex regex = new Regex("(?<!^)(?=[A-Z])");
        foreach (KeyValuePair<string, Dictionary<string, int>> dictionaryOfDictionary in __instance.dictionaryOfDictionaries) {
            if (!dictionaryOfDictionary.Key.StartsWith("playerUpgrade") || !dictionaryOfDictionary.Value.ContainsKey(_steamID)) {
                continue;
            }

            string text = "";
            string[] array = regex.Split(dictionaryOfDictionary.Key);
            bool flag = false;
            foreach (string text2 in array) {
                if (flag) {
                    text = text + text2 + " ";
                }

                if (text2 == "Upgrade") {
                    flag = true;
                }
            }

            text = text.Replace("Modded", "").Trim();

            if (text.Length == 0) {
                //SLRUpgradePack.Logger.LogDebug($"Extra data in dictionary: {dictionaryOfDictionary.Key}: {dictionaryOfDictionary.Value[_steamID]}");
                continue;
            }

            int value = dictionaryOfDictionary.Value[_steamID];

            // SLRUpgradePack.Logger.LogDebug($"Upgrade data [{dictionaryOfDictionary.Key} = {text}] value: {value}]");
            if (dictionary.TryGetValue(text, out var existing)) SLRUpgradePack.Logger.LogWarning($"Duplicate upgrade found [{text}: {existing} => {value}]");

            dictionary[text] = value;
        }

        __result = dictionary;
        return false;
    }
}

[HarmonyPatch(typeof(ItemUpgrade), "PlayerUpgrade")]
[HarmonyPriority(Priority.First)]
public class ItemUpgradePatch {
    private static FieldRef<ItemUpgrade, ItemToggle>? _itemToggleRef = FieldRefAccess<ItemUpgrade, ItemToggle>("itemToggle");
    private static FieldRef<ItemToggle, int>? _playerTogglePhotonIdRef = FieldRefAccess<ItemToggle, int>("playerTogglePhotonId");
    private static FieldRef<ItemUpgrade, ItemAttributes>? _itemAttributesRef = FieldRefAccess<ItemUpgrade, ItemAttributes>("itemAttributes");

    private static bool Prefix(ItemUpgrade __instance) {
        PlayerAvatar user = SemiFunc.PlayerAvatarGetFromPhotonID(_playerTogglePhotonIdRef.Invoke(_itemToggleRef.Invoke(__instance)));
        switch (_itemAttributesRef.Invoke(__instance).item.itemName) {
            case "Item Upgrade Inventory Slot": {
                if (SLRUpgradePack.InventorySlotUpgradeInstance.UpgradeRegister.GetLevel(user) >= SLRUpgradePack.InventorySlotUpgradeInstance.MaxLevel.Value) return false;
                return true;
            }
            default: return true;
        }
    }
}

[HarmonyPatch(typeof(ShopManager), "GetAllItemsFromStatsManager")]
public class ShopManagerSingleUsePatch {
    internal static readonly Dictionary<string, int> LimitedUse = new Dictionary<string, int>() { { "Item Upgrade Inventory Slot", SLRUpgradePack.InventorySlotUpgradeInstance.MaxLevel.Value } };

    private static void Prefix(ShopManager __instance) {
        foreach (Item obj in StatsManager.instance.itemDictionary.Values) {
            if (LimitedUse.ContainsKey(obj.itemName)) {
                obj.maxPurchaseAmount = GameDirector.instance.PlayerList.Count * LimitedUse[obj.itemName];
            }
        }
    }

    private static void Postfix(ShopManager __instance) {
        __instance.potentialItemUpgrades.RemoveAll(item => item.disabled);
    }
}