using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class ExtraLifeUpgrade : UpgradeBase<float> {
    public ConfigEntry<int> RevivePercent { get; protected set; }
    internal static Dictionary<string, PlayerAvatar> ExtraLifePlayers { get; } = new();
    private readonly Dictionary<string, Coroutine?> _reviveCoroutines = new();

    private static readonly FieldRef<PlayerHealth, int>? MaxHealthRef = FieldRefAccess<PlayerHealth, int>("maxHealth");
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");

    private readonly FieldRef<PlayerDeathHead, bool> _inExtractionPointRef =
        FieldRefAccess<PlayerDeathHead, bool>("inExtractionPoint");

    private readonly FieldRef<PlayerDeathHead, PhysGrabObject> _physGrabObjectRef =
        FieldRefAccess<PlayerDeathHead, PhysGrabObject>("physGrabObject");

    private static readonly FieldRef<PlayerAvatar, PlayerDeathHead> _playerDeathHeadRef = FieldRefAccess<PlayerAvatar, PlayerDeathHead>("playerDeathHead");
    private static readonly FieldRef<RoundDirector, bool>? ExtractionPointsFetchedRef = FieldRefAccess<RoundDirector, bool>("extractionPointsFetched");

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

        var playerDeathHead = player.GetComponent<PlayerDeathHead>();
        if (playerDeathHead == null) return;

        foreach (var missingPlayer in ExtraLifePlayers.Keys.Where(x => !UpgradeRegister.PlayerDictionary.ContainsKey(x)).ToList()) {
            ExtraLifePlayers.Remove(missingPlayer);
        }

        ExtraLifePlayers[SemiFunc.PlayerGetSteamID(player)] = player;

        if (!SLRUpgradePack.Instance.Actions.ContainsKey("Extra Life"))
            SLRUpgradePack.Instance.Actions.Add("Extra Life", ExtraLifeUpdateAction);
    }

    private void ExtraLifeUpdateAction() {
        foreach (var player in ExtraLifePlayers.Values) {
            if (!ExtractionPointsFetchedRef.Invoke(RoundDirector.instance)) continue;
            if (player.playerHealth) {
                var playerID = SemiFunc.PlayerGetSteamID(player);
                var playerHead = _playerDeathHeadRef(player);

                if (!UpgradeEnabled.Value) continue;

                var exists = _reviveCoroutines.TryGetValue(playerID, out var reviving);

                if (_healthRef.Invoke(player.playerHealth) == 0 && UpgradeRegister.GetLevel(player) > 0 && !_inExtractionPointRef.Invoke(playerHead) && (!exists || reviving == null)) {
                    reviving = SLRUpgradePack.Instance.StartCoroutine(BeginReviving(player));
                }

                if (reviving != null && _healthRef.Invoke(player.playerHealth) > 0) reviving = null;
            }
        }
    }

    private IEnumerator BeginReviving(PlayerAvatar player) {
        var playerHead = _playerDeathHeadRef(player);
        var playerID = SemiFunc.PlayerGetSteamID(player);
        yield return new WaitForEndOfFrame();
        SLRUpgradePack.Logger.LogInfo($"Preparing to revive {SemiFunc.PlayerGetName(player)}");

        if (_inExtractionPointRef.Invoke(playerHead)) {
            _reviveCoroutines.Remove(playerID);
            SLRUpgradePack.Logger.LogInfo("Not reviving head in extraction point");
            yield break;
        }

        ReviveLogic(player);

        _reviveCoroutines.Remove(playerID);
    }

    private void ReviveLogic(PlayerAvatar player) {
        var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
        var playerHead = _playerDeathHeadRef(player);
        _physGrabObjectRef.Invoke(playerHead).centerPoint = Vector3.zero;
        try {
            player.Revive(false);
        }
        catch { }

        player.playerHealth.HealOther(
            Mathf.FloorToInt(MaxHealthRef.Invoke(player.playerHealth) * (extraLifeUpgrade.RevivePercent.Value / 100f)),
            true);

        if (_healthRef.Invoke(player.playerHealth) > 0)
            extraLifeUpgrade.UpgradeRegister.RemoveLevel(player);
    }
}
