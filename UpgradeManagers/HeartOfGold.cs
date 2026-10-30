using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Newtonsoft.Json;
using Photon.Pun;
using Photon.Realtime;
using REPOLib.Modules;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class HeartOfGoldUpgrade : UpgradeBase<float> {
    internal const byte RegistrationAckMessageType = 1;

    public ConfigEntry<float> BaseHeartValue { get; protected set; }
    internal Dictionary<string, GoldenHeart> GoldenHearts { get; set; } = new();
    public static NetworkedEvent HeartOfGoldEvent = new NetworkedEvent("Heart Of Gold", HeartOfGoldAction);

    private static void HeartOfGoldAction(EventData e) {
        var data = NetworkMessage.FromByteArray((byte[])e.CustomData);
        var heartOfGoldUpgradeInstance = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (data.MessageType == RegistrationAckMessageType) {
            if (string.IsNullOrEmpty(data.PlayerId) || !data.PhotonId.HasValue ||
                !heartOfGoldUpgradeInstance.GoldenHearts.TryGetValue(data.PlayerId, out var localHeart) ||
                !localHeart)
                return;

            localHeart.RecordRegistrationAck(data.PhotonId.Value, e.Sender);
            return;
        }

        if (data.MessageType != 0 || string.IsNullOrEmpty(data.PlayerId) || !data.PhotonId.HasValue) return;

        var player = SemiFunc.PlayerAvatarGetFromSteamID(data.PlayerId!);
        if (!player) return;

        if (!heartOfGoldUpgradeInstance.GoldenHearts.TryGetValue(data.PlayerId!, out var heart) || !heart) {
            heartOfGoldUpgradeInstance.InitUpgrade(player,
                heartOfGoldUpgradeInstance.UpgradeRegister.GetLevel(player));
            heart = heartOfGoldUpgradeInstance.GoldenHearts[data.PlayerId!];
        }

        if (!heart.SetViewId(data.PhotonId.Value)) return;

        var acknowledgement = new NetworkMessage {
            PlayerId = data.PlayerId,
            PhotonId = data.PhotonId,
            MessageType = RegistrationAckMessageType
        };
        HeartOfGoldEvent.RaiseEvent(NetworkMessage.ToByteArray(acknowledgement),
            new RaiseEventOptions { TargetActors = [e.Sender] }, SendOptions.SendReliable);
    }

    public HeartOfGoldUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, float baseValue, float priceMultiplier) :
        base("Heart Of Gold", "assets/repo/mods/resources/items/items/item upgrade heart of gold lib.asset", enabled,
            upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true,
            ((int?)null)) {
        BaseHeartValue =
            config.Bind("Heart Of Gold Upgrade", "Base Value", baseValue, "Base value to scale by player health");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatIncrease(this, "HeartOfGold", value, player, level);

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        EnsureGoldenHeart(player, level);
    }

    internal override void UseUpgrade(PlayerAvatar player, int level) {
        base.UseUpgrade(player, level);
        EnsureGoldenHeart(player, level);
    }

    private void EnsureGoldenHeart(PlayerAvatar player, int level) {
        if (!player) return;

        var playerId = SemiFunc.PlayerGetSteamID(player);
        if (GoldenHearts.TryGetValue(playerId, out var goldenHeart) && goldenHeart) {
            goldenHeart.player = player;
            return;
        }

        goldenHeart = new GameObject($"Golden Heart: {SemiFunc.PlayerGetName(player)}").AddComponent<GoldenHeart>();
        goldenHeart.player = player;
        GoldenHearts[playerId] = goldenHeart;
        SLRUpgradePack.Logger.LogInfo(
            $"Initialized Heart of Gold tracker for {SemiFunc.PlayerGetName(player)} at level {level}");
    }
}

public class GoldenHeart : MonoBehaviour {
    private const float RegistrationRetryInterval = 1f;

