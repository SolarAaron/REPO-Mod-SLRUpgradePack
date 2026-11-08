using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib.Modules;
using REPOLib.Objects.Sdk;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public abstract class UpgradeBase<T> {
    private static readonly FieldRef<PlayerAvatar, bool>? IsLocalRef = FieldRefAccess<PlayerAvatar, bool>("isLocal");
    private readonly string _name;
    private readonly string _assetName;
    private readonly AssetBundle _assetBundle;
    private readonly string? _itemDisplayName;
    private readonly string? _itemDescription;
    public ConfigEntry<bool> UpgradeEnabled { get; protected set; }
    public ConfigEntry<T>? UpgradeAmount { get; protected set; }
    public ConfigEntry<bool>? UpgradeExponential { get; protected set; }
    public ConfigEntry<T>? UpgradeExpAmount { get; protected set; }
    public ConfigEntry<float> PriceMultiplier { get; protected set; }
    public ConfigEntry<int> StartingAmount { get; protected set; }
    public ConfigEntry<int>? MaxLevel { get; protected set; }
    public PlayerUpgrade? UpgradeRegister { get; protected set; }

    protected UpgradeBase(string name, string assetName, bool enabled, T upgradeAmount, bool exponential,
        T exponentialAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier, bool configureAmount,
        bool canBeExponential, int? maxLevel, string? upgradeAmountDescription = null,
        string? itemDisplayName = null, string? itemDescription = null) {
        _name = name;
        _assetName = assetName;
        _assetBundle = assetBundle;
        _itemDisplayName = itemDisplayName;
        _itemDescription = itemDescription;

        UpgradeEnabled = config.Bind($"{_name} Upgrade", "Enabled", enabled,
            $"Should the {_name} Upgrade be enabled?");
        PriceMultiplier = config.Bind($"{_name} Upgrade", "Price multiplier", priceMultiplier,
            "Multiplier of upgrade base price");
        StartingAmount = config.Bind($"{_name} Upgrade", "Starting Amount", 0,
            new ConfigDescription($"How many levels of {_name} to start a game with",
                new AcceptableValueRange<int>(0, maxLevel.GetValueOrDefault(100))));

        if (configureAmount) {
            UpgradeAmount = config.Bind($"{_name} Upgrade", $"{_name} Upgrade Power", upgradeAmount,
                upgradeAmountDescription ?? $"How much the {_name} Upgrade increments");
            if (canBeExponential) {
                UpgradeExponential = config.Bind($"{_name} Upgrade", "Exponential upgrade", exponential,
                    $"Should the {_name} Upgrade stack exponentially?");
                UpgradeExpAmount = config.Bind($"{_name} Upgrade", $"{_name} Upgrade Exponential Power",
                    exponentialAmount,
                    $"How much the Exponential {_name} upgrade increments");
            }
        }

        if (maxLevel.HasValue) {
            MaxLevel = config.Bind($"{_name} Upgrade", "Maximum Level", maxLevel.Value,
                new ConfigDescription("Maximum level", new AcceptableValueRange<int>(0, maxLevel.Value)));
        }

        if (UpgradeEnabled.Value) {
            RegisterUpgrade();
        }

        UpgradeEnabled.SettingChanged += UpgradeEnabledOnSettingChanged;
    }

    private void RegisterUpgrade() {
        var upgradeItem = _assetBundle.LoadAsset<ItemContent>(_assetName);
        if (!upgradeItem)
            throw new InvalidOperationException($"Could not load upgrade item asset '{_assetName}' for {_name}.");

        var prefab = upgradeItem.Prefab;
        if (!prefab || !prefab.item)
            throw new InvalidOperationException($"Upgrade item asset '{_assetName}' has no item prefab.");

        // Several bundle prefabs shared the same Item asset. Detach it before applying per-upgrade data.
        var item = UnityEngine.Object.Instantiate(prefab.item);
        if (!item)
            throw new InvalidOperationException($"Could not instantiate the item for upgrade {_name}.");

        prefab.item = item;
        if (!string.IsNullOrWhiteSpace(_itemDisplayName))
            item.itemName = _itemDisplayName;
        else
            item.itemName = $"{_name} Upgrade";
        if (_itemDescription != null)
            item.description = _itemDescription;
        var baseValue = item.value;
        var newVal = ScriptableObject.CreateInstance<Value>();
        const float priceRecalibration = 3200f / 375f;
        newVal.valueMin = baseValue.valueMin * PriceMultiplier.Value * priceRecalibration;
        newVal.valueMax = baseValue.valueMax * PriceMultiplier.Value * priceRecalibration;
        item.value = newVal;
        SLRUpgradePack.Logger.LogInfo($"Upgrade price range for {_name}: {baseValue.valueMin} - {baseValue.valueMax} x {PriceMultiplier.Value} = {newVal.valueMin} - {newVal.valueMax}");

        var itemRef = Items.RegisterItem(upgradeItem);
        if (itemRef == null)
            throw new InvalidOperationException($"REPOLib did not register the item for upgrade {_name}.");

        SLRUpgradePack.Logger.LogInfo(JsonUtility.ToJson(itemRef));
        var upgradeRegister = Upgrades.RegisterUpgrade(_name.Replace(" ", ""), item, InitUpgrade, UseUpgrade);
        if (upgradeRegister == null)
            throw new InvalidOperationException($"REPOLib did not register upgrade {_name}.");
        UpgradeRegister = upgradeRegister;

        if (MaxLevel is { } maxLevel) {
            SLRUpgradePack.LimitedUse[$"{_name} Upgrade"] = maxLevel.Value;
            SLRUpgradePack.Logger.LogInfo(
                $"{itemRef.PrefabName} is limited to {SLRUpgradePack.LimitedUse[$"{_name} Upgrade"]}");
        }
    }

    private void UpgradeEnabledOnSettingChanged(object sender, EventArgs e) {
        if (UpgradeEnabled.Value) {
            if (UpgradeRegister == null) {
                RegisterUpgrade();
            }

            var upgradeRegister = UpgradeRegister;
            if (upgradeRegister?.Item != null) upgradeRegister.Item.disabled = false;
        } else {
            var upgradeRegister = UpgradeRegister;
            if (upgradeRegister?.Item != null) upgradeRegister.Item.disabled = true;
        }
    }

    internal virtual void InitUpgrade(PlayerAvatar player, int level) {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
        if (IsLocalRef?.Invoke(player) == true) {
            SLRUpgradePack.Logger
                .LogInfo($"Init {_name}: {string.Join(",", GetActivePlayerLevels()
                    .Select(entry => (SemiFunc.PlayerGetName(entry.Player), entry.Level)))}");
        }
    }

    internal virtual void UseUpgrade(PlayerAvatar player, int level) {
        if (IsLocalRef?.Invoke(player) == true) {
            SLRUpgradePack.Logger
                .LogInfo($"Used {_name}: {string.Join(",", GetActivePlayerLevels()
                    .Select(entry => (SemiFunc.PlayerGetName(entry.Player), entry.Level)))}");
        }
    }

    public abstract T Calculate(T value, PlayerAvatar? player, int level);

    internal IEnumerable<(PlayerAvatar Player, int Level)> GetActivePlayerLevels() {
        var upgradeRegister = UpgradeRegister;
        if (upgradeRegister == null) yield break;

        if (!SemiFunc.IsMultiplayer()) {
            var localPlayer = SemiFunc.PlayerAvatarLocal();
            if (localPlayer)
                yield return (localPlayer, upgradeRegister.GetLevel(localPlayer));
            yield break;
        }

        if (upgradeRegister.PlayerDictionary == null) yield break;
        foreach (var playerLevel in upgradeRegister.PlayerDictionary) {
            var activePlayer = SemiFunc.PlayerAvatarGetFromSteamID(playerLevel.Key);
            if (activePlayer)
                yield return (activePlayer, playerLevel.Value);
        }
    }

    internal int GetTotalActivePlayerLevels() => GetActivePlayerLevels().Sum(entry => entry.Level);

    public static float DefaultCalculateFloatReduce(UpgradeBase<float> instance, string name, float value,
        PlayerAvatar? player, int level) {
        if (level > 0)
            if (instance.UpgradeExponential?.Value == true)
                return instance.UpgradeExpAmount is { } exponentialAmount
                    ? (float)(value / Math.Pow(exponentialAmount.Value, level))
                    : value;
            else if (instance.UpgradeAmount is { } amount)
                return value / (1f + (amount.Value * level));
        return value;
    }

    public static float DefaultCalculateFloatIncrease(UpgradeBase<float> instance, string name, float value,
        PlayerAvatar? player, int level) {
        if (level > 0)
            if (instance.UpgradeExponential?.Value == true)
                return instance.UpgradeExpAmount is { } exponentialAmount
                    ? (float)(value * Math.Pow(exponentialAmount.Value, level))
                    : value;
            else if (instance.UpgradeAmount is { } amount)
                return value * (1f + (amount.Value * level));
        return value;
    }

    protected GameObject? GetVisualsFromComponent(Component component) {
        GameObject? visuals = null;
        if (component.GetType() == typeof(EnemyParent)) {
            var enemyParent = component as EnemyParent;
            var enemy = Field(typeof(EnemyParent), "Enemy")?.GetValue(component) as Enemy;
            if (enemyParent && enemyParent.EnableObject) {
                var animator = enemyParent.EnableObject.GetComponentInChildren<Animator>();
                if (animator) visuals = animator.gameObject;
            }

            if (visuals == null) {
                var vision = enemy ? enemy.GetComponent<EnemyVision>() : null;
                if (vision && vision.VisionTransform) visuals = vision.VisionTransform.gameObject;
            }

            if (visuals == null && enemy)
                visuals = enemy.gameObject;
        } else if (component.GetType() == typeof(PlayerAvatar)) {
            var playerAvatar = component as PlayerAvatar;
            if (playerAvatar && playerAvatar.playerAvatarVisuals)
                visuals = playerAvatar.playerAvatarVisuals.gameObject;
        }

        return visuals;
    }
}

