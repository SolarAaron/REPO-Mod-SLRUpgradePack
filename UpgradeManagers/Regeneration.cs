using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class RegenerationUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> BaseHealing { get; protected set; }
    internal string BoundPlayer { get; set; }

    public RegenerationUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, float baseHealing, float priceMultiplier) : base("Regeneration", "assets/repo/mods/resources/items/items/item upgrade regeneration lib.asset", enabled, upgradeAmount,
        exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true, ((int?)null)) {
        BaseHealing = config.Bind("Regeneration Upgrade", "Base Healing", baseHealing, new ConfigDescription("Base Healing Amount", new AcceptableValueRange<float>(0.1f, 10f)));
    }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "Regeneration", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        if (player == SemiFunc.PlayerAvatarLocal()) {
            BoundPlayer = SemiFunc.PlayerGetSteamID(player);
            SLRUpgradePack.Logger.LogInfo($"{SemiFunc.PlayerGetName(player)} has regeneration level {level}");
        }
    }
}

[HarmonyPatch(typeof(PlayerHealth), "Update")]
[HarmonyWrapSafe]
public class PlayerHealthRecoveryPatch {
    private static float pendingHealing = 0;
    private static readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");

    private static void Postfix(PlayerHealth __instance, PlayerAvatar ___playerAvatar) {
        var regenerationUpgrade = SLRUpgradePack.RegenerationUpgradeInstance;

        if (!SemiFunc.PlayerGetSteamID(___playerAvatar).Equals(regenerationUpgrade.BoundPlayer)) return;

        if (!ValuableDirector.instance.setupComplete) return;

        if (!regenerationUpgrade.UpgradeEnabled.Value || regenerationUpgrade.UpgradeRegister.GetLevel(___playerAvatar) == 0 || _healthRef.Invoke(___playerAvatar.playerHealth) == 0) return;

        pendingHealing +=
            regenerationUpgrade.Calculate(regenerationUpgrade.BaseHealing.Value * Time.deltaTime, ___playerAvatar,
                regenerationUpgrade.UpgradeRegister.GetLevel(___playerAvatar));

        if (pendingHealing >= 1) {
            ___playerAvatar.playerHealth.HealOther((int)Math.Floor(pendingHealing), false);
            pendingHealing -= Mathf.Floor(pendingHealing);
        }
    }
}