    internal PlayerAvatar player { get; set; }
    internal bool Pause { get; set; }
    internal float NextExtractionZoneRefreshTime { get; set; }
    internal int lastLevel { get; set; } = -1;
    internal int lastHealth { get; set; } = -1;
    internal ValuableObject? ValuableComponent { get; private set; }
    private bool createRpcPending;
    private bool createRequested;
    private bool valueSyncPending = true;
    private bool hasQueuedMasterBaseValue;
    private float queuedMasterBaseValue;
    private bool hasQueuedFinalValue;
    private float queuedFinalBaseValue;
    private float queuedFinalValue;
    private float nextRegistrationRetryTime;
    private readonly HashSet<int> registeredActorNumbers = new();
    private readonly HashSet<int> knownRemoteActorNumbers = new();

    private PhotonView photonView;
    private readonly FieldRef<PlayerHealth, int> _healthRef = FieldRefAccess<PlayerHealth, int>("health");
    private readonly FieldRef<ValuableObject, PhysAttribute> _physAttributePresetRef =
        FieldRefAccess<ValuableObject, PhysAttribute>("physAttributePreset");

    private readonly FieldRef<ValuableObject, bool> _discoveredRef = FieldRefAccess<ValuableObject, bool>("discovered");

    private readonly FieldRef<ValuableObject, float> _dollarValueCurrentRef =
        FieldRefAccess<ValuableObject, float>("dollarValueCurrent");

    private void Awake() {
        if (!TryGetComponent<PhotonView>(out photonView)) {
            photonView = gameObject.AddComponent<PhotonView>();
        }
    }

    public void DestroyOnlyMe() {
        if (!ValuableComponent && !createRequested) return;

        if (SemiFunc.IsMultiplayer()) photonView.RPC("DestroyOnlyMeRPC", RpcTarget.All);
        else DestroyOnlyMeRPC();
    }

    [PunRPC]
    private void DestroyOnlyMeRPC() {
        createRequested = false;
        createRpcPending = false;
        hasQueuedMasterBaseValue = false;
        hasQueuedFinalValue = false;
        var valuableComponent = ValuableComponent;
        if (valuableComponent == null || !valuableComponent) return;

        SLRUpgradePack.Logger.LogInfo(
            $"Components in GoldenHeart: {string.Join(',', GetComponents<Object>().Select(v => v.ToString()))}");
        var physGrabObject = valuableComponent.GetComponent<PhysGrabObject>();
        if (RoundDirector.instance && physGrabObject)
            RoundDirector.instance.PhysGrabObjectRemove(physGrabObject);
        Destroy(valuableComponent);
        ValuableComponent = null;
    }

