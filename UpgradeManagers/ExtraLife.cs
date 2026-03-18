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

public class ExtraLifeUpgrade : UpgradeBase<float>
{
    public ConfigEntry<int> RevivePercent { get; protected set; }
    internal Dictionary<string, ExtraLife> ExtraLives { get; set; } = new();
    public static NetworkedEvent ExtraLifeEvent = new NetworkedEvent("Extra Life", ExtraLifeAction);

    private static void ExtraLifeAction(EventData e)
    {
        var data = (NetworkMessage)e.CustomData;
        var extraLifeUpgradeInstance = SLRUpgradePack.ExtraLifeUpgradeInstance;
        if (!extraLifeUpgradeInstance.ExtraLives.ContainsKey(data.PlayerId!))
        {
            extraLifeUpgradeInstance.InitUpgrade(SemiFunc.PlayerAvatarGetFromSteamID(data.PlayerId),
                extraLifeUpgradeInstance.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarGetFromSteamID(data.PlayerId!)));
        }

        var extraLife = extraLifeUpgradeInstance.ExtraLives[data.PlayerId!];
        extraLife.SetViewId(data.PhotonId!.Value);
    }

    public ExtraLifeUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, int revivePercent, float priceMultiplier) :
        base("Extra Life", "assets/repo/mods/resources/items/items/item upgrade extra life lib.asset", enabled,
            upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, false, true,
            ((int?)null))
    {
        RevivePercent = config.Bind("Extra Life Upgrade", "revivePercent", revivePercent,
            "Percentage of health to recover when revived");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatIncrease(this, "ExtraLife", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level)
    {
        base.InitUpgrade(player, level);

        if (ExtraLives.TryGetValue(SemiFunc.PlayerGetSteamID(player), out var extraLife)) Object.Destroy(extraLife);

        extraLife = new GameObject($"Extra Life: {SemiFunc.PlayerGetName(player)}").AddComponent<ExtraLife>();
        extraLife.player = player;
        extraLife.playerHead = player.playerDeathHead;
        ExtraLives[SemiFunc.PlayerGetSteamID(player)] = extraLife;
    }
}

public class ExtraLife : MonoBehaviour
{
    private static readonly FieldRef<PlayerHealth, int>? MaxHealthRef = FieldRefAccess<PlayerHealth, int>("maxHealth");
    public PlayerDeathHead playerHead { get; set; }
    public PlayerAvatar player { get; set; }
    private Coroutine? reviving;
    private PhotonView photonView;
    private FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");

    private FieldRef<PlayerDeathHead, bool> _inExtractionPointRef =
        FieldRefAccess<PlayerDeathHead, bool>("inExtractionPoint");

    private FieldRef<PlayerDeathHead, PhysGrabObject> _physGrabObjectRef =
        FieldRefAccess<PlayerDeathHead, PhysGrabObject>("physGrabObject");

    private void Update()
    {
        if (player != SemiFunc.PlayerAvatarLocal()) return;
        if (!Traverse.Create(RoundDirector.instance).Field("extractionPointsFetched").GetValue<bool>()) return;
        if (SemiFunc.IsMultiplayer() && photonView.ViewID == 0)
        {
            PhotonNetwork.AllocateViewID(photonView);
            var eventContent = new NetworkMessage
                { PlayerId = SemiFunc.PlayerGetSteamID(player), PhotonId = photonView.ViewID };
            ExtraLifeUpgrade.ExtraLifeEvent.RaiseEvent(eventContent, NetworkingEvents.RaiseOthers,
                SendOptions.SendReliable);
        }

        if (player.playerHealth)
        {
            var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;

            if (!extraLifeUpgrade.UpgradeEnabled.Value) return;

            if (_healthRef.Invoke(player.playerHealth) == 0 && extraLifeUpgrade.UpgradeRegister.GetLevel(player) > 0 &&
                !_inExtractionPointRef.Invoke(playerHead) && reviving == null)
            {
                reviving = StartCoroutine(BeginReviving());
            }
        }
        else
        {
            SLRUpgradePack.Logger.LogInfo(
                $"Extra Life: player {player.name} has no health object (disconnected?), self-destructing!");
            Destroy(this);
        }
    }

    private IEnumerator BeginReviving()
    {
        yield return new WaitForSecondsRealtime(1f);
        SLRUpgradePack.Logger.LogInfo($"Reviving {SemiFunc.PlayerGetName(player)}");

        if (_inExtractionPointRef.Invoke(playerHead))
        {
            reviving = null;
            yield break;
        }

        if (!SemiFunc.IsMultiplayer()) ReviveLogic();
        else photonView.RPC("ReviveLogic", RpcTarget.All);

        reviving = null;
    }

    [PunRPC]
    private void ReviveLogic()
    {
        var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
        _physGrabObjectRef.Invoke(playerHead).centerPoint = Vector3.zero;
        player.Revive(false);
        player.playerHealth.HealOther(
            Mathf.FloorToInt(MaxHealthRef.Invoke(player.playerHealth) * (extraLifeUpgrade.RevivePercent.Value / 100f)),
            true);

        extraLifeUpgrade.UpgradeRegister.RemoveLevel(player);
    }

    private void Start()
    {
        SLRUpgradePack.Logger.LogInfo($"{SemiFunc.PlayerGetName(player)} has obtained extra lives");
        if (!TryGetComponent<PhotonView>(out photonView))
        {
            photonView = gameObject.AddComponent<PhotonView>();
        }
    }

    internal void SetViewId(int id)
    {
        photonView.ViewID = id;
        photonView.TransferOwnership(player.photonView.Owner);
    }
}