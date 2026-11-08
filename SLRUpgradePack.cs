using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using REPOLib;
using REPOLib.Modules;
using SLRUpgradePack.UpgradeManagers;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Resources = SLRUpgradePack.Properties.Resources;

namespace SLRUpgradePack;

[BepInDependency(MyPluginInfo.PLUGIN_GUID)]
[BepInPlugin("SolarAaron.SLRUpgradePack", "SLRUpgradePack", "0.6.1")]
public class SLRUpgradePack : BaseUnityPlugin {
    private const float StartingLevelRetryInterval = 1f;
    private static readonly FieldRef<PlayerUpgrade, Action<PlayerAvatar, int>> PlayerUpgradeStartActionRef =
        FieldRefAccess<PlayerUpgrade, Action<PlayerAvatar, int>>("StartAction");

    internal Dictionary<string, Action> Actions { get; private set; } = new();
    internal static SLRUpgradePack Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    internal Harmony? Harmony { get; set; }
    private ManualLogSource _logger => base.Logger;
    internal static readonly Dictionary<string, int> LimitedUse = new();
    private readonly HashSet<(string Upgrade, string SteamId)> _initializedStartingLevels = new();
    private readonly HashSet<string> _playersWithInitializedStartingLevels = new();
    private readonly HashSet<string> _startingLevelRequestsSent = new();
    private readonly HashSet<string> _pendingStartingLevelInitializations = new();
    private readonly Dictionary<(string Upgrade, string SteamId), int> _initializedStartActionLevels = new();
    private readonly HashSet<string> _playersWithStartActions = new();
    private bool _activePlayersStartingLevelsInitialized;
    private bool _startingLevelInitializationAllowed = true;
    private float _nextStartingLevelRetryTime;
    private static readonly NetworkedEvent StartingLevelInitializationEvent =
        new("SLR Starting Upgrade Levels", HandleStartingLevelInitializationRequest);

    // Unity initializes these in Awake before the Harmony patches can use them. Overcharge is
    // intentionally optional when the current game API does not support its Harmony patch.
    public static OverchargeUpgrade? OverchargeUpgradeInstance { get; private set; }
    public static ArmorUpgrade ArmorUpgradeInstance { get; private set; } = null!;
    public static ObjectValueUpgrade ObjectValueUpgradeInstance { get; private set; } = null!;
    public static ObjectDurabilityUpgrade ObjectDurabilityUpgradeInstance { get; private set; } = null!;
    public static ValuableDensityUpgrade ValuableDensityUpgradeInstance { get; private set; } = null!;
    public static HeartOfGoldUpgrade HeartOfGoldUpgradeInstance { get; private set; } = null!;
    public static RegenerationUpgrade RegenerationUpgradeInstance { get; private set; } = null!;
    public static ExtraLifeUpgrade ExtraLifeUpgradeInstance { get; private set; } = null!;
    public static InventorySlotUpgrade InventorySlotUpgradeInstance { get; private set; } = null!;
    public static SafeTriggerUpgrade SafeTriggerUpgradeInstance { get; private set; } = null!;