    public void CreateOnlyMe() {
        if (ValuableComponent || createRpcPending || createRequested || Pause) return;

        createRpcPending = true;
        if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(CreateOnlyMeRPC), RpcTarget.All);
        else CreateOnlyMeRPC();
    }

    [PunRPC]
    private void CreateOnlyMeRPC() {
        createRpcPending = false;
        if (ValuableComponent) {
            createRequested = false;
            return;
        }

        createRequested = true;
        TryCreateValuableComponent();
    }

    private void TryCreateValuableComponent() {
        if (!createRequested || Pause || !player || !player.healthGrab || !player.playerHealth || ValuableComponent) return;

        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        SLRUpgradePack.Logger.LogInfo($"Adding valuable component to player {SemiFunc.PlayerGetName(player)}");

        ValuableComponent = player.healthGrab.gameObject.AddComponent<ValuableObject>();
        ValuableObjectValuePatch.MarkExcludedFromExtractionGoal(ValuableComponent);

        SLRUpgradePack.Logger.LogInfo(
            $"Valuable component {SemiFunc.PlayerGetName(player)} instantiated at {JsonConvert.SerializeObject(ValuableComponent.transform)}");

        ValuableComponent.valuePreset = ScriptableObject.CreateInstance<Value>();
        ValuableComponent.valuePreset.valueMin =
            ValuableComponent.valuePreset.valueMax =
                heartOfGoldUpgrade.Calculate(
                    _healthRef.Invoke(player.playerHealth) * heartOfGoldUpgrade.BaseHeartValue.Value,
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
            alphaKeys = [new GradientAlphaKey(1, 0)], colorKeys = [new GradientColorKey(Color.yellow, 0)]
        };

        _physAttributePresetRef.Invoke(ValuableComponent) = ScriptableObject.CreateInstance<PhysAttribute>();

        lastHealth = lastLevel = -1;
        createRequested = false;

        if (hasQueuedMasterBaseValue) {
            var baseValue = queuedMasterBaseValue;
            hasQueuedMasterBaseValue = false;
            ApplyHeartValue(baseValue);
        } else if (hasQueuedFinalValue) {
            var baseValue = queuedFinalBaseValue;
            var finalValue = queuedFinalValue;
            hasQueuedFinalValue = false;
            ApplySyncedHeartValue(baseValue, finalValue);
        }
    }

    private void Update() {
        if (!player) {
            Destroy(this);
            return;
        }

        if (createRequested) TryCreateValuableComponent();

        if (SemiFunc.PlayerAvatarLocal() != player) return;
        if (!player.playerHealth) return;

        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (!heartOfGoldUpgrade.UpgradeEnabled.Value) return;
        if (Pause) return;
        var currentLevel = heartOfGoldUpgrade.UpgradeRegister.GetLevel(player);
        if (currentLevel == 0) return;

        // Heart of Gold is a player-bound valuable; its creation and value must not wait for
        // extraction-point discovery, which can happen much later than player initialization.
        if (!ValuableComponent) CreateOnlyMe();
        if (!ValuableComponent) return;

        var currentHealth = _healthRef.Invoke(player.playerHealth);
        if (currentHealth == 0) return;

        if (lastLevel != currentLevel || lastHealth != currentHealth) {
            lastLevel = currentLevel;
            lastHealth = currentHealth;
            valueSyncPending = true;
        }

        var heartValue = heartOfGoldUpgrade.Calculate(
            currentHealth * heartOfGoldUpgrade.BaseHeartValue.Value, player, currentLevel);
        if (SemiFunc.IsMultiplayer() && !EnsureRemoteViewsRegistered()) return;

        if (!valueSyncPending) return;

        if (!SemiFunc.IsMultiplayer()) ApplyHeartValue(heartValue);
        else photonView.RPC(nameof(UpdateOnlyMeRPC), RpcTarget.MasterClient, heartValue);
        valueSyncPending = false;
    }

    private bool EnsureRemoteViewsRegistered() {
        if (photonView.ViewID == 0) {
            PhotonNetwork.AllocateViewID(photonView);
            if (photonView.ViewID == 0) return false;

            registeredActorNumbers.Clear();
            knownRemoteActorNumbers.Clear();
        }

        var remoteActorNumbers = PhotonNetwork.PlayerListOthers
            .Select(remotePlayer => remotePlayer.ActorNumber)
            .ToHashSet();

        if (!knownRemoteActorNumbers.SetEquals(remoteActorNumbers)) {
            knownRemoteActorNumbers.Clear();
            knownRemoteActorNumbers.UnionWith(remoteActorNumbers);
            registeredActorNumbers.IntersectWith(remoteActorNumbers);
            valueSyncPending = true;
        }

        if (remoteActorNumbers.All(registeredActorNumbers.Contains)) return true;
        if (Time.unscaledTime < nextRegistrationRetryTime) return false;

        var eventContent = new NetworkMessage {
            PlayerId = SemiFunc.PlayerGetSteamID(player),
            PhotonId = photonView.ViewID
        };
        HeartOfGoldUpgrade.HeartOfGoldEvent.RaiseEvent(NetworkMessage.ToByteArray(eventContent),
            NetworkingEvents.RaiseOthers, SendOptions.SendReliable);
        nextRegistrationRetryTime = Time.unscaledTime + RegistrationRetryInterval;
        return false;
    }

    internal void RecordRegistrationAck(int photonId, int actorNumber) {
        if (photonView.ViewID != photonId || actorNumber <= 0) return;
        registeredActorNumbers.Add(actorNumber);
    }

    [PunRPC]
    private void UpdateOnlyMeRPC(float baseValue) {
        if (!SemiFunc.IsMasterClient()) return;

        ApplyHeartValue(baseValue);
    }

    private void ApplyHeartValue(float baseValue) {
        var valuableComponent = ValuableComponent;
        if (valuableComponent == null || !valuableComponent) {
            queuedMasterBaseValue = baseValue;
            hasQueuedMasterBaseValue = true;
            createRequested = true;
            TryCreateValuableComponent();
            return;
        }

        if (!_discoveredRef.Invoke(valuableComponent))
            valuableComponent.Discover(ValuableDiscoverGraphic.State.Discover);

        valuableComponent.valuePreset.valueMin = baseValue;
        valuableComponent.valuePreset.valueMax = baseValue;
        _dollarValueCurrentRef.Invoke(valuableComponent) = baseValue;

        ValuableObjectValuePatch.Action(valuableComponent);

        var finalValue = _dollarValueCurrentRef.Invoke(valuableComponent);
        // Keep the unmodified base in the preset so later recalculations don't stack Object Value.
        valuableComponent.valuePreset.valueMin = baseValue;
        valuableComponent.valuePreset.valueMax = baseValue;

        SLRUpgradePack.Logger.LogInfo(
            $"Heart of Gold value updated for {SemiFunc.PlayerGetName(player)}: base={baseValue}, final={finalValue}, health={_healthRef.Invoke(player.playerHealth)}, level={SLRUpgradePack.HeartOfGoldUpgradeInstance.UpgradeRegister.GetLevel(player)}");

        if (!SemiFunc.IsMultiplayer()) HeartOfGoldValueSetRPC(baseValue, finalValue);
        else photonView.RPC(nameof(HeartOfGoldValueSetRPC), RpcTarget.All, baseValue, finalValue);
    }

    [PunRPC]
    private void HeartOfGoldValueSetRPC(float baseValue, float finalValue) {
        var valuableComponent = ValuableComponent;
        if (valuableComponent == null || !valuableComponent) {
            queuedFinalBaseValue = baseValue;
            queuedFinalValue = finalValue;
            hasQueuedFinalValue = true;
            createRequested = true;
            TryCreateValuableComponent();
            return;
        }

        ApplySyncedHeartValue(baseValue, finalValue);
    }

    private void ApplySyncedHeartValue(float baseValue, float finalValue) {
        var valuableComponent = ValuableComponent;
        if (valuableComponent == null || !valuableComponent) return;

        if (!_discoveredRef.Invoke(valuableComponent))
            valuableComponent.Discover(ValuableDiscoverGraphic.State.Discover);

        valuableComponent.valuePreset.valueMin = baseValue;
        valuableComponent.valuePreset.valueMax = baseValue;
        _dollarValueCurrentRef.Invoke(valuableComponent) = finalValue;
    }

    public void PauseLogic(bool pause) {
        if (!SemiFunc.IsMultiplayer()) PauseLogicRPC(pause);
        else photonView.RPC("PauseLogicRPC", RpcTarget.All, pause);
    }

    [PunRPC]
    private void PauseLogicRPC(bool pause) {
        Pause = pause;
    }

    internal bool SetViewId(int id) {
        if (id <= 0 || !photonView) return false;

        if (photonView.ViewID != id) {
            photonView.ViewID = id;
            if (player && player.photonView)
                photonView.TransferOwnership(player.photonView.Owner);
        }

        return photonView.ViewID == id;
    }
}

