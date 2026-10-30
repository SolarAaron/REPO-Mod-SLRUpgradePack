using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class ExtraLifeUpgrade : UpgradeBase<float> {
    public ConfigEntry<int> RevivePercent { get; protected set; }
    private readonly Dictionary<string, Coroutine?> _reviveCoroutines = new();

    private static readonly FieldRef<PlayerHealth, int>? MaxHealthRef = FieldRefAccess<PlayerHealth, int>("maxHealth");
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");
    private static readonly FieldRef<PlayerHealth, PlayerAvatar> _playerAvatarRef =
        FieldRefAccess<PlayerHealth, PlayerAvatar>("playerAvatar");

    private static readonly FieldRef<PlayerAvatar, bool> _deadSetRef = FieldRefAccess<PlayerAvatar, bool>("deadSet");

    private readonly FieldRef<PlayerDeathHead, bool> _inExtractionPointRef =
        FieldRefAccess<PlayerDeathHead, bool>("inExtractionPoint");

    private readonly FieldRef<PlayerDeathHead, bool> _deathHeadTriggeredRef =
        FieldRefAccess<PlayerDeathHead, bool>("triggered");

    private readonly FieldRef<PlayerDeathHead, RoomVolumeCheck> _deathHeadRoomVolumeCheckRef =
        FieldRefAccess<PlayerDeathHead, RoomVolumeCheck>("roomVolumeCheck");

    private readonly FieldRef<PlayerAvatar, RoomVolumeCheck> _playerRoomVolumeCheckRef =
        FieldRefAccess<PlayerAvatar, RoomVolumeCheck>("RoomVolumeCheck");

    private readonly FieldRef<RoomVolumeCheck, bool> _roomVolumeInExtractionPointRef =
        FieldRefAccess<RoomVolumeCheck, bool>("inExtractionPoint");

    private readonly FieldRef<RoundDirector, ExtractionPoint> _currentExtractionPointRef =
        FieldRefAccess<RoundDirector, ExtractionPoint>("extractionPointCurrent");

    private readonly FieldRef<ExtractionPoint, ExtractionPoint.State> _extractionPointStateRef =
        FieldRefAccess<ExtractionPoint, ExtractionPoint.State>("currentState");

    private readonly FieldRef<PlayerDeathHead, PhysGrabObject> _physGrabObjectRef =
        FieldRefAccess<PlayerDeathHead, PhysGrabObject>("physGrabObject");

    private static readonly FieldRef<PlayerAvatar, PlayerDeathHead> _playerDeathHeadRef = FieldRefAccess<PlayerAvatar, PlayerDeathHead>("playerDeathHead");

    private static readonly FieldRef<RoundDirector, bool> _extractionPointsFetchedRef =
        FieldRefAccess<RoundDirector, bool>("extractionPointsFetched");

    private static readonly FieldRef<PlayerAvatar, PlayerTumble> _playerTumbleRef =
        FieldRefAccess<PlayerAvatar, PlayerTumble>("tumble");

    private static readonly FieldRef<PlayerTumble, PhysGrabObject> _tumblePhysGrabObjectRef =
        FieldRefAccess<PlayerTumble, PhysGrabObject>("physGrabObject");

    private static readonly FieldRef<PlayerTumble, bool> _isTumblingRef =
        FieldRefAccess<PlayerTumble, bool>("isTumbling");

    private static readonly Vector3 ExtraLifeRespawnPosition = new(0f, 0.5f, 0f);

    public ExtraLifeUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, int revivePercent, float priceMultiplier) :
        base("Extra Life", "assets/repo/mods/resources/items/items/item upgrade extra life lib.asset", enabled,
            upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, false, true,
            ((int?)null)) {
        RevivePercent = config.Bind("Extra Life Upgrade", "revivePercent", revivePercent,
            "Percentage of health to recover when revived");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatIncrease(this, "ExtraLife", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return; // host handles all revivals

        // Starting levels can be initialized before PlayerAvatar.playerDeathHead is assigned.
        // Register the host-side check now and resolve active avatars each tick instead of
        // permanently skipping players whose death-head component is not ready yet.
        SLRUpgradePack.Instance.Actions["Extra Life"] = ExtraLifeUpdateAction;
    }

    private void ExtraLifeUpdateAction() {
        if (!UpgradeEnabled.Value || UpgradeRegister == null || !RoundDirector.instance ||
            !_extractionPointsFetchedRef.Invoke(RoundDirector.instance)) return;

        // Use the local avatar in single player and resolve registered Steam IDs in multiplayer.
        // Re-evaluating this each tick also handles avatars instantiated after initialization.
        foreach (var (player, level) in GetActivePlayerLevels())
            TryScheduleRevive(player, level);
    }

    internal void OnPlayerDeath(PlayerAvatar player) => OnPlayerDeathObserved(player, "PlayerDeathRPC");

    internal void OnPlayerHurt(PlayerHealth health) {
        if (!health) return;
        var player = _playerAvatarRef.Invoke(health);
        if (!player || !_deadSetRef.Invoke(player) || _healthRef.Invoke(health) != 0) return;

        OnPlayerDeathObserved(player, "PlayerHealth.Hurt");
    }

    private void OnPlayerDeathObserved(PlayerAvatar player, string source) {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !player || !player.playerHealth ||
            !UpgradeEnabled.Value || UpgradeRegister == null) return;

        var level = UpgradeRegister.GetLevel(player);
        SLRUpgradePack.Logger.LogInfo(
            $"Extra Life observed {source} for {SemiFunc.PlayerGetName(player)}: level={level}, health={_healthRef.Invoke(player.playerHealth)}, dead={_deadSetRef.Invoke(player)}.");
        TryScheduleRevive(player, level);
    }

    private void TryScheduleRevive(PlayerAvatar player, int level) {
        if (level <= 0 || !player || !player.playerHealth || !RoundDirector.instance ||
            !_extractionPointsFetchedRef.Invoke(RoundDirector.instance) || !_deadSetRef.Invoke(player) ||
            _healthRef.Invoke(player.playerHealth) != 0) return;

        var playerHead = _playerDeathHeadRef(player);
        if (!playerHead || IsExtractionReviveBlocked(player, playerHead)) return;

        var playerID = SemiFunc.PlayerGetSteamID(player);
        var exists = _reviveCoroutines.TryGetValue(playerID, out var reviving);
        if (!exists || reviving == null)
            _reviveCoroutines[playerID] = SLRUpgradePack.Instance.StartCoroutine(BeginReviving(player));
    }

    private IEnumerator BeginReviving(PlayerAvatar player) {
        var playerID = SemiFunc.PlayerGetSteamID(player);
        try {
            yield return new WaitForEndOfFrame();
            // Extraction's dead-head trigger/zone state can be established just after the death RPC.
            // Let that state settle before spending a life that the extraction point can replace for free.
            yield return new WaitForSeconds(0.2f);
            if (!player || !player.playerHealth || !UpgradeEnabled.Value || UpgradeRegister.GetLevel(player) == 0 ||
                !_deadSetRef.Invoke(player) || _healthRef.Invoke(player.playerHealth) != 0)
                yield break;

            var playerHead = _playerDeathHeadRef(player);
            if (!playerHead) yield break;

            SLRUpgradePack.Logger.LogInfo($"Preparing to revive {SemiFunc.PlayerGetName(player)}");
            if (IsExtractionReviveBlocked(player, playerHead)) {
                SLRUpgradePack.Logger.LogInfo(
                    $"Deferring Extra Life for {SemiFunc.PlayerGetName(player)} because the extraction point can revive this player");
                yield break;
            }

            if (!ReviveLogic(player)) yield break;

            // Revive and health changes are sent through REPO's player RPCs. Keep this player marked as
            // reviving until the revive state is replicated; a remote health replica can remain at zero
            // briefly even after the player has already been revived.
            while (player && player.playerHealth && _deadSetRef.Invoke(player) &&
                   _healthRef.Invoke(player.playerHealth) == 0)
                yield return null;
        }
        finally {
            _reviveCoroutines.Remove(playerID);
        }
    }

    private bool IsExtractionReviveBlocked(PlayerAvatar player, PlayerDeathHead playerHead) {
        if (!playerHead) return false;

        // A living avatar and its dead head use different RoomVolumeChecks. Test the avatar first so
        // a lethal hit inside the extraction zone is recognized before PlayerDeathHead.Trigger runs,
        // then test the triggered head using the same condition the game's extraction revive uses.
        var playerRoomVolumeCheck = player ? _playerRoomVolumeCheckRef(player) : null;
        var isInExtraction = false;
        if (playerRoomVolumeCheck) {
            playerRoomVolumeCheck.CheckSet();
            isInExtraction = _roomVolumeInExtractionPointRef.Invoke(playerRoomVolumeCheck);
        }

        if (_deathHeadTriggeredRef.Invoke(playerHead)) {
            var deathHeadRoomVolumeCheck = _deathHeadRoomVolumeCheckRef(playerHead);
            if (deathHeadRoomVolumeCheck) {
                deathHeadRoomVolumeCheck.CheckSet();
                isInExtraction |= _roomVolumeInExtractionPointRef.Invoke(deathHeadRoomVolumeCheck);
            } else {
                isInExtraction |= _inExtractionPointRef.Invoke(playerHead);
            }
        }

        if (!isInExtraction || !RoundDirector.instance) return false;

        // Do not suppress Extra Life just because a player died in an idle extraction zone. Defer
        // only once the current point has entered a successful/active extraction sequence, where
        // the game's DestroyAllPhysObjectsInHaulList path will perform its own free revive.
        var extractionPoint = _currentExtractionPointRef(RoundDirector.instance);
        if (!extractionPoint) return false;

        var state = _extractionPointStateRef(extractionPoint);
        return state == ExtractionPoint.State.Success || state == ExtractionPoint.State.Warning ||
               state == ExtractionPoint.State.Extracting || state == ExtractionPoint.State.Complete;
    }

    private bool ReviveLogic(PlayerAvatar player) {
        var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
        if (extraLifeUpgrade?.UpgradeRegister == null || !player || !player.playerHealth) return false;

        var playerHead = _playerDeathHeadRef(player);
        var headPhysObject = playerHead ? _physGrabObjectRef.Invoke(playerHead) : null;
        if (headPhysObject)
            // ReviveRPC subtracts 0.25 from centerPoint. Keep its intermediate placement at
            // the intended map-start location instead of below world zero.
            headPhysObject.centerPoint = ExtraLifeRespawnPosition + Vector3.up * 0.25f;

        var tumble = _playerTumbleRef(player);
        if (tumble && _isTumblingRef.Invoke(tumble))
            // TumbleSet also clears the tumble rigidbody's velocity and synchronizes the state.
            tumble.TumbleSet(false, false);

        try {
            player.Revive(false);
        }
        catch (Exception exception) {
            SLRUpgradePack.Logger.LogError(
                $"Extra Life could not revive {SemiFunc.PlayerGetName(player)}; the life was not consumed. {exception}");
            return false;
        }

        // The normal revive derives position from each peer's death-head replica. Use the game's
        // spawn path afterward so every client ends at the same safe point near origin. SpawnRPC
        // moves the avatar/controller rigidbody but does not move PlayerTumble's physics object;
        // that object can therefore pull the revived player back to the death location next frame.
        player.Spawn(ExtraLifeRespawnPosition, Quaternion.identity);

        var tumblePhysObject = tumble ? _tumblePhysGrabObjectRef.Invoke(tumble) : null;
        if (tumblePhysObject)
            tumblePhysObject.Teleport(ExtraLifeRespawnPosition, Quaternion.identity);

        // In single player the revive RPC is synchronous. Avoid charging a life if the game did not
        // actually clear the dead state or restore health. Multiplayer remains authoritative on the
        // host and spends after sending the revive, as before.
        if (!SemiFunc.IsMultiplayer() &&
            (_deadSetRef.Invoke(player) || _healthRef.Invoke(player.playerHealth) <= 0)) {
            SLRUpgradePack.Logger.LogWarning(
                $"Extra Life revive did not complete for {SemiFunc.PlayerGetName(player)}; the life was not consumed.");
            return false;
        }

        var maxHealth = MaxHealthRef?.Invoke(player.playerHealth) ?? 0;
        player.playerHealth.HealOther(
            Mathf.FloorToInt(maxHealth * (extraLifeUpgrade.RevivePercent.Value / 100f)), true);

        // Spend exactly one level through REPOLib's level setter so the resulting level is written
        // and broadcast through the same path used for purchases and starting levels.
        var playerSteamId = SemiFunc.PlayerGetSteamID(player);
        var levelBefore = extraLifeUpgrade.UpgradeRegister.GetLevel(player);
        var steamIdLevelBefore = extraLifeUpgrade.UpgradeRegister.GetLevel(playerSteamId);
        var targetLevel = Math.Max(0, levelBefore - 1);
        extraLifeUpgrade.UpgradeRegister.SetLevel(player, targetLevel);
        var levelAfter = extraLifeUpgrade.UpgradeRegister.GetLevel(player);
        var steamIdLevelAfter = extraLifeUpgrade.UpgradeRegister.GetLevel(playerSteamId);
        if (levelAfter != targetLevel || steamIdLevelAfter != targetLevel) {
            SLRUpgradePack.Logger.LogError(
                $"Extra Life level deduction mismatch for {SemiFunc.PlayerGetName(player)} ({playerSteamId}): before={levelBefore}, steamIdBefore={steamIdLevelBefore}, target={targetLevel}, after={levelAfter}, steamIdAfter={steamIdLevelAfter}.");
        }

        var playerPosition = player.transform.position;
        var tumblePosition = tumblePhysObject ? tumblePhysObject.transform.position : playerPosition;
        SLRUpgradePack.Logger.LogInfo(
            $"Extra Life used for {SemiFunc.PlayerGetName(player)}: level {levelBefore} -> {levelAfter}; respawn requested at {ExtraLifeRespawnPosition}, player={playerPosition}, tumble={tumblePosition}.");
        return true;
    }
}

