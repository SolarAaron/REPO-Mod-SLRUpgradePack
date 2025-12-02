using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Newtonsoft.Json;
using Photon.Pun;
using REPOLib.Modules;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class HeartOfGoldUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> BaseHeartValue { get; protected set; }
    internal Dictionary<string, GoldenHeart> GoldenHearts { get; set; } = new();
    public static NetworkedEvent HeartOfGoldEvent = new NetworkedEvent("Heart Of Gold", HeartOfGoldAction);

    private static void HeartOfGoldAction(EventData e) {
        var dict = (Dictionary<string, string>) e.CustomData;
        if (!SLRUpgradePack.HeartOfGoldUpgradeInstance.GoldenHearts.ContainsKey(dict["player"])) {
            SLRUpgradePack.HeartOfGoldUpgradeInstance.InitUpgrade(SemiFunc.PlayerAvatarGetFromSteamID(dict["player"]), 0);
        }
        var heart = SLRUpgradePack.HeartOfGoldUpgradeInstance.GoldenHearts[dict["player"]];
        heart.SetViewId(int.Parse(dict["viewId"]));
    }

    public HeartOfGoldUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                              ConfigFile config, AssetBundle assetBundle, float baseValue, float priceMultiplier) :
        base("Heart Of Gold", "assets/repo/mods/resources/items/items/item upgrade heart of gold lib.asset", enabled,
             upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true, ((int?) null)) {
        BaseHeartValue =
            config.Bind("Heart Of Gold Upgrade", "Base Value", baseValue, "Base value to scale by player health");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatIncrease(this, "HeartOfGold", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);

        if (GoldenHearts.TryGetValue(SemiFunc.PlayerGetSteamID(player), out var goldenHeart)) Object.Destroy(goldenHeart);

        goldenHeart = new GameObject($"Golden Heart: {SemiFunc.PlayerGetName(player)}").AddComponent<GoldenHeart>();
        goldenHeart.player = player;
        GoldenHearts[SemiFunc.PlayerGetSteamID(player)] = goldenHeart;
    }
}

public class GoldenHeart : MonoBehaviour {
    internal PlayerAvatar player { get; set; }
    internal bool Pause { get; set; }
    internal int lastLevel { get; set; } = -1;
    internal int lastHealth { get; set; } = -1;
    internal ValuableObject? ValuableComponent { get; private set; }

    private PhotonView photonView;
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");
    private readonly FieldRef<ValuableObject, PhysAttribute> _physAttributePresetRef = FieldRefAccess<ValuableObject, PhysAttribute>("physAttributePreset");
    private readonly FieldRef<ValuableObject, bool> _discoveredRef = FieldRefAccess<ValuableObject, bool>("discovered");
    private readonly FieldRef<ValuableObject, float> _dollarValueCurrentRef = FieldRefAccess<ValuableObject, float>("dollarValueCurrent");

    private void Start() {
        if (!TryGetComponent<PhotonView>(out photonView)) {
            photonView = gameObject.AddComponent<PhotonView>();
        }

        Pause = false;
    }

    public void DestroyOnlyMe() {
        if (SemiFunc.IsMasterClientOrSingleplayer()) DestroyOnlyMeRPC();
        else photonView.RPC("DestroyOnlyMeRPC", RpcTarget.All);
    }

    [PunRPC]
    private void DestroyOnlyMeRPC() {
        if (ValuableComponent == null) return;

        SLRUpgradePack.Logger.LogInfo($"Components in GoldenHeart: {string.Join(',', GetComponents<Object>().Select(v => v.ToString()))}");
        RoundDirector.instance.PhysGrabObjectRemove(ValuableComponent.GetComponent<PhysGrabObject>());
        Destroy(ValuableComponent);
        ValuableComponent = null;
    }

    public void CreateOnlyMe() {
        if (SemiFunc.IsMasterClientOrSingleplayer()) CreateOnlyMeRPC();
        else photonView.RPC("CreateOnlyMeRPC", RpcTarget.All);
    }