[HarmonyPatch(typeof(ExtractionPoint))]
public class ExtractionPointDestroyPatch {
    private static readonly FieldRef<RoundDirector, int> TotalHaulRef = FieldRefAccess<RoundDirector, int>("totalHaul");
    private static readonly FieldRef<RoundDirector, int> CurrentHaulRef = FieldRefAccess<RoundDirector, int>("currentHaul");
    private static readonly FieldRef<ValuableObject, float> DollarValueCurrentRef = FieldRefAccess<ValuableObject, float>("dollarValueCurrent");
    private static readonly FieldRef<ProgressionManager, float> HaulingPointsRef = FieldRefAccess<ProgressionManager, float>("roundPointsHauling");
    private static readonly FieldRef<PlayerAvatar, PlayerDeathHead> PlayerDeathHeadRef = FieldRefAccess<PlayerAvatar, PlayerDeathHead>("playerDeathHead");
    private static readonly FieldRef<PlayerAvatar, RoomVolumeCheck> PlayerAvatarRoomVolumeCheckRef = FieldRefAccess<PlayerAvatar, RoomVolumeCheck>("RoomVolumeCheck");
    private static readonly FieldRef<PlayerDeathHead, RoomVolumeCheck> PlayerDeathHeadRoomVolumeCheckRef = FieldRefAccess<PlayerDeathHead, RoomVolumeCheck>("roomVolumeCheck");
    private static readonly FieldRef<PlayerDeathHead, bool> PlayerDeathHeadTriggeredRef = FieldRefAccess<PlayerDeathHead, bool>("triggered");
    private static readonly FieldRef<RoomVolumeCheck, bool> RoomVolumeInExtractionPointRef = FieldRefAccess<RoomVolumeCheck, bool>("inExtractionPoint");
    private static readonly MethodInfo ExtractCosmeticMethod = Method(typeof(ExtractionPoint), "ExtractCosmeticWorldObject", [typeof(bool)]);
    private static readonly Dictionary<int, bool> LastReportedExtractionPresence = new();
    private static readonly HashSet<int> LastReportedHaulApplications = new();
    private static float NextRoundHaulTraceTime;

