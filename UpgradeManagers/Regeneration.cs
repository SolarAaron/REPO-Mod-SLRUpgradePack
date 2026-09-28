using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class RegenerationUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> BaseHealing { get; protected set; }
    internal static Dictionary<string, PlayerAvatar> RegeneratingPlayers { get; } = new();
    internal static Dictionary<string, float> pendingHealings { get; } = new();
    private static Dictionary<string, int> inFlightHealings { get; } = new();
    private static HashSet<string> disabledWhileInFlight { get; } = new();
    private static readonly FieldRef<PlayerHealth, PlayerAvatar> _playerAvatarRef = FieldRefAccess<PlayerHealth, PlayerAvatar>("playerAvatar");
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");
    private readonly FieldRef<PlayerHealth, int> _maxHealthRef = FieldRefAccess<PlayerHealth, int>("maxHealth");
    private readonly FieldRef<PlayerAvatar, bool> _isDisabledRef = FieldRefAccess<PlayerAvatar, bool>("isDisabled");
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

        RemoveMissingPlayers();

        RegeneratingPlayers[SemiFunc.PlayerGetSteamID(player)] = player;

        if (!SLRUpgradePack.Instance.Actions.ContainsKey("Regeneration"))
            SLRUpgradePack.Instance.Actions.Add("Regeneration", RegenerationUpdateAction);
    }

    private void RemoveMissingPlayers() {
        foreach (var missingPlayer in RegeneratingPlayers.Keys.Where(x => !UpgradeRegister.PlayerDictionary.ContainsKey(x)).ToList()) {
            RegeneratingPlayers.Remove(missingPlayer);
            pendingHealings.Remove(missingPlayer);
            inFlightHealings.Remove(missingPlayer);
            disabledWhileInFlight.Remove(missingPlayer);
        }
    }

    private void RegenerationUpdateAction() {
        RemoveMissingPlayers();

        foreach (var player in RegeneratingPlayers.Values.ToList()) {
            if (!player || !player.playerHealth) continue;

            var playerID = SemiFunc.PlayerGetSteamID(player);
            if (!ValuableDirector.instance || !_setupCompleteRef.Invoke(ValuableDirector.instance)) continue;

            var level = UpgradeRegister.GetLevel(player);
            if (!UpgradeEnabled.Value || level <= 0) {
                pendingHealings.Remove(playerID);
                inFlightHealings.Remove(playerID);
                disabledWhileInFlight.Remove(playerID);
                continue;
            }

            // REPO's owner-side Heal() rejects healing while the avatar is disabled. This
            // flag is synchronized by PlayerAvatar, so defer remote heals until the owner
            // can accept them. The queued amount remains available for a later retry.
            if (_isDisabledRef.Invoke(player)) {
                if (inFlightHealings.ContainsKey(playerID)) disabledWhileInFlight.Add(playerID);
                continue;
            }

            // If a request was pending as the remote avatar became disabled, keep it
            // alive during that transition so a late successful health RPC can confirm it.
            // If none arrived by the time the avatar is enabled again, retry the credit.
            if (disabledWhileInFlight.Remove(playerID)) inFlightHealings.Remove(playerID);

            var health = _healthRef.Invoke(player.playerHealth);
            if (health <= 0) continue;

            var maxHealth = _maxHealthRef.Invoke(player.playerHealth);
            if (health >= maxHealth) {
                pendingHealings.Remove(playerID);
                inFlightHealings.Remove(playerID);
                disabledWhileInFlight.Remove(playerID);
                continue;
            }

            pendingHealings.TryGetValue(playerID, out var pendingHealing);
            if (float.IsNaN(pendingHealing) || float.IsInfinity(pendingHealing) || pendingHealing < 0f)
                pendingHealing = 0f;
            var missingHealth = maxHealth - health;
            var healingThisFrame = Calculate(BaseHealing.Value * Time.deltaTime, player, level);
            if (float.IsNaN(healingThisFrame) || healingThisFrame <= 0f) continue;
            if (float.IsInfinity(healingThisFrame)) healingThisFrame = missingHealth;

            pendingHealing = Mathf.Min(pendingHealing + healingThisFrame, missingHealth);
            pendingHealings[playerID] = pendingHealing;

            if (pendingHealing < 1f || inFlightHealings.ContainsKey(playerID)) continue;

            var healAmount = (int)Math.Min(Math.Floor(pendingHealing), missingHealth);
            if (healAmount <= 0) continue;

            // Keep the credit until the health update arrives from the owner. For the
            // locally owned player HealOther applies synchronously, so confirm that path
            // immediately after the call as well.
            var healthBeforeHeal = health;
            inFlightHealings[playerID] = healAmount;
            player.playerHealth.HealOther(healAmount, false);

            var healthAfterHeal = _healthRef.Invoke(player.playerHealth);
            if (healthAfterHeal > healthBeforeHeal)
                ConfirmHealing(player.playerHealth, healthBeforeHeal, healthAfterHeal);
        }
    }

    internal static void ConfirmHealing(PlayerHealth playerHealth, int previousHealth, int updatedHealth) {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !playerHealth || updatedHealth <= previousHealth) return;

        var playerAvatar = _playerAvatarRef.Invoke(playerHealth);
        if (!playerAvatar) return;

        var playerID = SemiFunc.PlayerGetSteamID(playerAvatar);
        if (!inFlightHealings.TryGetValue(playerID, out var requestedHealing)) return;

        inFlightHealings.Remove(playerID);
        disabledWhileInFlight.Remove(playerID);
        if (!pendingHealings.TryGetValue(playerID, out var pendingHealing)) return;

        var confirmedHealing = Math.Min(requestedHealing, updatedHealth - previousHealth);
        pendingHealings[playerID] = Math.Max(0f, pendingHealing - confirmedHealing);
    }
}

[HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.UpdateHealthRPC))]
internal static class RegenerationHealthSyncPatch {
    private static readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");

    private static void Prefix(PlayerHealth __instance, out int __state) {
        __state = _healthRef.Invoke(__instance);
    }

    private static void Postfix(PlayerHealth __instance, int healthNew, int __state) {
        var currentHealth = _healthRef.Invoke(__instance);
        if (currentHealth == healthNew && healthNew > __state)
            RegenerationUpgrade.ConfirmHealing(__instance, __state, healthNew);
    }
}