    [PunRPC]
    private void CreateOnlyMeRPC() {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        SLRUpgradePack.Logger.LogInfo($"Adding valuable component to player {SemiFunc.PlayerGetName(player)}");

        ValuableComponent = player.healthGrab.gameObject.AddComponent<ValuableObject>();

        SLRUpgradePack.Logger.LogInfo($"Valuable component {SemiFunc.PlayerGetName(player)} instantiated at {JsonConvert.SerializeObject(ValuableComponent.transform)}");

        ValuableComponent.valuePreset = ScriptableObject.CreateInstance<Value>();
        ValuableComponent.valuePreset.valueMin =
            ValuableComponent.valuePreset.valueMax =
                heartOfGoldUpgrade.Calculate(_healthRef.Invoke(player.playerHealth) * heartOfGoldUpgrade.BaseHeartValue.Value,
                                             player, heartOfGoldUpgrade.UpgradeRegister.GetLevel(player));

        ValuableComponent.durabilityPreset = ScriptableObject.CreateInstance<Durability>();
        ValuableComponent.durabilityPreset.durability = 999;
        ValuableComponent.durabilityPreset.fragility = 0;
        ValuableComponent.physAttributePreset = ScriptableObject.CreateInstance<PhysAttribute>();
        ValuableComponent.transform.localScale = Vector3.zero;
        ValuableComponent.transform.SetParent(player.transform, false);
        ValuableComponent.gameObject.AddComponent<Rigidbody>();

        var ptv = ValuableComponent.gameObject.AddComponent<PhotonTransformView>();
        var pgoid = ValuableComponent.gameObject.AddComponent<PhysGrabObjectImpactDetector>();
        var pgo = ValuableComponent.gameObject.AddComponent<PhysGrabObject>();
        var pv = ValuableComponent.gameObject.AddComponent<PhotonView>();

        pv.ObservedComponents = [pgo, pgoid, ptv];
        pgoid.enabled = false;
        pgo.enabled = false;
        pgo.transform.localScale = Vector3.zero;
        pgo.transform.SetParent(ValuableComponent.transform, false);
        pgoid.transform.localScale = Vector3.zero;
        pgoid.transform.SetParent(ValuableComponent.transform, false);

        var rvc = ValuableComponent.gameObject.AddComponent<RoomVolumeCheck>();
        rvc.CurrentRooms = [];

        ValuableComponent.particleColors = new Gradient {
                                                            alphaKeys = [new GradientAlphaKey(1, 0)],
                                                            colorKeys = [new GradientColorKey(Color.yellow, 0)]
                                                        };

        _physAttributePresetRef.Invoke(ValuableComponent) = ScriptableObject.CreateInstance<PhysAttribute>();

        lastHealth = lastLevel = -1;
    }

    private void Update() {
        if (SemiFunc.PlayerAvatarLocal() != player) return;
        if (!Traverse.Create(RoundDirector.instance).Field("extractionPointsFetched").GetValue<bool>()) return;

        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (!heartOfGoldUpgrade.UpgradeEnabled.Value) return;
        if (Pause) return;
        if (heartOfGoldUpgrade.UpgradeRegister.GetLevel(player) == 0 || _healthRef.Invoke(player.playerHealth) == 0) return;
        if (lastLevel == heartOfGoldUpgrade.UpgradeRegister.GetLevel(player) &&
            lastHealth == _healthRef.Invoke(player.playerHealth)) return;

        if (SemiFunc.IsMultiplayer() && photonView.ViewID == 0) {
            PhotonNetwork.AllocateViewID(photonView);
            var eventContent = new Dictionary<string, string>();
            eventContent["player"] = SemiFunc.PlayerGetSteamID(player);
            eventContent["viewId"] = photonView.ViewID.ToString();
            HeartOfGoldUpgrade.HeartOfGoldEvent.RaiseEvent(eventContent, NetworkingEvents.RaiseOthers, SendOptions.SendReliable);
        }

        lastLevel = heartOfGoldUpgrade.UpgradeRegister.GetLevel(player);
        lastHealth = _healthRef.Invoke(player.playerHealth);

        if (ValuableComponent == null || !ValuableComponent) {
            CreateOnlyMe();
        }

        if (!SemiFunc.IsMultiplayer()) UpdateOnlyMeRPC();
        else photonView.RPC("UpdateOnlyMeRPC", RpcTarget.All);
    }

    [PunRPC]
    private void UpdateOnlyMeRPC() {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (!_discoveredRef.Invoke(ValuableComponent))
            ValuableComponent.Discover(ValuableDiscoverGraphic.State.Discover);

        _dollarValueCurrentRef.Invoke(ValuableComponent) =
            heartOfGoldUpgrade.Calculate(_healthRef.Invoke(player.playerHealth) * heartOfGoldUpgrade.BaseHeartValue.Value,
                                         player, heartOfGoldUpgrade.UpgradeRegister.GetLevel(player));

        ValuableObjectValuePatch.Action(ValuableComponent);
    }