    internal static bool TryGetGoldenHeart(HeartOfGoldUpgrade upgrade, ValuableObject valuableObject,
        out GoldenHeart goldenHeart) {
        foreach (var candidate in upgrade.GoldenHearts.Values) {
            if (candidate && candidate.ValuableComponent == valuableObject) {
                goldenHeart = candidate;
                return true;
            }
        }

        goldenHeart = null!;
        return false;
    }

    internal static bool IsGoldenHeartValuable(ValuableObject valuableObject) {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;
        return heartOfGoldUpgrade != null && valuableObject &&
               TryGetGoldenHeart(heartOfGoldUpgrade, valuableObject, out _);
    }

    private static void AddGoldenHeartHaulValue(ExtractionPoint extractionPoint, ValuableObject valuableObject) {
        var value = DollarValueCurrentRef.Invoke(valuableObject);
        TotalHaulRef.Invoke(RoundDirector.instance) += (int)value;

        var haulScore = valuableObject.GetLocalPlayerHaulScore(extractionPoint.gameObject);
        if (haulScore > 0f)
            HaulingPointsRef.Invoke(ProgressionManager.instance) += value / 10f * haulScore;
    }

    // Run after the game's haul list has been tallied. This is called even while the haul is below
    // quota, unlike the extraction-point state callback, so standing in the zone updates the live
    // value instead of waiting for the extraction sequence to start.
    internal static void AddPlayerHeartValueToCurrentHaul(RoundDirector roundDirector) {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;
        if (heartOfGoldUpgrade == null || !roundDirector || !heartOfGoldUpgrade.UpgradeEnabled.Value ||
            !SemiFunc.IsMasterClientOrSingleplayer()) return;

        if (heartOfGoldUpgrade.GoldenHearts.Values.Any(heart => heart && heart.ValuableComponent) &&
            Time.unscaledTime >= NextRoundHaulTraceTime) {
            SLRUpgradePack.Logger.LogInfo(
                $"Heart of Gold round haul check: currentHaul={CurrentHaulRef(roundDirector)}, listedHearts={heartOfGoldUpgrade.GoldenHearts.Values.Count(heart => heart && heart.ValuableComponent && roundDirector.dollarHaulList.Contains(heart.ValuableComponent.gameObject))}");
            NextRoundHaulTraceTime = Time.unscaledTime + 2f;
        }

        var playerHeartValue = 0;
        foreach (var goldenHeart in heartOfGoldUpgrade.GoldenHearts.Values) {
            if (!IsPlayerHeartInExtraction(goldenHeart)) continue;

            var valuableObject = goldenHeart.ValuableComponent;
            if (!valuableObject) continue;

            var value = (int)DollarValueCurrentRef.Invoke(valuableObject);
            playerHeartValue += value;
            if (LastReportedHaulApplications.Add(goldenHeart.GetInstanceID()))
                SLRUpgradePack.Logger.LogInfo(
                    $"Added Heart of Gold value to live haul for {SemiFunc.PlayerGetName(goldenHeart.player)}: {value}");
        }

        // RoundDirector.HaulCheck recalculates currentHaul from scratch, so adding here cannot
        // accumulate across frames. ExtractionPoint then consumes this value for display/quota.
        if (playerHeartValue > 0) CurrentHaulRef(roundDirector) += playerHeartValue;
    }

