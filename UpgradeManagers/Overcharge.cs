using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SLRUpgradePack.UpgradeManagers;

public class OverchargeUpgrade : UpgradeBase<float> {
    public OverchargeUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, float priceMultiplier) :
        base("Overcharge", "assets/repo/mods/resources/items/items/item upgrade overcharge lib.asset", enabled,
            upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true,
            ((int?)null)) { }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatReduce(this, "Overcharge", value, player, level);
}

[HarmonyPatch(typeof(PhysGrabber))]
public class PhysGrabberPatch {
    public static bool Prepare() {
        return typeof(PhysGrabber).GetMethods().Any(method => method.Name.Equals("PhysGrabOverCharge"));
    }

    [HarmonyPatch("PhysGrabOverCharge")]
    [HarmonyPrefix]
    private static void GrabOverchargePrefix(PhysGrabber __instance, ref float _amount) {
        var overchargeUpgrade = SLRUpgradePack.OverchargeUpgradeInstance;
        if (overchargeUpgrade.UpgradeEnabled.Value) {
            SLRUpgradePack.Logger.LogDebug($"Original overcharge amount: {_amount}");
            _amount = overchargeUpgrade.Calculate(_amount, __instance.playerAvatar,
                overchargeUpgrade.UpgradeRegister.GetLevel(__instance.playerAvatar));
            SLRUpgradePack.Logger
                .LogDebug(
                    $"After calculation with level {overchargeUpgrade.UpgradeRegister.GetLevel(__instance.playerAvatar)}: {_amount}");
        }
    }

    [HarmonyPatch("PhysGrabOverChargeLogic")]
    [HarmonyPrefix]
    private static void LogicPrefix(PhysGrabber __instance, float ___physGrabBeamOverChargeFloat, out float __state) {
        var overchargeUpgrade = SLRUpgradePack.OverchargeUpgradeInstance;
        __state = ___physGrabBeamOverChargeFloat;
    }

    [HarmonyPatch("PhysGrabOverChargeLogic")]
    [HarmonyPostfix]
    private static void LogicPostfix(PhysGrabber __instance, ref float __state,
        ref float ___physGrabBeamOverChargeFloat, ref byte ___physGrabBeamOverCharge) {
        if (__state > 0f && __state > ___physGrabBeamOverChargeFloat) {
            SLRUpgradePack.Logger.LogInfo("Applying cooldown boost");
            var overchargeUpgrade = SLRUpgradePack.OverchargeUpgradeInstance;
            float original = 0.1f * Time.deltaTime;
            float extra = overchargeUpgrade.Calculate(original, __instance.playerAvatar,
                overchargeUpgrade.UpgradeRegister.GetLevel(__instance.playerAvatar)) - original;
            ___physGrabBeamOverChargeFloat = Math.Max(0, ___physGrabBeamOverChargeFloat - extra);
            ___physGrabBeamOverCharge = (byte)((double)___physGrabBeamOverChargeFloat * 200.0);
        }
    }
}
