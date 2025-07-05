using System;
using BepInEx.Configuration;
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
    public int LastHealth { get; set; }
    public int LastLevel { get; set; }
    public bool Pause { get; set; }

    public HeartOfGoldUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                              ConfigFile config, AssetBundle assetBundle, float baseValue, float priceMultiplier) :
        base("Heart Of Gold", "assets/repo/mods/resources/items/items/item upgrade heart of gold.asset", enabled,
             upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000, true, false) {
        BaseHeartValue =
            config.Bind("Heart Of Gold Upgrade", "Base Value", baseValue, "Base value to scale by player health");
        LastHealth = -1;
        LastLevel = -1;
        Pause = false;
    }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatIncrease(this, "HeartOfGold", value, player, level);

    protected override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        LastHealth = LastLevel = -1;
        Pause = false;
    }
}

[HarmonyPatch(typeof(PlayerHealth), "Update")]
[HarmonyWrapSafe]
public class PlayerHealthAddValuePatch {
    private static void Postfix(PlayerHealth __instance, PlayerAvatar ___playerAvatar) {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        if (!Traverse.Create(RoundDirector.instance).Field("extractionPointsFetched").GetValue<bool>()) return;

        var healthRef = FieldRefAccess<PlayerHealth, int>("health");
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (!heartOfGoldUpgrade.UpgradeEnabled.Value) return;
        if (heartOfGoldUpgrade.Pause) return;
        if (heartOfGoldUpgrade.UpgradeLevel == 0 || healthRef.Invoke(__instance) == 0) return;
        if (heartOfGoldUpgrade.LastLevel == heartOfGoldUpgrade.UpgradeLevel &&
            heartOfGoldUpgrade.LastHealth == healthRef.Invoke(__instance)) return;

        heartOfGoldUpgrade.LastLevel = heartOfGoldUpgrade.UpgradeLevel;
        heartOfGoldUpgrade.LastHealth = healthRef.Invoke(__instance);

        if (!___playerAvatar.healthGrab.TryGetComponent<ValuableObject>(out var valuableComponent) || !valuableComponent || healthRef.Invoke(__instance) <= 0) {
            SLRUpgradePack.Logger.LogInfo($"Adding valuable component to player {SemiFunc.PlayerGetName(___playerAvatar)}");
            
            valuableComponent = ___playerAvatar.healthGrab.gameObject.AddComponent<ValuableObject>();
            
            SLRUpgradePack.Logger.LogInfo($"Valuable component {SemiFunc.PlayerGetName(___playerAvatar)} instantiated at {JsonConvert.SerializeObject(valuableComponent.transform)}");
            
            valuableComponent.valuePreset = ScriptableObject.CreateInstance<Value>();
            valuableComponent.valuePreset.valueMin =
                valuableComponent.valuePreset.valueMax =
                    heartOfGoldUpgrade.Calculate(healthRef.Invoke(__instance) * heartOfGoldUpgrade.BaseHeartValue.Value,
                                                 ___playerAvatar, heartOfGoldUpgrade.UpgradeLevel);

            valuableComponent.durabilityPreset = ScriptableObject.CreateInstance<Durability>();
            valuableComponent.durabilityPreset.durability = 999;
            valuableComponent.durabilityPreset.fragility = 0;
            valuableComponent.physAttributePreset = ScriptableObject.CreateInstance<PhysAttribute>();
            valuableComponent.transform.localScale = Vector3.zero;
            valuableComponent.transform.SetParent(___playerAvatar.transform, false);
            valuableComponent.gameObject.AddComponent<Rigidbody>();

            var ptv = valuableComponent.gameObject.AddComponent<PhotonTransformView>();
            var pgo = valuableComponent.gameObject.AddComponent<PhysGrabObject>();
            var pgoid = valuableComponent.gameObject.AddComponent<PhysGrabObjectImpactDetector>();
            var pv = valuableComponent.gameObject.AddComponent<PhotonView>();

            pv.ObservedComponents = [pgo, pgoid, ptv];
            pgoid.enabled = false;
            pgo.enabled = false;
            pgo.transform.localScale = Vector3.zero;
            pgo.transform.SetParent(valuableComponent.transform, false);
            pgoid.transform.localScale = Vector3.zero;
            pgoid.transform.SetParent(valuableComponent.transform, false);

            var rvc = valuableComponent.gameObject.AddComponent<RoomVolumeCheck>();
            rvc.CurrentRooms = [];

            valuableComponent.particleColors = new Gradient {
                                                                alphaKeys = [new GradientAlphaKey(1, 0)],
                                                                colorKeys = [new GradientColorKey(Color.yellow, 0)]
                                                            };

            var physAttributePresetRef = FieldRefAccess<ValuableObject, PhysAttribute>("physAttributePreset");
            physAttributePresetRef.Invoke(valuableComponent) = ScriptableObject.CreateInstance<PhysAttribute>();
            
            var heart = ___playerAvatar.healthGrab.gameObject.AddComponent<GoldenHeart>();
            heart.ValuableObject = valuableComponent;
            heart.gameObject.AddComponent<PhotonView>();

            heartOfGoldUpgrade.LastHealth = heartOfGoldUpgrade.LastLevel = -1;
        }
        
        if(!FieldRefAccess<ValuableObject, bool>("discovered").Invoke(valuableComponent))
            valuableComponent.Discover(ValuableDiscoverGraphic.State.Discover);

        var dollarValueCurrentRef = FieldRefAccess<ValuableObject, float>("dollarValueCurrent");

        dollarValueCurrentRef.Invoke(valuableComponent) =
            heartOfGoldUpgrade.Calculate(healthRef.Invoke(__instance) * heartOfGoldUpgrade.BaseHeartValue.Value,
                                         ___playerAvatar, heartOfGoldUpgrade.UpgradeLevel);

        ValuableObjectValuePatch.Action(valuableComponent);
    }
}