    internal static bool IsPlayerHeartInExtraction(GoldenHeart goldenHeart, bool refreshZone = false) {
        if (!goldenHeart || goldenHeart.Pause || !goldenHeart.player || !goldenHeart.ValuableComponent)
            return false;

        var playerDeathHead = PlayerDeathHeadRef(goldenHeart.player);
        var playerRoomVolumeCheck = PlayerAvatarRoomVolumeCheckRef(goldenHeart.player);
        var deathHeadRoomVolumeCheck = playerDeathHead ? PlayerDeathHeadRoomVolumeCheckRef(playerDeathHead) : null;
        if (!playerRoomVolumeCheck && !deathHeadRoomVolumeCheck) return false;

        // The avatar and its death head have distinct room-volume checks. The avatar check is the one
        // that follows a living player; the death-head check is only relevant after the head is triggered.
        // Refresh both before reading because their normal checks can be paused or run on different ticks.
        if (refreshZone || Time.time >= goldenHeart.NextExtractionZoneRefreshTime) {
            if (playerRoomVolumeCheck) playerRoomVolumeCheck.CheckSet();
            if (deathHeadRoomVolumeCheck) deathHeadRoomVolumeCheck.CheckSet();
            goldenHeart.NextExtractionZoneRefreshTime = Time.time + 0.1f;
        }
        var avatarInExtraction = playerRoomVolumeCheck && RoomVolumeInExtractionPointRef(playerRoomVolumeCheck);
        var deathHeadInExtraction = playerDeathHead && PlayerDeathHeadTriggeredRef(playerDeathHead) &&
                                    deathHeadRoomVolumeCheck && RoomVolumeInExtractionPointRef(deathHeadRoomVolumeCheck);
        var isInExtraction = avatarInExtraction || deathHeadInExtraction;
        var heartId = goldenHeart.GetInstanceID();
        if (!LastReportedExtractionPresence.TryGetValue(heartId, out var wasInExtraction) ||
            wasInExtraction != isInExtraction) {
            LastReportedExtractionPresence[heartId] = isInExtraction;
            SLRUpgradePack.Logger.LogInfo(
                $"Heart of Gold zone check for {SemiFunc.PlayerGetName(goldenHeart.player)}: inExtraction={isInExtraction}, avatar={avatarInExtraction}, deathHead={deathHeadInExtraction}, value={DollarValueCurrentRef.Invoke(goldenHeart.ValuableComponent)}");
        }
        if (!isInExtraction) LastReportedHaulApplications.Remove(heartId);

        return isInExtraction;
    }

    private static void ExtractGoldenHeart(ExtractionPoint extractionPoint, GoldenHeart goldenHeart) {
        var valuableObject = goldenHeart.ValuableComponent;
        if (!valuableObject) return;

        AddGoldenHeartHaulValue(extractionPoint, valuableObject);
        RoundDirector.instance.dollarHaulList.Remove(valuableObject.gameObject);
        SLRUpgradePack.Logger.LogInfo(
            $"Player {SemiFunc.PlayerGetName(goldenHeart.player)} in extraction zone counts as valuable");
        goldenHeart.Pause = true;
        goldenHeart.PauseLogic(true);
        goldenHeart.DestroyOnlyMe();
    }

