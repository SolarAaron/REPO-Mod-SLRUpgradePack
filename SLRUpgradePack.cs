using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using REPOLib;
using REPOLib.Modules;
using SLRUpgradePack.UpgradeManagers;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Resources = SLRUpgradePack.Properties.Resources;

namespace SLRUpgradePack;

[BepInDependency(MyPluginInfo.PLUGIN_GUID)]
[BepInDependency("bulletbot.keybindlib")]
[BepInPlugin("SolarAaron.SLRUpgradePack", "SLRUpgradePack", "0.5.1")]
public class SLRUpgradePack : BaseUnityPlugin {
    private static readonly FieldRef<PlayerUpgrade, Action<PlayerAvatar, int>> PlayerUpgradeStartActionRef =
        FieldRefAccess<PlayerUpgrade, Action<PlayerAvatar, int>>("StartAction");

    internal Dictionary<string, Action> Actions { get; private set; } = new();
    internal static SLRUpgradePack Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    internal Harmony? Harmony { get; set; }
    private ManualLogSource _logger => base.Logger;
    internal static readonly Dictionary<string, int> LimitedUse = new();
    private readonly HashSet<(string Upgrade, string SteamId)> _initializedStartingLevels = new();
    private readonly Dictionary<(string Upgrade, string SteamId), int> _initializedStartActionLevels = new();
    private readonly HashSet<string> _playersWithStartActions = new();

    public static OverchargeUpgrade OverchargeUpgradeInstance { get; private set; }
    public static ArmorUpgrade ArmorUpgradeInstance { get; private set; }
    public static ObjectValueUpgrade ObjectValueUpgradeInstance { get; private set; }
    public static ObjectDurabilityUpgrade ObjectDurabilityUpgradeInstance { get; private set; }
    public static ValuableDensityUpgrade ValuableDensityUpgradeInstance { get; private set; }
    public static HeartOfGoldUpgrade HeartOfGoldUpgradeInstance { get; private set; }
    public static RegenerationUpgrade RegenerationUpgradeInstance { get; private set; }
    public static ExtraLifeUpgrade ExtraLifeUpgradeInstance { get; private set; }
    public static InventorySlotUpgrade InventorySlotUpgradeInstance { get; private set; }

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
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        if (!LevelGenerator.Instance || !LevelGenerator.Instance.Generated) return;

        // Snapshot in case an action registers or removes another action while running.
        foreach (var action in new List<Action>(Actions.Values))
            action.Invoke();
    }

    internal void InitializeStartingLevelsForPlayer(string steamId) {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || string.IsNullOrWhiteSpace(steamId)) return;

        if (PhysGrabberPatch.Prepare())
            InitializeStartingLevel("Overcharge", OverchargeUpgradeInstance, steamId);
        InitializeStartingLevel("Armor", ArmorUpgradeInstance, steamId);
        InitializeStartingLevel("ObjectValue", ObjectValueUpgradeInstance, steamId);
        InitializeStartingLevel("ObjectDurability", ObjectDurabilityUpgradeInstance, steamId);
        InitializeStartingLevel("ValuableDensity", ValuableDensityUpgradeInstance, steamId);
        InitializeStartingLevel("HeartOfGold", HeartOfGoldUpgradeInstance, steamId);
        InitializeStartingLevel("Regeneration", RegenerationUpgradeInstance, steamId);
        InitializeStartingLevel("ExtraLife", ExtraLifeUpgradeInstance, steamId);
        InitializeStartingLevel("InventorySlot", InventorySlotUpgradeInstance, steamId);
    }

    internal void InitializeStartingLevelsForActivePlayers() {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (var player in FindObjectsOfType<PlayerAvatar>()) {
            if (!player) continue;
            InitializeStartingLevelsForPlayer(SemiFunc.PlayerGetSteamID(player));
        }
    }

    private void InitializeStartingLevel<T>(string upgradeName, UpgradeBase<T> upgrade, string steamId) {
        if (upgrade == null || !upgrade.UpgradeEnabled.Value || upgrade.UpgradeRegister == null) return;
        var startingLevel = upgrade.StartingAmount.Value;
        if (startingLevel <= 0) return;

        var key = (upgradeName, steamId);
        if (!_initializedStartingLevels.Add(key)) return;

        try {
            var currentLevel = upgrade.UpgradeRegister.GetLevel(steamId);
            if (currentLevel >= startingLevel) return;

            Logger.LogInfo(
                $"Applying configured starting level for {upgradeName} to player {steamId}: {currentLevel} -> {startingLevel}");
            // REPOLib's SetLevel updates the save dictionary, invokes the upgrade action, and
            // broadcasts the authoritative level to remote clients.
            upgrade.UpgradeRegister.SetLevel(steamId, startingLevel);
        }
        catch (Exception exception) {
            // Permit a retry if registration/network state was not ready when the grant ran.
            _initializedStartingLevels.Remove(key);
            Logger.LogError($"Failed to initialize {upgradeName} for player {steamId}: {exception}");
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
        if (OverchargeUpgradeInstance?.UpgradeRegister == upgrade) return OverchargeUpgradeInstance.StartingAmount.Value;
        if (ArmorUpgradeInstance?.UpgradeRegister == upgrade) return ArmorUpgradeInstance.StartingAmount.Value;
        if (ObjectValueUpgradeInstance?.UpgradeRegister == upgrade) return ObjectValueUpgradeInstance.StartingAmount.Value;
        if (ObjectDurabilityUpgradeInstance?.UpgradeRegister == upgrade) return ObjectDurabilityUpgradeInstance.StartingAmount.Value;
        if (ValuableDensityUpgradeInstance?.UpgradeRegister == upgrade) return ValuableDensityUpgradeInstance.StartingAmount.Value;
        if (HeartOfGoldUpgradeInstance?.UpgradeRegister == upgrade) return HeartOfGoldUpgradeInstance.StartingAmount.Value;
        if (RegenerationUpgradeInstance?.UpgradeRegister == upgrade) return RegenerationUpgradeInstance.StartingAmount.Value;
        if (ExtraLifeUpgradeInstance?.UpgradeRegister == upgrade) return ExtraLifeUpgradeInstance.StartingAmount.Value;
        if (InventorySlotUpgradeInstance?.UpgradeRegister == upgrade) return InventorySlotUpgradeInstance.StartingAmount.Value;
        return 0;
    }

    internal void ResetStartingLevelInitialization() {
        _initializedStartingLevels.Clear();
        _initializedStartActionLevels.Clear();
        _playersWithStartActions.Clear();
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
