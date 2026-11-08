using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class RegenerationUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> BaseHealing { get; protected set; }
    internal static Dictionary<string, PlayerAvatar> RegeneratingPlayers { get; } = new();
    internal static Dictionary<string, float> pendingHealings { get; } = new();
    private static Dictionary<string, int> inFlightHealings { get; } = new();
    private static HashSet<string> disabledWhileInFlight { get; } = new();
    private static HashSet<string> loggedActivePlayers { get; } = new();
    private static Dictionary<int, string> avatarDiagnostics { get; } = new();
    private static readonly FieldRef<PlayerHealth, PlayerAvatar> _playerAvatarRef = FieldRefAccess<PlayerHealth, PlayerAvatar>("playerAvatar");
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");
    private readonly FieldRef<PlayerHealth, int> _maxHealthRef = FieldRefAccess<PlayerHealth, int>("maxHealth");
    private readonly FieldRef<PlayerAvatar, bool> _isDisabledRef = FieldRefAccess<PlayerAvatar, bool>("isDisabled");
    private readonly FieldRef<PlayerAvatar, bool> _isLocalRef = FieldRefAccess<PlayerAvatar, bool>("isLocal");
    private readonly FieldRef<ValuableDirector, bool> _setupCompleteRef = FieldRefAccess<ValuableDirector, bool>("setupComplete");

    public RegenerationUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, float baseHealing, float priceMultiplier) : base("Regeneration", "assets/repo/mods/resources/items/items/item upgrade regeneration lib.asset", enabled, upgradeAmount,
        exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true, ((int?)null)) {
        BaseHealing = config.Bind("Regeneration Upgrade", "Base Healing", baseHealing, new ConfigDescription("Base Healing Amount", new AcceptableValueRange<float>(0.1f, 10f)));
    }

    public override float Calculate(float value, PlayerAvatar? player, int level) => DefaultCalculateFloatIncrease(this, "Regeneration", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
    }

    internal void UpdateLocalPlayerRegeneration() {
        var upgradeRegister = UpgradeRegister;
        if (!UpgradeEnabled.Value || upgradeRegister == null) {
            ClearHealingState();
            return;
        }

        var multiplayer = SemiFunc.IsMultiplayer();
        PlayerAvatar[] players;
        if (multiplayer) {
            // PlayerAvatarLocal() can point at the primary account avatar even when a
            // spoofed/local-multiplayer avatar is also owned by this peer. Enumerate all
            // owned PhotonViews so each player's own upgrade level drives their healing.
            players = UnityEngine.Object.FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        } else {
            var localPlayer = SemiFunc.PlayerAvatarLocal();
            if (!localPlayer) {
                ClearHealingState();
                return;
            }

            players = new[] { localPlayer };
        }

        var activePlayerIDs = new HashSet<string>();
        var seenAvatarInstances = new HashSet<int>();
        foreach (var player in players) {
            if (!player) continue;

            var playerHealth = player.playerHealth;
            var photonView = player.photonView;
            var steamID = SemiFunc.PlayerGetSteamID(player);
            var hasUpgradeKey = !string.IsNullOrWhiteSpace(steamID);
            var playerID = GetPlayerStateId(player);
            // REPOLib's PlayerAvatar overload forwards its Steam ID to a dictionary
            // lookup. During avatar/Photon initialization that ID can still be null.
            // Skip the lookup until the upgrade register has a usable key.
            var level = hasUpgradeKey ? upgradeRegister.GetLevel(steamID) : -1;
            var isMine = photonView && photonView.IsMine;
            var isLocal = _isLocalRef.Invoke(player);
            var health = playerHealth ? _healthRef.Invoke(playerHealth) : -1;
            var maxHealth = playerHealth ? _maxHealthRef.Invoke(playerHealth) : -1;
            var isDisabled = _isDisabledRef.Invoke(player);
            var worldReady = ValuableDirector.instance && _setupCompleteRef.Invoke(ValuableDirector.instance);
            var ownerActor = photonView && photonView.Owner != null ? photonView.Owner.ActorNumber : 0;
            var localActor = PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
            var instanceID = player.GetInstanceID();
            seenAvatarInstances.Add(instanceID);

            // Emit only when an avatar's relevant state changes. This records why an
            // avatar with a locally-used upgrade may still be excluded by ownership,
            // health initialization, or scene readiness without adding per-frame noise.
            var signature = $"{hasUpgradeKey}|{level}|{isLocal}|{(bool)photonView}|{(photonView ? photonView.ViewID : 0)}|{isMine}|{ownerActor}|{(bool)playerHealth}|{(health < maxHealth)}|{isDisabled}|{worldReady}";
            if (!avatarDiagnostics.TryGetValue(instanceID, out var previousSignature) || previousSignature != signature) {
                avatarDiagnostics[instanceID] = signature;
                SLRUpgradePack.Logger.LogInfo(
                    $"Regeneration scan: player={SemiFunc.PlayerGetName(player)}, level={level}, local={isLocal}, " +
                    $"view={(photonView ? photonView.ViewID : 0)}, mine={isMine}, ownerActor={ownerActor}, localActor={localActor}, " +
                    $"health={(playerHealth ? $"{health}/{maxHealth}" : "uninitialized")}, upgradeKeyReady={hasUpgradeKey}, disabled={isDisabled}, worldReady={worldReady}.");
            }

            if (!hasUpgradeKey) continue;

            // REPO's health API only accepts a heal from the player's owner. A client may
            // have stale/zero levels for remote players, so process only views it owns.
            if (multiplayer && (!photonView || !isMine)) continue;

            activePlayerIDs.Add(playerID);
            RegeneratingPlayers[playerID] = player;
            if (!playerHealth) continue;

            UpdatePlayerRegeneration(player, playerID, level);
        }

        foreach (var oldPlayerID in RegeneratingPlayers.Keys.Where(id => !activePlayerIDs.Contains(id)).ToList()) {
            RegeneratingPlayers.Remove(oldPlayerID);
            pendingHealings.Remove(oldPlayerID);
            inFlightHealings.Remove(oldPlayerID);
            disabledWhileInFlight.Remove(oldPlayerID);
            loggedActivePlayers.Remove(oldPlayerID);
        }

        foreach (var staleInstanceID in avatarDiagnostics.Keys.Where(id => !seenAvatarInstances.Contains(id)).ToList())
            avatarDiagnostics.Remove(staleInstanceID);
    }

    private void UpdatePlayerRegeneration(PlayerAvatar player, string playerID, int level) {
        // ValuableDirector.setupComplete is host-authoritative in multiplayer and can
        // remain false on a client whose locally-owned avatar is already active. The
        // owning client has the authoritative health state, so don't gate its healing
        // on that host-only scene flag.
        if (level <= 0) {
            pendingHealings.Remove(playerID);
            inFlightHealings.Remove(playerID);
            disabledWhileInFlight.Remove(playerID);
            return;
        }

        var health = _healthRef.Invoke(player.playerHealth);
        var maxHealth = _maxHealthRef.Invoke(player.playerHealth);
        if (loggedActivePlayers.Add(playerID))
            SLRUpgradePack.Logger.LogInfo(
                $"Regeneration active for owned player {SemiFunc.PlayerGetName(player)}: level={level}, health={health}/{maxHealth}, state={playerID}.");

        // REPO's owner-side Heal() rejects healing while the avatar is disabled. Defer
        // until the player is able to accept the heal, retaining any fractional credit.
        if (_isDisabledRef.Invoke(player)) {
            if (inFlightHealings.ContainsKey(playerID)) disabledWhileInFlight.Add(playerID);
            return;
        }

        // If a request was pending as the avatar became disabled, retry that credit once
        // the owner is enabled again.
        if (disabledWhileInFlight.Remove(playerID)) inFlightHealings.Remove(playerID);

        if (health <= 0) return;
        if (health >= maxHealth) {
            pendingHealings.Remove(playerID);
            inFlightHealings.Remove(playerID);
            disabledWhileInFlight.Remove(playerID);
            return;
        }

        pendingHealings.TryGetValue(playerID, out var pendingHealing);
        if (float.IsNaN(pendingHealing) || float.IsInfinity(pendingHealing) || pendingHealing < 0f)
            pendingHealing = 0f;
        var missingHealth = maxHealth - health;
        var healingThisFrame = Calculate(BaseHealing.Value * Time.deltaTime, player, level);
        if (float.IsNaN(healingThisFrame) || healingThisFrame <= 0f) return;
        if (float.IsInfinity(healingThisFrame)) healingThisFrame = missingHealth;

        pendingHealing = Mathf.Min(pendingHealing + healingThisFrame, missingHealth);
        pendingHealings[playerID] = pendingHealing;

        if (pendingHealing < 1f || inFlightHealings.ContainsKey(playerID)) return;

        var healAmount = (int)Math.Min(Math.Floor(pendingHealing), missingHealth);
        if (healAmount <= 0) return;

        // This runs on the owning client, so HealOther takes its local Heal() path and
        // broadcasts the resulting health through REPO's normal health RPC.
        var healthBeforeHeal = health;
        inFlightHealings[playerID] = healAmount;
        player.playerHealth.HealOther(healAmount, false);

        var healthAfterHeal = _healthRef.Invoke(player.playerHealth);
        if (healthAfterHeal > healthBeforeHeal) {
            SLRUpgradePack.Logger.LogDebug(
                $"Regeneration healed owned player {SemiFunc.PlayerGetName(player)}: {healthBeforeHeal} -> {healthAfterHeal} (level={level}).");
            ConfirmHealing(player.playerHealth, healthBeforeHeal, healthAfterHeal);
        }
    }

    private static void ClearHealingState() {
        RegeneratingPlayers.Clear();
        pendingHealings.Clear();
        inFlightHealings.Clear();
        disabledWhileInFlight.Clear();
        loggedActivePlayers.Clear();
        avatarDiagnostics.Clear();
    }

    private static string GetPlayerStateId(PlayerAvatar player) {
        var photonView = player.photonView;
        return photonView ? $"view:{photonView.ViewID}" : SemiFunc.PlayerGetSteamID(player);
    }

    internal static void ConfirmHealing(PlayerHealth playerHealth, int previousHealth, int updatedHealth) {
        if (!playerHealth || updatedHealth <= previousHealth) return;

        var playerAvatar = _playerAvatarRef.Invoke(playerHealth);
        if (!playerAvatar) return;
        if (SemiFunc.IsMultiplayer() && (!playerAvatar.photonView || !playerAvatar.photonView.IsMine)) return;

        var playerID = GetPlayerStateId(playerAvatar);
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
