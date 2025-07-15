using System;
using BepInEx.Configuration;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class RegenerationComponent : MonoBehaviour {
    internal PlayerAvatar player;
    private float pendingHealing = 0;
    private FieldRef<PlayerHealth, int>? _healthRef = FieldRefAccess<PlayerHealth, int>("health");

    private void Update() {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return; // host handles all calculation

        var regenerationUpgrade = SLRUpgradePack.RegenerationUpgradeInstance;

        if (!ValuableDirector.instance.setupComplete) return;

        if (!regenerationUpgrade.UpgradeEnabled.Value || regenerationUpgrade.UpgradeRegister.GetLevel(player) == 0 || _healthRef.Invoke(player.playerHealth) == 0) return;

        pendingHealing +=
            regenerationUpgrade.Calculate(regenerationUpgrade.BaseHealing.Value * Time.deltaTime, player,
                                          regenerationUpgrade.UpgradeRegister.GetLevel(player));

        if (pendingHealing >= 1) {
            player.playerHealth.HealOther((int)Math.Floor(pendingHealing), false);
            pendingHealing -= Mathf.Floor(pendingHealing);
        }
    }
}

public class RegenerationUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> BaseHealing { get; protected set; }

    public RegenerationUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                               ConfigFile config, AssetBundle assetBundle, float baseHealing, float priceMultiplier) : base("Regeneration", "assets/repo/mods/resources/items/items/item upgrade regeneration.asset", enabled, upgradeAmount,
                                                                                                                            exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000, true, false) {
        BaseHealing = config.Bind("Regeneration Upgrade", "Base Healing", baseHealing, new ConfigDescription("Base Healing Amount", new AcceptableValueRange<float>(0f, 10f)));
    }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "Regeneration", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        if (!player.TryGetComponent<RegenerationComponent>(out var regenerationComponent)) {
            regenerationComponent = player.gameObject.AddComponent<RegenerationComponent>();
            regenerationComponent.player = player;
        }
    }
}