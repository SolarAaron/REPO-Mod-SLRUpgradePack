using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine;

namespace SLRUpgradePack.UpgradeManagers;

public class OverchargeUpgrade: UpgradeBase<float> {
    public OverchargeUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier) : 
        base("Overcharge", "assets/repo/mods/resources/items/items/item upgrade overcharge.asset", enabled, upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000) {
    }

    public override float Calculate (float value, PlayerAvatar player, int level) => DefaultCalculateFloatReduce(this, "Overcharge", value, player, level);
}

[HarmonyPatch(typeof(PhysGrabber))]
public class PhysGrabberPatch {
    public static bool Prepare() {
        return typeof(PhysGrabber).GetMethods().Any(method => method.Name.Equals("PhysGrabOverCharge"));
    }
    
    [HarmonyPatch("PhysGrabOverCharge")]
    private static void Prefix(PhysGrabber __instance, ref float _amount) {
        var overchargeUpgrade = SLRUpgradePack.OverchargeUpgradeInstance;
        if(overchargeUpgrade.UpgradeEnabled.Value) {
            SLRUpgradePack.Logger.LogDebug($"Original overcharge amount: {_amount}");
            _amount = overchargeUpgrade.Calculate(_amount, __instance.playerAvatar, overchargeUpgrade.UpgradeLevel);
            SLRUpgradePack.Logger
                          .LogDebug($"After calculation with level {Upgrades.GetUpgrade("Overcharge").GetLevel(__instance.playerAvatar)}: {_amount}");
        }
    }
}
