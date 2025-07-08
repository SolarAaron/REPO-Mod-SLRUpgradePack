using System.Collections;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using Photon.Pun;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class HeartOfGoldUpgrade : UpgradeBase<float> {
    public ConfigEntry<float> BaseHeartValue { get; protected set; }

    public HeartOfGoldUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                              ConfigFile config, AssetBundle assetBundle, float baseValue, float priceMultiplier) :
        base("Heart Of Gold", "assets/repo/mods/resources/items/items/item upgrade heart of gold.asset", enabled,
             upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000, true, false) {
        BaseHeartValue =
            config.Bind("Heart Of Gold Upgrade", "Base Value", baseValue, "Base value to scale by player health");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatIncrease(this, "HeartOfGold", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);

        if (!player.healthGrab.gameObject.TryGetComponent<GoldenHeart>(out var heart)) {
            heart = player.healthGrab.gameObject.AddComponent<GoldenHeart>();
            heart.PlayerAvatarInstance = player;
            heart.gameObject.AddComponent<PhotonView>();
        }
    }
}

public class GoldenHeart : MonoBehaviour {
    internal PlayerAvatar PlayerAvatarInstance { get; set; }
    internal bool Pause { get; set; }
    internal int lastLevel { get; set; } = -1;
    internal int lastHealth { get; set; } = -1;
    private PhotonView photonView;

    private void Start() {
        photonView = gameObject.GetComponent<PhotonView>();
    }

    public void DestroyOnlyMe() {
        if (SemiFunc.IsMasterClientOrSingleplayer()) DestroyOnlyMeRPC();
        else photonView.RPC("DestroyOnlyMeRPC", RpcTarget.All);
    }

    [PunRPC]
    private void DestroyOnlyMeRPC() {
        SLRUpgradePack.Logger.LogInfo($"Components in GoldenHeart: {string.Join(',', (IEnumerable)GetComponents<Object>())}");
        RoundDirector.instance.PhysGrabObjectRemove(GetComponent<ValuableObject>().GetComponent<PhysGrabObject>());
        Destroy(GetComponent<ValuableObject>());
    }

    public void CreateOnlyMe() {
        if (SemiFunc.IsMasterClientOrSingleplayer()) CreateOnlyMeRPC();
        else photonView.RPC("CreateOnlyMeRPC", RpcTarget.All);
    }

    [PunRPC]
    private void CreateOnlyMeRPC() {
        var healthRef = FieldRefAccess<PlayerHealth, int>("health");
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        SLRUpgradePack.Logger.LogInfo($"Adding valuable component to player {SemiFunc.PlayerGetName(PlayerAvatarInstance)}");

        var valuableObject = gameObject.AddComponent<ValuableObject>();

        SLRUpgradePack.Logger.LogInfo($"Valuable component {SemiFunc.PlayerGetName(PlayerAvatarInstance)} instantiated at {JsonConvert.SerializeObject(valuableObject.transform)}");

        valuableObject.valuePreset = ScriptableObject.CreateInstance<Value>();
        valuableObject.valuePreset.valueMin =
            valuableObject.valuePreset.valueMax =
                heartOfGoldUpgrade.Calculate(healthRef.Invoke(PlayerAvatarInstance.playerHealth) * heartOfGoldUpgrade.BaseHeartValue.Value,
                                             PlayerAvatarInstance, heartOfGoldUpgrade.UpgradeRegister.GetLevel(PlayerAvatarInstance));

        valuableObject.durabilityPreset = ScriptableObject.CreateInstance<Durability>();
        valuableObject.durabilityPreset.durability = 999;
        valuableObject.durabilityPreset.fragility = 0;
        valuableObject.physAttributePreset = ScriptableObject.CreateInstance<PhysAttribute>();
        valuableObject.transform.localScale = Vector3.zero;
        valuableObject.transform.SetParent(PlayerAvatarInstance.transform, false);
        valuableObject.gameObject.AddComponent<Rigidbody>();

        var ptv = valuableObject.gameObject.AddComponent<PhotonTransformView>();
        var pgoid = valuableObject.gameObject.AddComponent<PhysGrabObjectImpactDetector>();
        var pgo = valuableObject.gameObject.AddComponent<PhysGrabObject>();
        var pv = valuableObject.gameObject.AddComponent<PhotonView>();

        pv.ObservedComponents = [pgo, pgoid, ptv];
        pgoid.enabled = false;
        pgo.enabled = false;
        pgo.transform.localScale = Vector3.zero;
        pgo.transform.SetParent(valuableObject.transform, false);
        pgoid.transform.localScale = Vector3.zero;
        pgoid.transform.SetParent(valuableObject.transform, false);

        var rvc = valuableObject.gameObject.AddComponent<RoomVolumeCheck>();
        rvc.CurrentRooms = [];

        valuableObject.particleColors = new Gradient {
                                                         alphaKeys = [new GradientAlphaKey(1, 0)],
                                                         colorKeys = [new GradientColorKey(Color.yellow, 0)]
                                                     };

        var physAttributePresetRef = FieldRefAccess<ValuableObject, PhysAttribute>("physAttributePreset");
        physAttributePresetRef.Invoke(valuableObject) = ScriptableObject.CreateInstance<PhysAttribute>();

        lastHealth = lastLevel = -1;
    }

