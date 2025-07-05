using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class RegenerationUpgrade: UpgradeBase<float> {
    public ConfigEntry<float> BaseHealing { get; protected set; }
    internal float PendingHealing = 0;
    
    public RegenerationUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                               ConfigFile config, AssetBundle assetBundle, float baseHealing, float priceMultiplier): base("Regeneration", "assets/repo/mods/resources/items/items/item upgrade regeneration.asset", enabled, upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000, true, false) {
        BaseHealing = config.Bind("Regeneration Upgrade", "Base Healing", baseHealing, new ConfigDescription("Base Healing Amount", new AcceptableValueRange<float>(0f, 10f)) );
    }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "Regeneration", value, player, level);

    protected override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        PendingHealing = 0;
        PlayerHealthRecoveryPatch.user = player;
    }
}

[HarmonyPatch(typeof(PlayerHealth), "Update")]
[HarmonyWrapSafe]
public class PlayerHealthRecoveryPatch {
    internal static PlayerAvatar user;
    private static void Postfix(PlayerHealth __instance, PlayerAvatar ___playerAvatar) {
        if (user == null || ___playerAvatar != user) return; // regenerate current player only
        
        var healthRef = FieldRefAccess<PlayerHealth, int>("health");
        var regenerationUpgrade = SLRUpgradePack.RegenerationUpgradeInstance;
        
        if (!ValuableDirector.instance.setupComplete) return;
        
        if(!regenerationUpgrade.UpgradeEnabled.Value || regenerationUpgrade.UpgradeLevel == 0 || healthRef.Invoke(__instance) == 0) return;

        regenerationUpgrade.PendingHealing +=
            regenerationUpgrade.Calculate(regenerationUpgrade.BaseHealing.Value * Time.deltaTime, ___playerAvatar,
                                          regenerationUpgrade.UpgradeLevel);
        if(regenerationUpgrade.PendingHealing >= 1) {
            __instance.HealOther((int)Math.Floor(regenerationUpgrade.PendingHealing), false);
            regenerationUpgrade.PendingHealing -= Mathf.Floor(regenerationUpgrade.PendingHealing);
        }
    }
}
