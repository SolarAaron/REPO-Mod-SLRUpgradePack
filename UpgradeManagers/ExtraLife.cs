using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class ExtraLifeUpgrade : UpgradeBase<float> {
    public ConfigEntry<int> RevivePercent { get; protected set; }
    internal Dictionary<string, ExtraLife> ExtraLives { get; set; } = new();

    public ExtraLifeUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                            ConfigFile config, AssetBundle assetBundle, int revivePercent, float priceMultiplier) :
        base("Extra Life", "assets/repo/mods/resources/items/items/item upgrade extra life.asset", enabled,
             upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, false, 2000, 100000, true, false) {
        RevivePercent = config.Bind("Extra Life Upgrade", "revivePercent", revivePercent, "Percentage of health to recover when revived");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "ExtraLife", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);

        if (ExtraLives.TryGetValue(SemiFunc.PlayerGetSteamID(player), out var extraLife)) Object.Destroy(extraLife);

        extraLife = new GameObject($"Extra Life: {SemiFunc.PlayerGetName(player)}").AddComponent<ExtraLife>();
        extraLife.player = player;
        extraLife.playerHead = player.playerDeathHead;
        ExtraLives[SemiFunc.PlayerGetSteamID(player)] = extraLife;
    }
}

public class ExtraLife : MonoBehaviour {
    public PlayerDeathHead playerHead { get; set; }
    public PlayerAvatar player { get; set; }
    private bool _isMoving;
    private Coroutine? reviving;
    private PhotonView photonView;
    private FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");
    private FieldRef<PlayerDeathHead, bool> _inExtractionPointRef = FieldRefAccess<PlayerDeathHead, bool>("inExtractionPoint");

    private void Update() {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return; // host handles all calculation

        if (player.playerHealth) {
            var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;

            if (!extraLifeUpgrade.UpgradeEnabled.Value) return;

            if (_healthRef.Invoke(player.playerHealth) == 0 && extraLifeUpgrade.UpgradeRegister.GetLevel(player) > 0 && !_isMoving && !_inExtractionPointRef.Invoke(playerHead) && reviving == null) {
                reviving = StartCoroutine(BeginReviving());
            }
        } else {
            SLRUpgradePack.Logger.LogInfo("why are we here?");
        }
    }

    private IEnumerator BeginReviving() {
        yield return new WaitForSecondsRealtime(1f);
        SLRUpgradePack.Logger.LogInfo($"Reviving {SemiFunc.PlayerGetName(player)}");
        var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
        var maxHealthRef = FieldRefAccess<PlayerHealth, int>("maxHealth");

        while (_isMoving) {
            var deadTimerRef = FieldRefAccess<PlayerAvatar, float>("deadTimer");
            deadTimerRef.Invoke(player) += 1;
            yield return new WaitForSecondsRealtime(0.1f);
            SLRUpgradePack.Logger.LogInfo("Pause attempt to revive moving head");
        }

        if (_inExtractionPointRef.Invoke(playerHead)) {
            reviving = null;
            yield break;
        }

        player.Revive();
        player.playerHealth.HealOther(Mathf.FloorToInt(maxHealthRef.Invoke(player.playerHealth) * extraLifeUpgrade.RevivePercent.Value / 100f), true);

        if (!SemiFunc.IsMultiplayer()) ReviveLogic();
        else photonView.RPC("ReviveLogic", RpcTarget.All);

        reviving = null;
    }

    [PunRPC]
    private void ReviveLogic() {
        var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
        extraLifeUpgrade.UpgradeRegister.RemoveLevel(player);
    }

    private void Start() {
        SLRUpgradePack.Logger.LogInfo($"{SemiFunc.PlayerGetName(player)} has obtained extra lives");
        photonView = gameObject.AddComponent<PhotonView>();
        StartCoroutine(MovementCheck());
    }

    private IEnumerator MovementCheck() {
        while (!playerHead) yield return null;
        while (!playerHead.transform) yield return null;
        Vector3 currentPosition = playerHead.transform.position;
        while (true) {
            yield return new WaitForSecondsRealtime(0.5f);
            Vector3 nextPosition = playerHead.transform.position;
            _isMoving = Vector3.Distance(currentPosition, nextPosition) >= 0.01f;
            SLRUpgradePack.Logger.LogDebug($"{SemiFunc.PlayerGetName(player)} is moving: {_isMoving}");
            currentPosition = nextPosition;
        }
    }
}