public class GoldenHeart : MonoBehaviour {
    public ValuableObject ValuableObject { get; set; }
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
        Destroy(ValuableObject);
        Destroy(this);
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

                if (dollarHaul.GetComponent<ValuableObject>().name.Equals("Health Grab") && dollarHaul.TryGetComponent<GoldenHeart>(out var heart)) {
                    SLRUpgradePack.Logger.LogInfo("Player in extraction zone counts as valuable");
                    heart.DestroyOnlyMe();
                } else
                    dollarHaul.GetComponent<PhysGrabObject>().DestroyPhysGrabObject();
            }
        }

        foreach (PlayerAvatar player in GameDirector.instance.PlayerList)
            player.playerDeathHead.Revive();

        heartOfGoldUpgrade.Pause = false;
        heartOfGoldUpgrade.LastHealth = heartOfGoldUpgrade.LastLevel = -1;
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

        heartOfGoldUpgrade.Pause = true;

        var totalHaulRef = FieldRefAccess<RoundDirector, int>("totalHaul");

        totalHaulRef.Invoke(RoundDirector.instance) += (int)Traverse
                                                           .Create(RoundDirector.instance.dollarHaulList[0]
                                                                                .GetComponent<ValuableObject>())
                                                           .Field("dollarValueCurrent").GetValue<float>();

        if (RoundDirector.instance.dollarHaulList[0].GetComponent<ValuableObject>().name.Equals("Health Grab") && RoundDirector.instance.dollarHaulList[0].TryGetComponent<GoldenHeart>(out var heart)) {
            SLRUpgradePack.Logger.LogInfo("Player in extraction zone counts as valuable");
            heart.DestroyOnlyMe();
        } else
            RoundDirector.instance.dollarHaulList[0].GetComponent<PhysGrabObject>().DestroyPhysGrabObject();

        RoundDirector.instance.dollarHaulList.RemoveAt(0);

        return false;
    }
}