    private void Awake() {
        Instance = this;

        // Prevent the plugin from being deleted
        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = HideFlags.HideAndDontSave;

        _logger.LogInfo("Configuring upgrade pack...");

        var assetBundle = AssetBundle.LoadFromMemory(Resources.slr_assets);
        assetBundle.name = "slr";

        foreach (var assetName in assetBundle.GetAllAssetNames()) {
            _logger.LogInfo($"Found asset: {assetName}");
        }

        if (PhysGrabberPatch.Prepare())
            OverchargeUpgradeInstance = new OverchargeUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 0.75f);
        ArmorUpgradeInstance = new ArmorUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.0f);
        ObjectValueUpgradeInstance = new ObjectValueUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 2.5f);
        ObjectDurabilityUpgradeInstance =
            new ObjectDurabilityUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 2.5f);
        ValuableDensityUpgradeInstance = new ValuableDensityUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 2.5f);
        HeartOfGoldUpgradeInstance = new HeartOfGoldUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 10f, 3.5f);
        RegenerationUpgradeInstance = new RegenerationUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, .1f, 4.5f);
        ExtraLifeUpgradeInstance = new ExtraLifeUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 5, 10f);
        InventorySlotUpgradeInstance = new InventorySlotUpgrade(true, 1, Config, assetBundle, 2.0f);
        SafeTriggerUpgradeInstance = new SafeTriggerUpgrade(true, 0.1f, Config, assetBundle, 3.5f);

        Patch();
        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    internal void Patch() {
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();
    }

    internal void Unpatch() {
        Harmony?.UnpatchSelf();
    }

    private void Update() {
        // Regeneration reads the owning player's local upgrade state and applies healing
        // through the same owner-authoritative health path used by REPO. Other upgrades
        // remain host-managed below.
        RegenerationUpgradeInstance?.UpdateLocalPlayerRegeneration();

        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        if (_pendingStartingLevelInitializations.Count > 0 && Time.unscaledTime >= _nextStartingLevelRetryTime) {
            _nextStartingLevelRetryTime = Time.unscaledTime + StartingLevelRetryInterval;
            foreach (var steamId in new List<string>(_pendingStartingLevelInitializations))
                InitializeStartingLevelsForPlayer(steamId);
        }

        if (!LevelGenerator.Instance || !LevelGenerator.Instance.Generated) return;

        if (!_activePlayersStartingLevelsInitialized)
            _activePlayersStartingLevelsInitialized = InitializeStartingLevelsForActivePlayers();

        // Snapshot in case an action registers or removes another action while running.
        foreach (var action in new List<Action>(Actions.Values))
            action.Invoke();
    }

    internal bool InitializeStartingLevelsForPlayer(string steamId) {
        if (string.IsNullOrWhiteSpace(steamId)) return false;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) {
            RequestStartingLevelInitialization(steamId);
            return false;
        }
        if (!_startingLevelInitializationAllowed) return true;
        if (_playersWithInitializedStartingLevels.Contains(steamId)) {
            _pendingStartingLevelInitializations.Remove(steamId);
            return true;
        }

        var initialized = true;
        var overchargeUpgrade = OverchargeUpgradeInstance;
        if (overchargeUpgrade != null)
            initialized &= InitializeStartingLevel("Overcharge", overchargeUpgrade, steamId);
        initialized &= InitializeStartingLevel("Armor", ArmorUpgradeInstance, steamId);
        initialized &= InitializeStartingLevel("ObjectValue", ObjectValueUpgradeInstance, steamId);
        initialized &= InitializeStartingLevel("ObjectDurability", ObjectDurabilityUpgradeInstance, steamId);
        initialized &= InitializeStartingLevel("ValuableDensity", ValuableDensityUpgradeInstance, steamId);
        initialized &= InitializeStartingLevel("HeartOfGold", HeartOfGoldUpgradeInstance, steamId);
        initialized &= InitializeStartingLevel("Regeneration", RegenerationUpgradeInstance, steamId);
        initialized &= InitializeStartingLevel("ExtraLife", ExtraLifeUpgradeInstance, steamId);
        initialized &= InitializeStartingLevel("InventorySlot", InventorySlotUpgradeInstance, steamId);
        initialized &= InitializeStartingLevel("SafeTrigger", SafeTriggerUpgradeInstance, steamId);

        if (initialized) {
            _playersWithInitializedStartingLevels.Add(steamId);
            _pendingStartingLevelInitializations.Remove(steamId);
        } else {
            _pendingStartingLevelInitializations.Add(steamId);
        }

        return initialized;
    }

    internal bool InitializeStartingLevelsForActivePlayers() {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return false;

        var players = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
        if (players.Length == 0) return false;

        var initialized = true;
        foreach (var player in players) {
            if (!player) continue;
            initialized &= InitializeStartingLevelsForPlayer(GetUpgradePlayerId(player));
        }

        return initialized;
    }

    private static string GetUpgradePlayerId(PlayerAvatar player) {
        // LocalMultiplayer can spoof the local account after the avatar is created. Once the
        // network view has an owner, REPOLib's upgrade dictionary must use that authoritative ID.
        var photonView = player.photonView;
        var networkUserId = photonView && photonView.Owner != null ? photonView.Owner.UserId : null;
        return !string.IsNullOrWhiteSpace(networkUserId) ? networkUserId : SemiFunc.PlayerGetSteamID(player);
    }

    private void RequestStartingLevelInitialization(string steamId) {
        if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.InRoom || string.IsNullOrWhiteSpace(steamId)) return;
        var masterClient = PhotonNetwork.MasterClient;
        if (masterClient == null || !_startingLevelRequestsSent.Add(steamId)) return;

        try {
            StartingLevelInitializationEvent.RaiseEvent(steamId,
                new RaiseEventOptions { TargetActors = [masterClient.ActorNumber] }, SendOptions.SendReliable);
            Logger.LogInfo($"Requested host to apply configured starting levels for player {steamId}.");
        }
        catch (Exception exception) {
            _startingLevelRequestsSent.Remove(steamId);
            Logger.LogError($"Failed to request starting-level initialization for player {steamId}: {exception}");
        }
    }

    private static void HandleStartingLevelInitializationRequest(EventData eventData) {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.IsMultiplayer() ||
            eventData.CustomData is not string requestedSteamId || string.IsNullOrWhiteSpace(requestedSteamId)) return;

        // A client may request initialization only for its own Photon user ID. The host remains
        // the only side that writes levels, and ignores mismatched or stale identity requests.
        Player? requester = null;
        foreach (var networkPlayer in PhotonNetwork.PlayerList) {
            if (networkPlayer.ActorNumber != eventData.Sender) continue;
            requester = networkPlayer;
            break;
        }

        if (requester == null || !string.Equals(requester.UserId, requestedSteamId, StringComparison.Ordinal)) {
            Logger.LogWarning(
                $"Ignoring starting-level request from actor {eventData.Sender} for mismatched player ID '{requestedSteamId}'.");
            return;
        }

        Logger.LogInfo($"Received starting-level initialization request for player {requestedSteamId}.");
        if (Instance != null && !Instance.InitializeStartingLevelsForPlayer(requestedSteamId))
            Logger.LogWarning($"Host could not fully initialize configured starting levels for player {requestedSteamId}; queued for retry.");
    }

    private bool InitializeStartingLevel<T>(string upgradeName, UpgradeBase<T> upgrade, string steamId) {
        if (upgrade == null || !upgrade.UpgradeEnabled.Value) return true;
        if (upgrade.UpgradeRegister == null) return false;

        var startingLevel = upgrade.StartingAmount.Value;
        if (startingLevel <= 0) return true;

        var key = (upgradeName, steamId);
        if (_initializedStartingLevels.Contains(key)) return true;

        try {
            var currentLevel = upgrade.UpgradeRegister.GetLevel(steamId);
            if (currentLevel >= startingLevel) {
                _initializedStartingLevels.Add(key);
                return true;
            }

            Logger.LogInfo(
                $"Applying configured starting level for {upgradeName} to player {steamId}: {currentLevel} -> {startingLevel}");
            // REPOLib's SetLevel updates the save dictionary, invokes the upgrade action, and
            // broadcasts the authoritative level to remote clients.
            upgrade.UpgradeRegister.SetLevel(steamId, startingLevel);
            if (upgrade.UpgradeRegister.GetLevel(steamId) < startingLevel) {
                Logger.LogWarning(
                    $"Configured starting level for {upgradeName} did not persist for player {steamId}; will retry.");
                return false;
            }

            _initializedStartingLevels.Add(key);
            return true;
        }
        catch (Exception exception) {
            // Permit a retry if registration/network state was not ready when the grant ran.
            Logger.LogError($"Failed to initialize {upgradeName} for player {steamId}: {exception}");
            return false;
        }
    }

    internal void RecordPlayerStartActions(string steamId) {
        if (string.IsNullOrWhiteSpace(steamId)) return;

        _playersWithStartActions.Add(steamId);
        foreach (var upgrade in Upgrades.PlayerUpgrades) {
            _initializedStartActionLevels[(upgrade.UpgradeId, steamId)] = upgrade.GetLevel(steamId);
        }
    }

    internal void RepairRemotePlayerStartAction(PlayerUpgrade upgrade, string steamId, int level) {
        if (SemiFunc.IsMasterClientOrSingleplayer() || string.IsNullOrWhiteSpace(steamId) ||
            !_playersWithStartActions.Contains(steamId)) return;

        var startingLevel = GetConfiguredStartingLevel(upgrade);
        var startAction = PlayerUpgradeStartActionRef.Invoke(upgrade);
        if (startingLevel <= 0 || level < startingLevel || startAction == null) return;

        var key = (upgrade.UpgradeId, steamId);
        if (_initializedStartActionLevels.TryGetValue(key, out var initializedLevel) &&
            initializedLevel >= startingLevel) return;

        var player = SemiFunc.PlayerAvatarGetFromSteamID(steamId);
        if (!player) return;

        try {
            // The host's reliable level event can arrive after REPOLib has already invoked
            // this client's start action at level 0. Re-run only that missed initialization
            // with the synchronized level; the host remains the sole writer of upgrade state.
            startAction(player, upgrade.GetLevel(steamId));
            _initializedStartActionLevels[key] = upgrade.GetLevel(steamId);
        }
        catch (Exception exception) {
            Logger.LogError($"Failed to initialize synchronized {upgrade.UpgradeId} for player {steamId}: {exception}");
        }
    }

    private int GetConfiguredStartingLevel(PlayerUpgrade upgrade) {
        var overchargeUpgrade = OverchargeUpgradeInstance;
        if (overchargeUpgrade?.UpgradeRegister == upgrade) return overchargeUpgrade.StartingAmount.Value;
        if (ArmorUpgradeInstance?.UpgradeRegister == upgrade) return ArmorUpgradeInstance.StartingAmount.Value;
        if (ObjectValueUpgradeInstance?.UpgradeRegister == upgrade) return ObjectValueUpgradeInstance.StartingAmount.Value;
        if (ObjectDurabilityUpgradeInstance?.UpgradeRegister == upgrade) return ObjectDurabilityUpgradeInstance.StartingAmount.Value;
        if (ValuableDensityUpgradeInstance?.UpgradeRegister == upgrade) return ValuableDensityUpgradeInstance.StartingAmount.Value;
        if (HeartOfGoldUpgradeInstance?.UpgradeRegister == upgrade) return HeartOfGoldUpgradeInstance.StartingAmount.Value;
        if (RegenerationUpgradeInstance?.UpgradeRegister == upgrade) return RegenerationUpgradeInstance.StartingAmount.Value;
        if (ExtraLifeUpgradeInstance?.UpgradeRegister == upgrade) return ExtraLifeUpgradeInstance.StartingAmount.Value;
        if (InventorySlotUpgradeInstance?.UpgradeRegister == upgrade) return InventorySlotUpgradeInstance.StartingAmount.Value;
        if (SafeTriggerUpgradeInstance?.UpgradeRegister == upgrade) return SafeTriggerUpgradeInstance.StartingAmount.Value;
        return 0;
    }

    internal void ResetStartingLevelInitialization(bool allowStartingLevels = true) {
        _initializedStartingLevels.Clear();
        _playersWithInitializedStartingLevels.Clear();
        _startingLevelRequestsSent.Clear();
        _pendingStartingLevelInitializations.Clear();
        _nextStartingLevelRetryTime = 0f;
        _initializedStartActionLevels.Clear();
        _playersWithStartActions.Clear();
        _activePlayersStartingLevelsInitialized = false;
        _startingLevelInitializationAllowed = allowStartingLevels;
    }

    internal void SuppressStartingLevelInitializationForLoadedSave() {
        ResetStartingLevelInitialization(allowStartingLevels: false);
        Logger.LogInfo("Configured starting upgrade levels are suppressed for the loaded save.");
    }
}