[Serializable]
public class NetworkMessage {
    public string? PlayerId { get; set; } = null;
    public int? PhotonId { get; set; } = null;
    public byte MessageType { get; set; }

    public static byte[] ToByteArray(NetworkMessage msg) {
        var formatter = new BinaryFormatter();
        using var stream = new MemoryStream();

        formatter.Serialize(stream, msg);
        return stream.ToArray();
    }

    public static NetworkMessage FromByteArray(byte[] bytes) {
        var formatter = new BinaryFormatter();
        using var stream = new MemoryStream(bytes);
        return (NetworkMessage)formatter.Deserialize(stream);
    }
}

public class EnumeratorWrapper : IEnumerable {
    public IEnumerator? enumerator;
    public Action? prefixAction, postfixAction;
    public Action<object>? preItemAction, postItemAction;
    public Func<object, object>? itemAction;

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    public IEnumerator GetEnumerator() {
        prefixAction?.Invoke();
        var source = enumerator;
        if (source == null) {
            postfixAction?.Invoke();
            yield break;
        }

        while (source.MoveNext()) {
            var item = source.Current;
            preItemAction?.Invoke(item);

            if (itemAction != null) yield return itemAction(item);
            else yield return item;

            postItemAction?.Invoke(item);
        }

        postfixAction?.Invoke();
    }
}