    [HarmonyPatch("DestroyAllPhysObjectsInHaulList")]
    [HarmonyPrefix]
    private static bool DestroyAllPrefix(ExtractionPoint __instance) {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !heartOfGoldUpgrade.UpgradeEnabled.Value)
            return true;

        var dollarHaulList = RoundDirector.instance.dollarHaulList;
        foreach (var dollarHaul in new List<GameObject>(dollarHaulList)) {
            if (!dollarHaul || !dollarHaul.TryGetComponent<PhysGrabObject>(out var physGrabObject) ||
                !physGrabObject || !dollarHaul.TryGetComponent<ValuableObject>(out var valuableObject) ||
                !valuableObject || !TryGetGoldenHeart(heartOfGoldUpgrade, valuableObject, out var goldenHeart))
                continue;

            ExtractGoldenHeart(__instance, goldenHeart);
        }

        // Golden Hearts are attached to players and their hidden PhysGrabObject is disabled, so the game does not
        // reliably add them to dollarHaulList. Check each player's live RoomVolumeCheck instead.
        foreach (var goldenHeart in new List<GoldenHeart>(heartOfGoldUpgrade.GoldenHearts.Values)) {
            if (!IsPlayerHeartInExtraction(goldenHeart, refreshZone: true)) continue;

            ExtractGoldenHeart(__instance, goldenHeart);
        }

        // Leave the rest of extraction to REPO so its revive, scoring, box, and cosmetic logic stays authoritative.
        return true;
    }

    [HarmonyPatch("DestroyTheFirstPhysObjectsInHaulList")]
    [HarmonyPrefix]
    private static bool DestroyFirstPrefix(ExtractionPoint __instance) {
        var heartOfGoldUpgrade = SLRUpgradePack.HeartOfGoldUpgradeInstance;

        if (!SemiFunc.IsMasterClientOrSingleplayer() || !heartOfGoldUpgrade.UpgradeEnabled.Value)
            return true;

        // This call consumes one cosmetic object when present. Skip the original method afterward, or it would
        // consume a second cosmetic object in the same extraction tick.
        if ((bool)ExtractCosmeticMethod.Invoke(__instance, [false]))
            return false;

        var dollarHaulList = RoundDirector.instance.dollarHaulList;
        if (dollarHaulList.Count == 0 || !dollarHaulList[0] ||
            !dollarHaulList[0].TryGetComponent<PhysGrabObject>(out var physGrabObject) || !physGrabObject ||
            !dollarHaulList[0].TryGetComponent<ValuableObject>(out var valuableObject) || !valuableObject ||
            !TryGetGoldenHeart(heartOfGoldUpgrade, valuableObject, out var goldenHeart))
            return true;

        ExtractGoldenHeart(__instance, goldenHeart);

        return false;
    }
}

[HarmonyPatch(typeof(RoundDirector), "Update")]
public class RoundDirectorUpdatePatch {
    private static void Prefix(RoundDirector __instance) {
        __instance.dollarHaulList.RemoveAll(go =>
            go == null || !go.TryGetComponent<ValuableObject>(out var valuableObject) || !valuableObject);
    }
}

[HarmonyPatch(typeof(RoundDirector), "HaulCheck")]
public class RoundDirectorHaulCheckPatch {
    private static void Prefix(RoundDirector __instance) {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        // Heart of Gold follows the player's live RoomVolumeCheck in ExtractionPoint's haul display.
        // Keep it out of the game's separate valuable-object tally, which checks the attached component's
        // RoomVolumeCheck and can disagree with the player's zone state.
        __instance.dollarHaulList.RemoveAll(gameObject =>
            gameObject && gameObject.TryGetComponent<ValuableObject>(out var valuableObject) &&
            valuableObject && ExtractionPointDestroyPatch.IsGoldenHeartValuable(valuableObject));
    }

    private static void Postfix(RoundDirector __instance) {
        ExtractionPointDestroyPatch.AddPlayerHeartValueToCurrentHaul(__instance);
    }
}
