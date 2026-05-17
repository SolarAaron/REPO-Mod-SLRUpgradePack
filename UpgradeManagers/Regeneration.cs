using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class RegenerationComponent : MonoBehaviour {
    internal PlayerAvatar player;
    private float pendingHealing = 0;
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");

    private void Start() {
        SLRUpgradePack.Logger.LogInfo($"{SemiFunc.PlayerGetName(player)} is regenerating");
    }

    private void Update() {
        var regenerationUpgrade = SLRUpgradePack.RegenerationUpgradeInstance;

        if (!ValuableDirector.instance.setupComplete) return;

        if (!regenerationUpgrade.UpgradeEnabled.Value || regenerationUpgrade.UpgradeRegister.GetLevel(player) == 0 || _healthRef.Invoke(player.playerHealth) == 0) return;

        pendingHealing +=
            regenerationUpgrade.Calculate(regenerationUpgrade.BaseHealing.Value * Time.deltaTime, player,
                regenerationUpgrade.UpgradeRegister.GetLevel(player));

        if (pendingHealing >= 1) {
            player.playerHealth.Heal((int)Math.Floor(pendingHealing), false);
            pendingHealing -= Mathf.Floor(pendingHealing);
        }
    }
}

public class RegenerationUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> BaseHealing { get; protected set; }
    internal Dictionary<string, RegenerationComponent> Regenerations { get; set; } = new();

    public RegenerationUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, float baseHealing, float priceMultiplier) : base("Regeneration", "assets/repo/mods/resources/items/items/item upgrade regeneration lib.asset", enabled, upgradeAmount,
        exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true, ((int?)null)) {
        BaseHealing = config.Bind("Regeneration Upgrade", "Base Healing", baseHealing, new ConfigDescription("Base Healing Amount", new AcceptableValueRange<float>(0.1f, 10f)));
    }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "Regeneration", value, player, level);
}

[HarmonyPatch(typeof(LevelGenerator))]
public class LevelGeneratorRegenerationPatch {
    [HarmonyPatch("GenerateDone")]
    [HarmonyPostfix]
    private static void GenerateDonePostfix() {
        var regenerationUpgradeInstance = SLRUpgradePack.RegenerationUpgradeInstance;
        var player = PlayerController.instance.playerAvatarScript;
        if (player == null) return;
        SLRUpgradePack.Logger.LogInfo($"Adding regeneration component to {SemiFunc.PlayerGetName(player)} ({SemiFunc.PlayerGetSteamID(player)})");
        if (regenerationUpgradeInstance.Regenerations.TryGetValue(SemiFunc.PlayerGetSteamID(player), out var regenerationComponent) && regenerationComponent != null) Object.Destroy(regenerationComponent);

        regenerationComponent = player.gameObject.AddComponent<RegenerationComponent>();
        regenerationComponent.player = player;
        regenerationUpgradeInstance.Regenerations[SemiFunc.PlayerGetSteamID(player)] = regenerationComponent;
    }
}