[HarmonyPatch(typeof(PlayerAvatar), "PlayerDeathRPC")]
internal static class ExtraLifePlayerDeathPatch {
    private static void Postfix(PlayerAvatar __instance) {
        SLRUpgradePack.ExtraLifeUpgradeInstance?.OnPlayerDeath(__instance);
    }
}

[HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Hurt))]
internal static class ExtraLifePlayerHurtPatch {
    private static void Postfix(PlayerHealth __instance) {
        SLRUpgradePack.ExtraLifeUpgradeInstance?.OnPlayerHurt(__instance);
    }
}

[HarmonyPatch(typeof(PlayerAvatar), "ReviveRPC")]
internal static class ReviveSpectateCameraNullGuardPatch {
    private static readonly FieldRef<PlayerAvatar, bool> DeadSetRef =
        FieldRefAccess<PlayerAvatar, bool>("deadSet");
    private static readonly FieldRef<PlayerAvatar, bool>? IsLocalRef =
        FieldRefAccess<PlayerAvatar, bool>("isLocal");

    [HarmonyPostfix]
    private static void Postfix(PlayerAvatar __instance) {
        if (!__instance || IsLocalRef?.Invoke(__instance) != true) return;

        // The death/spectate UI can keep hiding Aim for the frame in which ReviveRPC switches
        // back to gameplay. Restore its default state after those late-frame hide calls have ended.
        SLRUpgradePack.Instance?.StartCoroutine(RestoreLocalAimAfterRevive(__instance!));
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var safeStopSpectate = AccessTools.Method(typeof(ReviveSpectateCameraNullGuardPatch),
            nameof(StopSpectateIfAvailable));
        var patched = false;

        foreach (var instruction in instructions) {
            if (!patched && safeStopSpectate != null && instruction.opcode == OpCodes.Callvirt &&
                instruction.operand is MethodInfo calledMethod &&
                calledMethod.DeclaringType == typeof(SpectateCamera) &&
                calledMethod.Name == nameof(SpectateCamera.StopSpectate)) {
                // The updated single-player revive path may have no SpectateCamera singleton.
                // Keep the argument on the stack, but route through a null-safe instance call.
                instruction.opcode = OpCodes.Call;
                instruction.operand = safeStopSpectate;
                patched = true;
            }

            yield return instruction;
        }

        if (!patched)
            SLRUpgradePack.Logger.LogError(
                "Could not install the null-safe SpectateCamera guard in PlayerAvatar.ReviveRPC; Extra Life revives may fail.");
    }

    private static void StopSpectateIfAvailable(SpectateCamera camera) {
        if (camera) camera.StopSpectate();
    }

    private static IEnumerator RestoreLocalAimAfterRevive(PlayerAvatar player) {
        yield return new WaitForSecondsRealtime(0.4f);

        if (!player || IsLocalRef?.Invoke(player) != true || DeadSetRef.Invoke(player) || !GameDirector.instance ||
            GameDirector.instance.currentState != GameDirector.gameState.Main)
            yield break;

        var aim = Aim.instance;
        if (aim) aim.SetState(Aim.State.Default);
    }
}
