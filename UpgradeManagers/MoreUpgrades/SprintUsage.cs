using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers.MoreUpgrades;

public class SprintUsageComponent : MonoBehaviour {
    private void FixedUpdate() {
        var sprintUsageUpgrade = SLRUpgradePack.SprintUsageUpgradeInstance;
        if (PlayerController.instance != null && sprintUsageUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) != 0 && sprintUsageUpgrade.originalEnergySprintDrain != null) {
            PlayerController.instance.EnergySprintDrain = sprintUsageUpgrade.Calculate(sprintUsageUpgrade.originalEnergySprintDrain.Value, PlayerController.instance.playerAvatarScript,
                                                                                       sprintUsageUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()));
        }
    }

    private void OnDestroy() {
        var sprintUsageUpgrade = SLRUpgradePack.SprintUsageUpgradeInstance;
        if (PlayerController.instance != null && sprintUsageUpgrade.originalEnergySprintDrain != null) PlayerController.instance.EnergySprintDrain = sprintUsageUpgrade.originalEnergySprintDrain.Value;
    }
}

public class SprintUsageUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> ScalingFactor { get; set; }
    internal float? originalEnergySprintDrain;
    private SprintUsageComponent sprintUsageComponent;

    public SprintUsageUpgrade(bool enabled, ConfigFile config, AssetBundle assetBundle, float priceMultiplier, int minPrice, int maxPrice) :
        base("Sprint Usage", "assets/repo/mods/resources/items/items/item upgrade sprint usage.asset", enabled, 0.1f, false, 1.1f, config, assetBundle, priceMultiplier, false, minPrice, maxPrice, false, false) {
        ScalingFactor = config.Bind("Sprint Usage Upgrade", "Scaling Factor", 0.1f, "Formula: energySprintDrain / (1 + (upgradeAmount * scalingFactor))");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) {
        return value / (1f + level * ScalingFactor.Value);
    }

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        if (PlayerController.instance != null)
            originalEnergySprintDrain = PlayerController.instance.EnergySprintDrain;
        if (sprintUsageComponent != null) Object.Destroy(sprintUsageComponent);
        sprintUsageComponent = new GameObject("Sprint Usage Component").AddComponent<SprintUsageComponent>();
    }
}