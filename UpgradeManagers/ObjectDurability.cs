using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SLRUpgradePack.UpgradeManagers;

public class ObjectDurabilityUpgrade : UpgradeBase<float> {
    public ObjectDurabilityUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, float priceMultiplier) :
        base("Object Durability", "assets/repo/mods/resources/items/items/item upgrade durability lib.asset", enabled,
            upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true,
            ((int?)null)) { }

    public override float Calculate(float damage, PlayerAvatar player, int level) =>
        DefaultCalculateFloatReduce(this, "ObjectDurability", damage, player, level);
}

[HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "Break")]
public static class ObjectDurabilityDamagePatch {
    internal static void Prefix(PhysGrabObjectImpactDetector __instance, ref float valueLost) {
        var objectDurabilityUpgrade = SLRUpgradePack.ObjectDurabilityUpgradeInstance;
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !__instance.isValuable || valueLost <= 0f ||
            !objectDurabilityUpgrade.UpgradeEnabled.Value) return;

        var totalLevels = objectDurabilityUpgrade.GetTotalActivePlayerLevels();
        if (totalLevels <= 0) return;

        var reducedDamage = objectDurabilityUpgrade.Calculate(valueLost, null!, totalLevels);
        // Invalid config values should not turn damage into healing, or increase damage.
        if (!float.IsNaN(reducedDamage) && !float.IsInfinity(reducedDamage))
            valueLost = Math.Min(valueLost, Math.Max(0f, reducedDamage));
    }
}