    public void PauseLogic(bool pause) {
        if (!SemiFunc.IsMultiplayer()) PauseLogicRPC(pause);
        else photonView.RPC("PauseLogicRPC", RpcTarget.All, pause);
    }

    [PunRPC]
    private void PauseLogicRPC(bool pause) {
        Pause = pause;
    }

    internal void SetViewId(int id) {
        photonView.ViewID = id;
        photonView.TransferOwnership(player.photonView.Owner);
    }
}

[HarmonyPatch(typeof(ExtractionPoint))]
public class ExtractionPointDestroyPatch {
    private static FieldRef<RoundDirector, int>? _totalHaulRef = FieldRefAccess<RoundDirector, int>("totalHaul");

    [HarmonyPatch("DestroyAllPhysObjectsInHaulList")]
    [HarmonyPrefix]
    private static bool DestroyAllPrefix() {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !heartOfGoldUpgrade.UpgradeEnabled.Value)
            return true;

        foreach (var dollarHaul in RoundDirector.instance.dollarHaulList) {
            if (dollarHaul && dollarHaul.GetComponent<PhysGrabObject>()) {
                _totalHaulRef.Invoke(RoundDirector.instance) += (int) Traverse
                                                                     .Create(dollarHaul.GetComponent<ValuableObject>())
                                                                     .Field("dollarValueCurrent").GetValue<float>();

                if (dollarHaul.TryGetComponent<ValuableObject>(out var valuableObject) && valuableObject.name.Equals("Health Grab")) {
                    foreach (var idHeart in heartOfGoldUpgrade.GoldenHearts)
                        if (idHeart.Value.ValuableComponent == valuableObject) {
                            SLRUpgradePack.Logger.LogInfo($"Player {SemiFunc.PlayerGetName(idHeart.Value.player)} in extraction zone counts as valuable");
                            idHeart.Value.PauseLogic(true);
                            idHeart.Value.DestroyOnlyMe();
                        }
                } else
                    dollarHaul.GetComponent<PhysGrabObject>().DestroyPhysGrabObject();
            }
        }

        foreach (var player in GameDirector.instance.PlayerList) {
            player.playerDeathHead.Revive();
        }

        foreach (var idHeart in heartOfGoldUpgrade.GoldenHearts) {
            idHeart.Value.PauseLogic(false);
        }

        return false;
    }

    [HarmonyPatch("DestroyTheFirstPhysObjectsInHaulList")]
    [HarmonyPrefix]
    private static bool DestroyFirstPrefix() {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (!SemiFunc.IsMasterClientOrSingleplayer() || RoundDirector.instance.dollarHaulList.Count == 0 ||
            !RoundDirector.instance.dollarHaulList[0] ||
            !RoundDirector.instance.dollarHaulList[0].GetComponent<PhysGrabObject>() ||
            !heartOfGoldUpgrade.UpgradeEnabled.Value)
            return true;

        _totalHaulRef.Invoke(RoundDirector.instance) += (int) Traverse
                                                             .Create(RoundDirector.instance.dollarHaulList[0]
                                                                                  .GetComponent<ValuableObject>())
                                                             .Field("dollarValueCurrent").GetValue<float>();

        if (RoundDirector.instance.dollarHaulList[0].TryGetComponent<ValuableObject>(out var valuableObject) && valuableObject.name.Equals("Health Grab")) {
            foreach (var idHeart in heartOfGoldUpgrade.GoldenHearts)
                if (idHeart.Value.ValuableComponent == valuableObject) {
                    SLRUpgradePack.Logger.LogInfo($"Player {SemiFunc.PlayerGetName(idHeart.Value.player)} in extraction zone counts as valuable");
                    idHeart.Value.PauseLogic(true);
                    idHeart.Value.DestroyOnlyMe();
                }
        } else
            RoundDirector.instance.dollarHaulList[0].GetComponent<PhysGrabObject>().DestroyPhysGrabObject();

        RoundDirector.instance.dollarHaulList.RemoveAt(0);

        return false;
    }
}

[HarmonyPatch(typeof(RoundDirector), "Update")]
public class RoundDirectorUpdatePatch {
    private static void Prefix(RoundDirector __instance) {
        __instance.dollarHaulList.RemoveAll(go => go == null || !go.TryGetComponent<ValuableObject>(out var valuableObject) || !valuableObject);
    }
}