    private void Update() {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        if (!Traverse.Create(RoundDirector.instance).Field("extractionPointsFetched").GetValue<bool>()) return;

        var healthRef = FieldRefAccess<PlayerHealth, int>("health");
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (!heartOfGoldUpgrade.UpgradeEnabled.Value) return;
        if (Pause) return;
        if (heartOfGoldUpgrade.UpgradeRegister.GetLevel(PlayerAvatarInstance) == 0 || healthRef.Invoke(PlayerAvatarInstance.playerHealth) == 0) return;
        if (lastLevel == heartOfGoldUpgrade.UpgradeRegister.GetLevel(PlayerAvatarInstance) &&
            lastHealth == healthRef.Invoke(PlayerAvatarInstance.playerHealth)) return;

        lastLevel = heartOfGoldUpgrade.UpgradeRegister.GetLevel(PlayerAvatarInstance);
        lastHealth = healthRef.Invoke(PlayerAvatarInstance.playerHealth);

        if (GetComponent<ValuableObject>() == null || !GetComponent<ValuableObject>()) {
            CreateOnlyMe();
        }

        if (!SemiFunc.IsMultiplayer()) UpdateOnlyMeRPC();
        else photonView.RPC("UpdateOnlyMeRPC", RpcTarget.All);
    }

    [PunRPC]
    private void UpdateOnlyMeRPC() {
        var healthRef = FieldRefAccess<PlayerHealth, int>("health");
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (!FieldRefAccess<ValuableObject, bool>("discovered").Invoke(GetComponent<ValuableObject>()))
            GetComponent<ValuableObject>().Discover(ValuableDiscoverGraphic.State.Discover);

        var dollarValueCurrentRef = FieldRefAccess<ValuableObject, float>("dollarValueCurrent");

        dollarValueCurrentRef.Invoke(GetComponent<ValuableObject>()) =
            heartOfGoldUpgrade.Calculate(healthRef.Invoke(PlayerAvatarInstance.playerHealth) * heartOfGoldUpgrade.BaseHeartValue.Value,
                                         PlayerAvatarInstance, heartOfGoldUpgrade.UpgradeRegister.GetLevel(PlayerAvatarInstance));

        ValuableObjectValuePatch.Action(GetComponent<ValuableObject>());
    }

    public void PauseLogic(bool pause) {
        if (!SemiFunc.IsMultiplayer()) PauseLogicRPC(pause);
        else photonView.RPC("PauseLogicRPC", RpcTarget.All, pause);
    }

    [PunRPC]
    private void PauseLogicRPC(bool pause) {
        Pause = pause;
    }
}

[HarmonyPatch(typeof(ExtractionPoint))]
public class ExrtractionPointdestoryPatch {
    [HarmonyPatch("DestroyAllPhysObjectsInHaulList")]
    [HarmonyPrefix]
    private static bool DestroyAllPrefix() {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !heartOfGoldUpgrade.UpgradeEnabled.Value)
            return true;

        var totalHaulRef = FieldRefAccess<RoundDirector, int>("totalHaul");

        foreach (GameObject dollarHaul in RoundDirector.instance.dollarHaulList) {
            if (dollarHaul && dollarHaul.GetComponent<PhysGrabObject>()) {
                totalHaulRef.Invoke(RoundDirector.instance) += (int)Traverse
                                                                   .Create(dollarHaul.GetComponent<ValuableObject>())
                                                                   .Field("dollarValueCurrent").GetValue<float>();

                if (dollarHaul.TryGetComponent<GoldenHeart>(out var heart)) {
                    SLRUpgradePack.Logger.LogInfo("Player in extraction zone counts as valuable");
                    heart.PauseLogic(true);
                    heart.DestroyOnlyMe();
                } else
                    dollarHaul.GetComponent<PhysGrabObject>().DestroyPhysGrabObject();
            }
        }

        foreach (PlayerAvatar player in GameDirector.instance.PlayerList) {
            player.playerDeathHead.Revive();

            if (player.TryGetComponent<GoldenHeart>(out var heart)) {
                heart.PauseLogic(false);
                heart.lastHealth = heart.lastLevel = -1;
            }
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

        var totalHaulRef = FieldRefAccess<RoundDirector, int>("totalHaul");

        totalHaulRef.Invoke(RoundDirector.instance) += (int)Traverse
                                                           .Create(RoundDirector.instance.dollarHaulList[0]
                                                                                .GetComponent<ValuableObject>())
                                                           .Field("dollarValueCurrent").GetValue<float>();

        if (RoundDirector.instance.dollarHaulList[0].TryGetComponent<GoldenHeart>(out var heart)) {
            SLRUpgradePack.Logger.LogInfo("Player in extraction zone counts as valuable");
            heart.PauseLogic(true);
            heart.DestroyOnlyMe();

            if (RoundDirector.instance.dollarHaulList[0].TryGetComponent<ExtraLife>(out var extraLife)) {
                SLRUpgradePack.Logger.LogInfo("Pausing extra life logic for player in extraction zone");
                extraLife.PauseLogic(true);
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
        __instance.dollarHaulList.RemoveAll(go => go == null || !go);
    }
}