using System;
using System.Collections;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class ExtraLifeUpgrade: UpgradeBase<float> {
    public ConfigEntry<int> RevivePercent { get; protected set; }
    
    public ExtraLifeUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                     ConfigFile config, AssetBundle assetBundle, int revivePercent, float priceMultiplier) :
    base("Extra Life", "assets/repo/mods/resources/items/items/item upgrade extra life.asset", enabled,
    upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, false, 2000, 100000, true, false){
        RevivePercent = config.Bind("Extra Life Upgrade", "revivePercent", revivePercent, "Percentage of health to recover when revived");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "ExtraLife", value, player, level);
}

public class ExtraLife : MonoBehaviour {
    public PlayerDeathHead PlayerDeathHead { get; set; }
    public PlayerAvatar PlayerAvatar { get; set; }
    private bool _isMoving;
    private Coroutine? reviving;

    private void Update() {
        if(SemiFunc.PlayerGetSteamID(PlayerAvatar) !=  SemiFunc.PlayerGetSteamID(SemiFunc.PlayerAvatarLocal())) return;
        var healthRef = FieldRefAccess<PlayerHealth, int>("health");
        if (PlayerAvatar.playerHealth) {
            var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
            if (healthRef.Invoke(PlayerAvatar.playerHealth) == 0 && extraLifeUpgrade.UpgradeLevel > 0 && !_isMoving && reviving == null) {
                reviving = StartCoroutine(BeginReviving());
            }
        } else {
            SLRUpgradePack.Logger.LogInfo("why are we here?");
        }
    }

    private IEnumerator BeginReviving() {
        yield return new WaitForSecondsRealtime(1f);
        SLRUpgradePack.Logger.LogInfo($"Reviving {SemiFunc.PlayerGetName(PlayerAvatar)}");
        var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
        var maxHealthRef = FieldRefAccess<PlayerHealth, int>("maxHealth");
        
        while (_isMoving) {
            var deadTimerRef = FieldRefAccess<PlayerAvatar, float>("deadTimer");
            deadTimerRef.Invoke(PlayerAvatar) += 1;
            yield return new WaitForSecondsRealtime(0.1f);
            SLRUpgradePack.Logger.LogInfo("Pause attempt to revive moving head");
        }
        
        PlayerAvatar.Revive();
        PlayerAvatar.playerHealth.HealOther(Mathf.FloorToInt(maxHealthRef.Invoke(PlayerAvatar.playerHealth) * extraLifeUpgrade.RevivePercent.Value / 100f), true);
        extraLifeUpgrade.UpgradeRegister.RemoveLevel(PlayerAvatar);
        
        reviving = null;
    }

    private void Start() {
        SLRUpgradePack.Logger.LogInfo($"{SemiFunc.PlayerGetName(PlayerAvatar)} has obtained extra lives");
        StartCoroutine(MovementCheck());
    }

    private IEnumerator MovementCheck() {
        Vector3 currentPosition = PlayerDeathHead.transform.position;
        while (true) {
            yield return new WaitForSecondsRealtime(0.5f);
            Vector3 nextPosition = PlayerDeathHead.transform.position;
            _isMoving = Vector3.Distance(currentPosition, nextPosition) >= 0.01f;
            SLRUpgradePack.Logger.LogDebug($"{SemiFunc.PlayerGetName(PlayerAvatar)} is moving: {_isMoving}");
            currentPosition = nextPosition;
        }
    }
}

[HarmonyPatch(typeof(PlayerDeathHead), "Update")]
public class PlayerDeathHeadExtraLifePatch {
    [HarmonyWrapSafe]
    private static void Postfix(PlayerDeathHead __instance) {
        if (SemiFunc.PlayerGetSteamID(__instance.playerAvatar) != SemiFunc.PlayerGetSteamID(SemiFunc.PlayerAvatarLocal())) return;
        
        var extraLifeUpgrade = SLRUpgradePack.ExtraLifeUpgradeInstance;
        if (extraLifeUpgrade.UpgradeLevel == 0) return;

        if (!__instance.TryGetComponent<ExtraLife>(out var extraLife)) {
            extraLife = __instance.gameObject.AddComponent<ExtraLife>();
            extraLife.PlayerAvatar = __instance.playerAvatar;
            extraLife.PlayerDeathHead = __instance;
        }
    }
}