[HarmonyPatch(typeof(MenuPageSaves), "OnNewGame")]
internal static class NewGameStartingUpgradeLevelsPatch {
    private static void Prefix() {
        SLRUpgradePack.Instance.ResetStartingLevelInitialization();
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.ResetProgress))]
internal static class ResetRunStartingUpgradeLevelsPatch {
    private static void Postfix() {
        // Completing/failing an arena can reset StatsManager and begin another run without
        // returning through MenuPageSaves.OnNewGame. Allow configured starting levels to be
        // applied again when that run's LevelGenerator starts.
        if (SLRUpgradePack.Instance != null)
            SLRUpgradePack.Instance.ResetStartingLevelInitialization();
    }
}

[HarmonyPatch(typeof(StatsManager), nameof(StatsManager.LoadGame))]
internal static class LoadedSaveStartingUpgradeLevelsPatch {
    private static void Prefix() {
        if (SLRUpgradePack.Instance != null)
            SLRUpgradePack.Instance.SuppressStartingLevelInitializationForLoadedSave();
    }
}

[HarmonyPatch(typeof(Upgrades), "InvokeStartActions")]
internal static class InitializeStartingUpgradeLevelsPatch {
    private static void Prefix(string steamId) {
        if (SLRUpgradePack.Instance != null)
            SLRUpgradePack.Instance.InitializeStartingLevelsForPlayer(steamId);
    }

    private static void Postfix(string steamId) {
        if (SLRUpgradePack.Instance != null)
            SLRUpgradePack.Instance.RecordPlayerStartActions(steamId);
    }
}

[HarmonyPatch(typeof(PlayerUpgrade), "ApplyUpgrade")]
internal static class RepairRemoteStartingUpgradeActionsPatch {
    private static void Postfix(PlayerUpgrade __instance, string steamId, int level) {
        if (SLRUpgradePack.Instance != null)
            SLRUpgradePack.Instance.RepairRemotePlayerStartAction(__instance, steamId, level);
    }
}

[HarmonyPatch(typeof(LevelGenerator), "Start")]
internal static class LevelGeneratorStartingUpgradeLevelsPatch {
    [HarmonyPriority(Priority.First)]
    private static void Prefix() {
        if (SLRUpgradePack.Instance != null)
            SLRUpgradePack.Instance.InitializeStartingLevelsForActivePlayers();
    }
}
