using System;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine;

namespace SLRUpgradePack.UpgradeManagers;

public class ArmorUpgrade : UpgradeBase<float> {
    public ArmorUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier) :
        base("Armor", "assets/repo/mods/resources/items/items/item upgrade armor.asset", enabled, upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000, true, false) { }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatReduce(this, "Armor", value, player, level);
}

[HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Hurt))]
public class PlayerHealthArmorPatch {
    private static void Prefix(PlayerHealth __instance, ref int damage, PlayerAvatar ___playerAvatar) {
        var armorUpgrade = SLRUpgradePack.ArmorUpgradeInstance;
        if (armorUpgrade.UpgradeEnabled.Value) {
            SLRUpgradePack.Logger.LogDebug($"Original damage amount: {damage}");
            damage = (int)Math.Ceiling(armorUpgrade.Calculate(damage, ___playerAvatar, armorUpgrade.UpgradeRegister.GetLevel(___playerAvatar)));
            SLRUpgradePack.Logger
                          .LogDebug($"After calculation with level {Upgrades.GetUpgrade("Armor").GetLevel(___playerAvatar)}: {damage}");
        }
    }
}