[HarmonyPatch(typeof(StatsManager), nameof(StatsManager.FetchPlayerUpgrades))]
public class StatsManagerPatch {
    private static readonly FieldRef<StatsManager, SortedDictionary<string, Dictionary<string, int>>> _dictionaryOfDictionariesRef = FieldRefAccess<StatsManager, SortedDictionary<string, Dictionary<string, int>>>("dictionaryOfDictionaries");

    private static bool Prefix(StatsManager __instance, ref string _steamID, ref Dictionary<string, int> __result) {
        var dictionary = new Dictionary<string, int>();
        // StatsUI may query an avatar before REPO has assigned its Steam ID. The game
        // cannot have saved upgrade entries for an uninitialized key, so return an
        // empty result rather than passing null to Dictionary.ContainsKey.
        if (string.IsNullOrWhiteSpace(_steamID)) {
            __result = dictionary;
            return false;
        }

        var regex = new Regex("(?<!^)(?=[A-Z])");
        foreach (KeyValuePair<string, Dictionary<string, int>> dictionaryOfDictionary in _dictionaryOfDictionariesRef.Invoke(__instance)) {
            if (!dictionaryOfDictionary.Key.StartsWith("playerUpgrade") ||
                !dictionaryOfDictionary.Value.ContainsKey(_steamID)) {
                continue;
            }

            var text = "";
            string[] array = regex.Split(dictionaryOfDictionary.Key);
            var flag = false;
            foreach (var text2 in array) {
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

            var value = dictionaryOfDictionary.Value[_steamID];

            // SLRUpgradePack.Logger.LogDebug($"Upgrade data [{dictionaryOfDictionary.Key} = {text}] value: {value}]");
            if (dictionary.TryGetValue(text, out var existing))
                SLRUpgradePack.Logger.LogWarning($"Duplicate upgrade found [{text}: {existing} => {value}]");

            dictionary[text] = value;
        }

        __result = dictionary;
        return false;
    }
}

[HarmonyPatch(typeof(ItemUpgrade), "PlayerUpgrade")]
[HarmonyPriority(Priority.First)]
public class ItemUpgradePatch {
    private static readonly FieldRef<ItemUpgrade, ItemToggle>? _itemToggleRef =
        FieldRefAccess<ItemUpgrade, ItemToggle>("itemToggle");

