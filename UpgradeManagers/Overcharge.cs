using System;
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
    private static bool _compatibilityWarningLogged;
    private static bool _runtimeWarningLogged;

    public static bool Prepare() {
        var compatible = AccessTools.Method(typeof(PhysGrabber), "PhysGrabOverCharge",
                            new[] { typeof(float), typeof(float) }) != null &&
                        AccessTools.Method(typeof(PhysGrabber), "PhysGrabOverChargeLogic", Type.EmptyTypes) != null &&
                        AccessTools.Method(typeof(PhysGrabber), "OverrideGrabRelease",
                            new[] { typeof(int), typeof(float) }) != null &&
                        AccessTools.Field(typeof(PhysGrabber), "physGrabBeamOverChargeFloat") != null &&
                        AccessTools.Field(typeof(PhysGrabber), "physGrabBeamOverCharge") != null;

        if (!compatible && !_compatibilityWarningLogged) {
            _compatibilityWarningLogged = true;
            SLRUpgradePack.Logger.LogWarning(
                "Skipping Overcharge patches because the current PhysGrabber methods or fields no longer match the expected game API.");
        }

        return compatible;
    }

    private static bool TryGetUpgradeLevel(PhysGrabber instance, out OverchargeUpgrade upgrade, out int level) {
        upgrade = SLRUpgradePack.OverchargeUpgradeInstance;
        level = 0;
        if (!instance || !instance.playerAvatar || upgrade == null || !upgrade.UpgradeEnabled.Value ||
            upgrade.UpgradeRegister == null) return false;

        try {
            level = upgrade.UpgradeRegister.GetLevel(instance.playerAvatar);
            return level > 0;
        }
        catch (Exception exception) {
            if (!_runtimeWarningLogged) {
                _runtimeWarningLogged = true;
                SLRUpgradePack.Logger.LogWarning(
                    $"Overcharge could not read the player's upgrade level; vanilla grab behavior will be preserved. {exception}");
            }

            return false;
        }
    }

    [HarmonyPatch("PhysGrabOverCharge")]
    [HarmonyPrefix]
    private static void GrabOverchargePrefix(PhysGrabber __instance, ref float _amount, ref float _multiplier) {
        try {
            if (!TryGetUpgradeLevel(__instance, out var overchargeUpgrade, out var level)) return;

            SLRUpgradePack.Logger.LogDebug($"Original overcharge amount: {_amount}");
            var amount = overchargeUpgrade.Calculate(_amount, __instance.playerAvatar, level);
            var multiplier = overchargeUpgrade.Calculate(_multiplier, __instance.playerAvatar, level);
            if (float.IsNaN(amount) || float.IsInfinity(amount) || float.IsNaN(multiplier) ||
                float.IsInfinity(multiplier)) return;

            _amount = amount;
            _multiplier = multiplier;
            SLRUpgradePack.Logger
                .LogDebug(
                    $"After calculation with level {level}: {_amount} (initial boost multiplier {_multiplier})");
        }
        catch (Exception exception) {
            LogRuntimeWarning("Overcharge amount adjustment failed; vanilla overcharge behavior will be preserved.",
                exception);
        }
    }

    [HarmonyPatch("PhysGrabOverChargeLogic")]
    [HarmonyPrefix]
    private static void LogicPrefix(PhysGrabber __instance, float ___physGrabBeamOverChargeFloat, out float __state) {
        __state = ___physGrabBeamOverChargeFloat;
    }

    [HarmonyPatch("PhysGrabOverChargeLogic")]
    [HarmonyPostfix]
    private static void LogicPostfix(PhysGrabber __instance, ref float __state,
        ref float ___physGrabBeamOverChargeFloat, ref byte ___physGrabBeamOverCharge) {
        if (__state <= 0f || __state <= ___physGrabBeamOverChargeFloat ||
            !TryGetUpgradeLevel(__instance, out var overchargeUpgrade, out var level)) return;

        try {
            float original = 0.1f * Time.deltaTime;
            float extra = UpgradeBase<float>.DefaultCalculateFloatIncrease(overchargeUpgrade, "Overcharge", original,
                __instance.playerAvatar, level) - original;
            if (float.IsNaN(extra) || float.IsInfinity(extra)) return;

            ___physGrabBeamOverChargeFloat = Math.Max(0, ___physGrabBeamOverChargeFloat - extra);
            ___physGrabBeamOverCharge = (byte)Mathf.Clamp(___physGrabBeamOverChargeFloat * 200.0f, 0f, 255f);
        }
        catch (Exception exception) {
            LogRuntimeWarning("Overcharge decay adjustment failed; vanilla grab behavior will be preserved.", exception);
        }
    }

    [HarmonyPatch("OverrideGrabRelease")]
    [HarmonyPrefix]
    private static void GrabCooldownPrefix(PhysGrabber __instance, ref float _disableTimer) {
        try {
            if (_disableTimer <= 0f ||
                !TryGetUpgradeLevel(__instance, out var overchargeUpgrade, out var level)) return;

            var reducedCooldown = overchargeUpgrade.Calculate(_disableTimer, __instance.playerAvatar, level);
            // Keep malformed config values from lengthening the cooldown or making it negative.
            if (!float.IsNaN(reducedCooldown) && !float.IsInfinity(reducedCooldown))
                _disableTimer = Math.Min(_disableTimer, Math.Max(0f, reducedCooldown));
        }
        catch (Exception exception) {
            LogRuntimeWarning("Overcharge release adjustment failed; vanilla release behavior will be preserved.",
                exception);
        }
    }

    private static void LogRuntimeWarning(string message, Exception exception) {
        if (_runtimeWarningLogged) return;

        _runtimeWarningLogged = true;
        SLRUpgradePack.Logger.LogWarning($"{message} {exception}");
    }
}
