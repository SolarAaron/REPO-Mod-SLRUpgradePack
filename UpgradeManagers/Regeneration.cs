using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class RegenerationUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> BaseHealing { get; protected set; }
    internal static Dictionary<string, PlayerAvatar> RegeneratingPlayers { get; } = new();
    internal static Dictionary<string, float> pendingHealings { get; } = new();
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");
    private readonly FieldRef<ValuableDirector, bool> _setupCompleteRef = FieldRefAccess<ValuableDirector, bool>("setupComplete");

    public RegenerationUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, float baseHealing, float priceMultiplier) : base("Regeneration", "assets/repo/mods/resources/items/items/item upgrade regeneration lib.asset", enabled, upgradeAmount,
        exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true, ((int?)null)) {
        BaseHealing = config.Bind("Regeneration Upgrade", "Base Healing", baseHealing, new ConfigDescription("Base Healing Amount", new AcceptableValueRange<float>(0.1f, 10f)));
    }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "Regeneration", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return; // host handles all regeneration

        foreach (var missingPlayer in RegeneratingPlayers.Keys.Where(x => !UpgradeRegister.PlayerDictionary.ContainsKey(x)).ToList()) {
            RegeneratingPlayers.Remove(missingPlayer);
            pendingHealings.Remove(missingPlayer);
        }

        RegeneratingPlayers[SemiFunc.PlayerGetSteamID(player)] = player;

        if (!SLRUpgradePack.Instance.Actions.ContainsKey("Regeneration"))
            SLRUpgradePack.Instance.Actions.Add("Regeneration", RegenerationUpdateAction);
    }

    private void RegenerationUpdateAction() {
        foreach (var player in RegeneratingPlayers.Values) {
            var playerID = SemiFunc.PlayerGetSteamID(player);
            if (!_setupCompleteRef.Invoke(ValuableDirector.instance)) continue;

            if (!UpgradeEnabled.Value || UpgradeRegister.GetLevel(player) == 0 || _healthRef.Invoke(player.playerHealth) == 0) continue;

            pendingHealings.TryGetValue(playerID, out var pendingHealing);

            pendingHealing +=
                Calculate(BaseHealing.Value * Time.deltaTime, player,
                    UpgradeRegister.GetLevel(player));

            if (pendingHealing >= 1) {
                player.playerHealth.HealOther((int)Math.Floor(pendingHealing), false);
                pendingHealing -= Mathf.Floor(pendingHealing);
            }

            pendingHealings[playerID] = pendingHealing;
        }
    }
}
