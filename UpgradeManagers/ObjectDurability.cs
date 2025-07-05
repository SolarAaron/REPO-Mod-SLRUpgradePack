using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine;

namespace SLRUpgradePack.UpgradeManagers;

public class ObjectDurabilityUpgrade: UpgradeBase<float> {
    public ObjectDurabilityUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier) : 
        base("Object Durability", "assets/repo/mods/resources/items/items/item upgrade durability.asset", enabled, upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000, true, false) {
    }

    public override float Calculate (float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "ObjectDurability", value, player, level);
}

[HarmonyPatch(typeof(ValuableObject), "Start")]
public class ValuableObjectDurabilityPatch {
    [HarmonyPriority(Priority.Last)]
    internal static void Prefix(ValuableObject __instance) {
        var objectDurabilityUpgrade = SLRUpgradePack.ObjectDurabilityUpgradeInstance;
        if(SemiFunc.IsMasterClientOrSingleplayer() && objectDurabilityUpgrade.UpgradeEnabled.Value) {
            SLRUpgradePack.Logger
                          .LogDebug($"Original durability: {__instance.durabilityPreset.durability}");
            var customDurability = Durability.Instantiate(__instance.durabilityPreset);
            
            foreach (var pair in objectDurabilityUpgrade.UpgradeRegister.PlayerDictionary) {
                customDurability.durability =
                    objectDurabilityUpgrade.Calculate(__instance.durabilityPreset.durability, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value);
                customDurability.fragility = 
                    ObjectDurabilityUpgrade.DefaultCalculateFloatReduce(objectDurabilityUpgrade, "ObjectDurability", __instance.durabilityPreset.fragility, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value);
            }
            __instance.durabilityPreset = customDurability;
            
            SLRUpgradePack.Logger.LogDebug($"After calculation with levels {string.Join(",", Upgrades.GetUpgrade("ObjectDurability").PlayerDictionary)}: {__instance.durabilityPreset.durability}");
        }
    }
}

public class SpawnedValuableDurabilityPatch<TSVT> where TSVT : MonoBehaviour {
    protected static void DoValuableStuff(TSVT spawnedValuable) {
        if (spawnedValuable.TryGetComponent<ValuableObject>(out var valuableObject)) {
            SLRUpgradePack.Logger.LogDebug($"Valuable spawned with: {valuableObject.durabilityPreset.durability} / {valuableObject.durabilityPreset.fragility}");
            
            ValuableObjectDurabilityPatch.Prefix(valuableObject);
        }
    }
}

[HarmonyPatch(typeof(SurplusValuable), "Start")]
public class SurplusValuableDurabilityValuePatch: SpawnedValuableDurabilityPatch<SurplusValuable> {
    internal static void Prefix(SurplusValuable __instance) {
        DoValuableStuff(__instance);
    }
}

[HarmonyPatch(typeof(EnemyValuable), "Start")]
public class EnemyValuableDurabilityValuePatch: SpawnedValuableDurabilityPatch<EnemyValuable> {
    internal static void Prefix(EnemyValuable __instance) {
        DoValuableStuff(__instance);
    }
}