    private static readonly FieldRef<ItemToggle, int>? _playerTogglePhotonIdRef =
        FieldRefAccess<ItemToggle, int>("playerTogglePhotonID");

    private static readonly FieldRef<ItemUpgrade, ItemAttributes>? _itemAttributesRef =
        FieldRefAccess<ItemUpgrade, ItemAttributes>("itemAttributes");

    private static bool Prefix(ItemUpgrade __instance) {
        if (_playerTogglePhotonIdRef == null || _itemToggleRef == null || _itemAttributesRef == null) return true;
        var itemToggle = _itemToggleRef.Invoke(__instance);
        var itemAttributes = _itemAttributesRef.Invoke(__instance);
        if (!itemToggle || itemAttributes == null || !itemAttributes.item) return true;

        var user = SemiFunc.PlayerAvatarGetFromPhotonID(_playerTogglePhotonIdRef.Invoke(itemToggle));
        if (!user) return true;

        var itemName = itemAttributes.item.itemName;
        SLRUpgradePack.Logger.LogInfo($"Looking for {itemName} in {string.Join(",", SLRUpgradePack.LimitedUse)}");
        if (SLRUpgradePack.LimitedUse.TryGetValue(itemName, out var value)) {
            var inventoryUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
            var userLevel = inventoryUpgrade.UpgradeRegister?.GetLevel(user) ?? 0;
            SLRUpgradePack.Logger.LogInfo($"Checking for level limit for {SemiFunc.PlayerGetName(user)}:{itemName} => {userLevel} < {value}");
            return userLevel < value;
        }

        return true;
    }
}

[HarmonyPatch(typeof(ShopManager), "GetAllItemsFromStatsManager")]
public class ShopManagerSingleUsePatch {
    private static void Prefix(ShopManager __instance) {
        foreach (var obj in StatsManager.instance.itemDictionary.Values) {
            if (SLRUpgradePack.LimitedUse.TryGetValue(obj.itemName, out var value)) {
                obj.maxPurchaseAmount = GameDirector.instance.PlayerList.Count * value;
            }
        }
    }

    private static void Postfix(ShopManager __instance) {
        __instance.potentialItemUpgrades.RemoveAll(item => item.disabled);
    }
}
