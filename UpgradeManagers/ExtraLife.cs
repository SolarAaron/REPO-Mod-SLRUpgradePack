using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using REPOLib.Modules;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class ExtraLifeUpgrade : UpgradeBase<float> {
    public ConfigEntry<int> RevivePercent { get; protected set; }
    internal Dictionary<string, ExtraLife> ExtraLives { get; set; } = new();
    public static NetworkedEvent ExtraLifeEvent = new NetworkedEvent("Extra Life", ExtraLifeAction);

    private static void ExtraLifeAction(EventData e) {
        var dict = (Dictionary<string, string>) e.CustomData;
        var extraLife = SLRUpgradePack.ExtraLifeUpgradeInstance.ExtraLives[dict["player"]];
        extraLife.SetViewId(int.Parse(dict["viewId"]));
    }

    public ExtraLifeUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                            ConfigFile config, AssetBundle assetBundle, int revivePercent, float priceMultiplier) :
        base("Extra Life", "assets/repo/mods/resources/items/items/item upgrade extra life lib.asset", enabled,
             upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, false, true, ((int?) null)) {
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
    private Coroutine? reviving;
    private PhotonView photonView;
    private FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");
    private FieldRef<PlayerDeathHead, bool> _inExtractionPointRef = FieldRefAccess<PlayerDeathHead, bool>("inExtractionPoint");
    private FieldRef<PlayerDeathHead, PhysGrabObject> _physGrabObjectRef = FieldRefAccess<PlayerDeathHead, PhysGrabObject>("physGrabObject");

    private void Update() {
        if (player != SemiFunc.PlayerAvatarLocal()) return;
        if (!Traverse.Create(RoundDirector.instance).Field("extractionPointsFetched").GetValue<bool>()) return;
        if (SemiFunc.IsMultiplayer() && photonView.ViewID == 0) {
            PhotonNetwork.AllocateViewID(photonView);
            var eventContent = new Dictionary<string, string>();
            eventContent["player"] = SemiFunc.PlayerGetSteamID(player);
            eventContent["viewId"] = photonView.ViewID.ToString();
            ExtraLifeUpgrade.ExtraLifeEvent.RaiseEvent(eventContent, NetworkingEvents.RaiseOthers, SendOptions.SendReliable);
        }

        if (player.playerHealth) {
            var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;

            if (!extraLifeUpgrade.UpgradeEnabled.Value) return;

            if (_healthRef.Invoke(player.playerHealth) == 0 && extraLifeUpgrade.UpgradeRegister.GetLevel(player) > 0 && !_inExtractionPointRef.Invoke(playerHead) && reviving == null) {
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

        if (_inExtractionPointRef.Invoke(playerHead)) {
            reviving = null;
            yield break;
        }

        _physGrabObjectRef.Invoke(playerHead).centerPoint = Vector3.zero;
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
        if (!TryGetComponent<PhotonView>(out photonView)) {
            photonView = gameObject.AddComponent<PhotonView>();
        }
    }

    internal void SetViewId(int id) {
        photonView.ViewID = id;
        photonView.TransferOwnership(player.photonView.Owner);
    }
}