using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine;
using UnityEngine.Events;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public abstract class UpgradeBase<T> {
    public ConfigEntry<bool> UpgradeEnabled { get; protected set; }
    public ConfigEntry<T> UpgradeAmount { get; protected set; }
    public ConfigEntry<bool> UpgradeExponential { get; protected set; }
    public ConfigEntry<T> UpgradeExpAmount { get; protected set; }
    public ConfigEntry<float> PriceMultiplier { get; protected set; }
    public ConfigEntry<int> MinPrice { get; protected set; }
    public ConfigEntry<int> MaxPrice { get; protected set; }
    public bool IsIntegration { get; private set; }
    public int UpgradeLevel { get; protected set; }
    public PlayerUpgrade UpgradeRegister { get; protected set; }

    protected UpgradeBase(string name, string assetName, bool enabled, T upgradeAmount, bool exponential, T exponentialAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier, bool configureAmount, int minPrice, int maxPrice, bool canBeExponential, bool singleUse) {
        IsIntegration = false;
        
        UpgradeEnabled = config.Bind($"{name} Upgrade", "Enabled", enabled,
                                     $"Should the {name} Upgrade be enabled?");
        PriceMultiplier = config.Bind($"{name} Upgrade", "Price multiplier", priceMultiplier, "Multiplier of upgrade base price");
        if(configureAmount) {
            UpgradeAmount = config.Bind($"{name} Upgrade", $"{name} Upgrade Power", upgradeAmount,
                                        $"How much the {name} Upgrade increments");
            if(canBeExponential) {
                UpgradeExponential = config.Bind($"{name} Upgrade", "Exponential upgrade", exponential,
                                                 $"Should the {name} Upgrade stack exponentially?");
                UpgradeExpAmount = config.Bind($"{name} Upgrade", $"{name} Upgrade Exponential Power", exponentialAmount,
                                               $"How much the Exponential {name} upgrade increments");
            }
        }
        if (UpgradeEnabled.Value) {
            Item upgradeItem = assetBundle.LoadAsset<Item>(assetName);
            
            if (upgradeItem.value == null) { // it's probably a moreupgrades integration
                upgradeItem.value = ScriptableObject.CreateInstance<Value>();

                MinPrice = config.Bind($"{name} Upgrade", "Base Value Minimum", minPrice, "Minimum value to use for price calculation");
                MaxPrice = config.Bind($"{name} Upgrade", "Base Value Maximum", maxPrice, "Maximum value to use for price calculation");
                
                upgradeItem.value.valueMin = MinPrice.Value;
                upgradeItem.value.valueMax = MaxPrice.Value;
                upgradeItem.maxAmountInShop = !singleUse ? 2 : 1;
                upgradeItem.maxAmount = !singleUse ? 10 : 1;
                upgradeItem.maxPurchaseAmount = !singleUse ? 0 : 1;
                upgradeItem.maxPurchase = singleUse;
            }

            if (upgradeItem.prefab == null) { // it's a moreupgrades integration
                upgradeItem.prefab = assetBundle.LoadAsset<GameObject>($"{assetName} Prefab");
                upgradeItem.prefab.GetComponent<ItemAttributes>().item = upgradeItem;
                upgradeItem.itemAssetName = upgradeItem.name = upgradeItem.prefab.name = $"Item Upgrade {name}";
                upgradeItem.itemName = name;
            }

            if (!upgradeItem.prefab.TryGetComponent<REPOLibItemUpgrade>(out var libItemUpgrade)) { // it's a moreupgrades integration
                libItemUpgrade = upgradeItem.prefab.AddComponent<REPOLibItemUpgrade>();
                FieldRefAccess<REPOLibItemUpgrade, string>("_upgradeId").Invoke(libItemUpgrade) = name.Replace(" ", "");
                IsIntegration = true;
            }
            
            SLRUpgradePack.Logger.LogInfo($"Upgrade price range (default) {upgradeItem.value.valueMin} - {upgradeItem.value.valueMax}");
            var newVal = ScriptableObject.CreateInstance<Value>();
            newVal.valueMin = upgradeItem.value.valueMin * PriceMultiplier.Value;
            newVal.valueMax = upgradeItem.value.valueMax * PriceMultiplier.Value;
            upgradeItem.value = newVal;
            Items.RegisterItem(upgradeItem);
            UpgradeRegister = Upgrades.RegisterUpgrade(name.Replace(" ", ""), upgradeItem,
                                                       InitUpgrade, UseUpgrade);
            
        }
    }

    protected virtual void InitUpgrade(PlayerAvatar player, int level) {
        if (Traverse.Create(player).Field<bool>("isLocal").Value) {
            UpgradeLevel = level;
            SLRUpgradePack.Logger
                          .LogInfo($"Init: {string.Join(",", UpgradeRegister.PlayerDictionary)}");
        }
    }

    protected void UseUpgrade(PlayerAvatar player, int level) {
        if (Traverse.Create(player).Field<bool>("isLocal").Value) {
            UpgradeLevel = level;
            SLRUpgradePack.Logger
                          .LogInfo($"Used: {string.Join(",", UpgradeRegister.PlayerDictionary)}");
        }
    }

    public abstract T Calculate(T value, PlayerAvatar player, int level);

    public static float DefaultCalculateFloatReduce(UpgradeBase<float> instance, string name, float value,
                                              PlayerAvatar player, int level) {
        if (level > 0)
            if (instance.UpgradeExponential.Value) return (float)(value / Math.Pow(instance.UpgradeExpAmount.Value, level));
            else return value / (1f + (instance.UpgradeAmount.Value * level));
        return value;
    }
    
    public static float DefaultCalculateFloatIncrease(UpgradeBase<float> instance, string name, float value,
                                                    PlayerAvatar player, int level) {
        if (level > 0)
            if (instance.UpgradeExponential.Value) return (float)(value * Math.Pow(instance.UpgradeExpAmount.Value, level));
            else return value * (1f + (instance.UpgradeAmount.Value * level));
        return value;
    }
    
    protected GameObject GetVisualsFromComponent(Component component)
    {
        GameObject visuals = null;
        if (component.GetType() == typeof(EnemyParent))
        {
            EnemyParent enemyParent = component as EnemyParent;
            Enemy enemy = (Enemy)AccessTools.Field(typeof(EnemyParent), "Enemy").GetValue(component);
            try
            {
                visuals = enemyParent.EnableObject.gameObject.GetComponentInChildren<Animator>().gameObject;
            }
            catch {}
            if (visuals == null)
            {
                try
                {
                    visuals = enemy.GetComponent<EnemyVision>().VisionTransform.gameObject;
                }
                catch { }
            }
            if (visuals == null)
                visuals = enemy.gameObject;
        }
        else if (component.GetType() == typeof(PlayerAvatar))
        {
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

[HarmonyPatch(typeof(StatsManager), nameof(StatsManager.FetchPlayerUpgrades))]
public class StatsManagerPatch {
    private static bool Prefix(StatsManager __instance, ref string _steamID, ref Dictionary<string, int> __result) {
        Dictionary<string, int> dictionary = new Dictionary<string, int>();
        Regex regex = new Regex("(?<!^)(?=[A-Z])");
        foreach (KeyValuePair<string, Dictionary<string, int>> dictionaryOfDictionary in __instance.dictionaryOfDictionaries)
        {
            if (!dictionaryOfDictionary.Key.StartsWith("playerUpgrade") || !dictionaryOfDictionary.Value.ContainsKey(_steamID))
            {
                continue;
            }
            string text = "";
            string[] array = regex.Split(dictionaryOfDictionary.Key);
            bool flag = false;
            foreach (string text2 in array)
            {
                if (flag)
                {
                    text = text + text2 + " ";
                }
                if (text2 == "Upgrade")
                {
                    flag = true;
                }
            }
            
            text = text.Replace("Modded", "").Trim();
            
            if(text.Length == 0) {
                //SLRUpgradePack.Logger.LogDebug($"Extra data in dictionary: {dictionaryOfDictionary.Key}: {dictionaryOfDictionary.Value[_steamID]}");
                continue;
            }
            
            int value = dictionaryOfDictionary.Value[_steamID];
            
            // SLRUpgradePack.Logger.LogDebug($"Upgrade data [{dictionaryOfDictionary.Key} = {text}] value: {value}]");
            if(dictionary.TryGetValue(text, out var existing)) SLRUpgradePack.Logger.LogWarning($"Duplicate upgrade found [{text}: {existing} => {value}]");
            
            dictionary[text] = value;
        }
        __result = dictionary;
        return false;
    }
}
