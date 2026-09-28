using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class ExtraLifeUpgrade : UpgradeBase<float> {
    public ConfigEntry<int> RevivePercent { get; protected set; }
    private readonly Dictionary<string, Coroutine?> _reviveCoroutines = new();

    private static readonly FieldRef<PlayerHealth, int>? MaxHealthRef = FieldRefAccess<PlayerHealth, int>("maxHealth");
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");

    private static readonly FieldRef<PlayerAvatar, bool> _deadSetRef = FieldRefAccess<PlayerAvatar, bool>("deadSet");

    private readonly FieldRef<PlayerDeathHead, bool> _inExtractionPointRef =
        FieldRefAccess<PlayerDeathHead, bool>("inExtractionPoint");

    private readonly FieldRef<PlayerDeathHead, PhysGrabObject> _physGrabObjectRef =
        FieldRefAccess<PlayerDeathHead, PhysGrabObject>("physGrabObject");

    private static readonly FieldRef<PlayerAvatar, PlayerDeathHead> _playerDeathHeadRef = FieldRefAccess<PlayerAvatar, PlayerDeathHead>("playerDeathHead");

    private static readonly FieldRef<RoundDirector, bool> _extractionPointsFetchedRef =
        FieldRefAccess<RoundDirector, bool>("extractionPointsFetched");

    private static readonly FieldRef<PlayerAvatar, PlayerTumble> _playerTumbleRef =
        FieldRefAccess<PlayerAvatar, PlayerTumble>("tumble");

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
        foreach (var (player, level) in GetActivePlayerLevels()) {
            if (level <= 0 || !player || !player.playerHealth) continue;

            var playerID = SemiFunc.PlayerGetSteamID(player);
            var playerHead = _playerDeathHeadRef(player);
            if (!playerHead) continue;

            var exists = _reviveCoroutines.TryGetValue(playerID, out var reviving);
            if (_deadSetRef.Invoke(player) && _healthRef.Invoke(player.playerHealth) == 0 &&
                !IsExtractionReviveBlocked(playerHead) && (!exists || reviving == null)) {
                _reviveCoroutines[playerID] = SLRUpgradePack.Instance.StartCoroutine(BeginReviving(player));
            }
        }
    }

    private IEnumerator BeginReviving(PlayerAvatar player) {
        var playerID = SemiFunc.PlayerGetSteamID(player);
        try {
            yield return new WaitForEndOfFrame();
            if (!player || !player.playerHealth || !UpgradeEnabled.Value || UpgradeRegister.GetLevel(player) == 0 ||
                !_deadSetRef.Invoke(player) || _healthRef.Invoke(player.playerHealth) != 0)
                yield break;

            var playerHead = _playerDeathHeadRef(player);
            if (!playerHead) yield break;

            SLRUpgradePack.Logger.LogInfo($"Preparing to revive {SemiFunc.PlayerGetName(player)}");
            if (IsExtractionReviveBlocked(playerHead)) {
                SLRUpgradePack.Logger.LogInfo("Delaying extra life because the extraction point can revive this dead head");
                yield break;
            }

            ReviveLogic(player);

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

    private bool IsExtractionReviveBlocked(PlayerDeathHead playerHead) {
        if (!playerHead) return false;

        // Be conservative while the dead head is inside an extraction point. Its triggered flag
        // can change before extraction cleanup runs, so don't let that transient state start a
        // competing extra-life revive. Check this both before scheduling and after the end-of-frame wait.
        return _inExtractionPointRef.Invoke(playerHead);
    }

    private void ReviveLogic(PlayerAvatar player) {
        var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
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
        catch { }

        // The normal revive derives position from each peer's death-head replica. Use the game's
        // networked spawn path afterward so every client ends at the same safe point near origin.
        player.Spawn(ExtraLifeRespawnPosition, Quaternion.identity);

        player.playerHealth.HealOther(
            Mathf.FloorToInt(MaxHealthRef.Invoke(player.playerHealth) * (extraLifeUpgrade.RevivePercent.Value / 100f)),
            true);

        // This runs on the master. For another player's avatar HealOther schedules a Photon RPC, so the
        // master's health replica is still zero here. Spend the life when the authoritative revive is sent.
        extraLifeUpgrade.UpgradeRegister.RemoveLevel(player);
    }
}
