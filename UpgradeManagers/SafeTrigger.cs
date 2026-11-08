using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class SafeTriggerUpgrade : UpgradeBase<float> {
    public SafeTriggerUpgrade(bool enabled, float chancePerLevel, ConfigFile config, AssetBundle assetBundle,
        float priceMultiplier) :
        base("Safe Trigger", "assets/repo/mods/resources/items/items/item upgrade safe trigger lib.asset",
            enabled, chancePerLevel, false, 1.1f, config, assetBundle, priceMultiplier, true, false, 10,
            "Chance per level to avoid consuming a charge or single-use item.",
            "Safe Trigger Upgrade",
            "Chance to keep a charge or reusable item when using it.") { }

    public override float Calculate(float value, PlayerAvatar? player, int level) =>
        Mathf.Clamp01((UpgradeAmount?.Value ?? 0f) * level);
}

internal static class SafeTriggerRoll {
    private static readonly FieldRef<ItemEquippable, bool> IsEquippedRef =
        FieldRefAccess<ItemEquippable, bool>("isEquipped");
    private static readonly FieldRef<ItemDrone, bool> DroneIsActiveRef =
        FieldRefAccess<ItemDrone, bool>("itemActivated");
    private static readonly FieldRef<ItemDrone, PlayerAvatar> DroneOwnerRef =
        FieldRefAccess<ItemDrone, PlayerAvatar>("droneOwner");
    private static readonly FieldRef<ItemToggle, int> PlayerTogglePhotonIdRef =
        FieldRefAccess<ItemToggle, int>("playerTogglePhotonID");
    private static readonly FieldRef<RunManager, bool> LevelIsShopRef =
        FieldRefAccess<RunManager, bool>("levelIsShop");
    private static readonly FieldRef<ItemBattery, int> BatteryLifeIntRef =
        FieldRefAccess<ItemBattery, int>("batteryLifeInt");
    private static readonly FieldRef<ItemAttributes, string> ItemInstanceNameRef =
        FieldRefAccess<ItemAttributes, string>("instanceName");
    private static readonly FieldRef<StatsManager, Dictionary<string, int>> ItemsPurchasedRef =
        FieldRefAccess<StatsManager, Dictionary<string, int>>("itemsPurchased");
    internal static readonly FieldInfo? BatteryLifeField = AccessTools.Field(typeof(ItemBattery), "batteryLife");
    internal static readonly MethodInfo? SetBatteryLifeMethod = AccessTools.Method(
        typeof(SafeTriggerRoll), nameof(SetBatteryLifeWithSafeTrigger));
    private static readonly MethodInfo? BatteryFullPercentChangeMethod = AccessTools.Method(
        typeof(ItemBattery), "BatteryFullPercentChange",
        new[] { typeof(int), typeof(bool), typeof(bool) });

    private sealed class ItemUseState {
        internal int UseCount;
        internal int HighestCachedRoll;
        internal readonly Dictionary<int, float> CachedRolls = new();
        internal bool HasPendingUse;
        internal bool PendingPreserved;
        internal bool HasLastHealthPackResult;
        internal bool LastHealthPackPreserved;
    }

    internal sealed class ItemRemovalContext {
        internal ItemEquippable ItemEquippable { get; }
        internal string ItemInstanceName { get; }
        internal string ItemType { get; }
        internal ItemReviveItem? ReviveItem { get; }
        internal bool RemovalAttempted { get; set; }
        internal bool Preserved { get; set; }

        internal ItemRemovalContext(ItemEquippable itemEquippable, string itemInstanceName, string itemType,
            ItemReviveItem? reviveItem) {
            ItemEquippable = itemEquippable;
            ItemInstanceName = itemInstanceName;
            ItemType = itemType;
            ReviveItem = reviveItem;
        }
    }

    internal sealed class UpgradePurchaseCountTransaction {
        private readonly Dictionary<string, int> _itemsPurchased;
        private readonly string _itemInstanceName;
        private readonly int _purchaseCount;
        private readonly bool _hadPurchaseCount;

        internal UpgradePurchaseCountTransaction(Dictionary<string, int> itemsPurchased, string itemInstanceName,
            int purchaseCount, bool hadPurchaseCount) {
            _itemsPurchased = itemsPurchased;
            _itemInstanceName = itemInstanceName;
            _purchaseCount = purchaseCount;
            _hadPurchaseCount = hadPurchaseCount;
        }

        internal void Restore() {
            if (!_hadPurchaseCount) return;
            if (!_itemsPurchased.TryGetValue(_itemInstanceName, out var currentCount)) {
                _itemsPurchased[_itemInstanceName] = _purchaseCount;
                return;
            }

            // Preserve any purchase added re-entrantly while the original count was hidden.
            _itemsPurchased[_itemInstanceName] = unchecked(currentCount + _purchaseCount);
        }
    }

    private static readonly ConditionalWeakTable<ItemEquippable, ItemUseState> ItemUseStates = new();
    private static readonly ConditionalWeakTable<ItemUpgrade, ItemUseState> UpgradeItemUseStates = new();
    [ThreadStatic]
    private static ItemRemovalContext? _activeItemRemovalContext;
    private static readonly MethodInfo? GrenadeResetMethod = AccessTools.Method(typeof(ItemGrenade), "GrenadeReset");
    private static readonly MethodInfo? MineResetMethod = AccessTools.Method(typeof(ItemMine), "ResetMine");
    private static bool _runtimeWarningLogged;
    private static bool _rollCacheWarningLogged;
    private static bool _itemRemovalNameMismatchWarningLogged;
    private const int RollBatchSize = 1;
    private const int RollRefillThreshold = 0;

    internal static bool IsLevelShop => RunManager.instance && LevelIsShopRef.Invoke(RunManager.instance);

    internal static bool ShouldPreserveCharge(ItemEquippable itemEquippable, bool logSuccess = true) {
        if (!itemEquippable || !IsEquippedRef.Invoke(itemEquippable)) return false;

        try {
            var player = itemEquippable.GetOwnerPlayerAvatar();
            return ShouldPreserveChargeForPlayer(player, "battery charge", logSuccess);
        }
        catch (System.Exception exception) {
            LogRuntimeWarning(exception);
            return false;
        }
    }

    // Vehicle durability and deployed drone modules are battery-backed equipment too, but their
    // ItemEquippable is not in the hand while the resource is being spent. Resolve the player
    // responsible for those uses instead of requiring isEquipped.
    internal static bool ShouldPreserveCharge(ItemBattery battery, bool logSuccess = true) {
        if (!battery) return false;

        try {
            var itemEquippable = battery.GetComponent<ItemEquippable>();

            // Active orbs keep draining after they leave the player's hand, like deployed drones.
            // Attribute their drain to the item owner instead of requiring it to remain equipped.
            var orb = battery.GetComponent<ItemOrb>();
            if (orb && orb.itemActive) {
                var owner = itemEquippable ? itemEquippable.GetOwnerPlayerAvatar() : null;
                return ShouldPreserveChargeForPlayer(owner, "orb charge", logSuccess);
            }

            if (itemEquippable && IsEquippedRef.Invoke(itemEquippable))
                return ShouldPreserveCharge(itemEquippable, logSuccess);

            var vehicle = battery.GetComponent<ItemVehicle>();
            if (vehicle && vehicle.seats != null && vehicle.seats.Length > 0) {
                var driver = vehicle.seats[0] != null ? vehicle.seats[0].seatedPlayer : null;
                if (driver) return ShouldPreserveChargeForPlayer(driver, "vehicle durability", logSuccess);
            }

            var drone = battery.GetComponent<ItemDrone>();
            if (drone && DroneIsActiveRef.Invoke(drone)) {
                var owner = DroneOwnerRef.Invoke(drone);
                if (!owner && itemEquippable) owner = itemEquippable.GetOwnerPlayerAvatar();
                return ShouldPreserveChargeForPlayer(owner, "drone charge", logSuccess);
            }

            return false;
        }
        catch (System.Exception exception) {
            LogRuntimeWarning(exception);
            return false;
        }
    }

    private static bool ShouldPreserveChargeForPlayer(PlayerAvatar? player, string useDescription,
        bool logSuccess) {
        var upgrade = SLRUpgradePack.SafeTriggerUpgradeInstance;
        if (!SemiFunc.IsMasterClientOrSingleplayer() || !player || upgrade == null ||
            !upgrade.UpgradeEnabled.Value || upgrade.UpgradeRegister == null) return false;

        try {
            var level = upgrade.UpgradeRegister.GetLevel(player);
            var chance = upgrade.Calculate(0f, player, level);
            if (chance <= 0f || (chance < 1f && UnityEngine.Random.value >= chance)) return false;

            if (logSuccess)
                SLRUpgradePack.Logger.LogDebug(
                    $"Safe Trigger preserved {useDescription} for {SemiFunc.PlayerGetName(player)} (level {level}, chance {chance:P0}).");
            return true;
        }
        catch (System.Exception exception) {
            LogRuntimeWarning(exception);
            return false;
        }
    }

    // Replicated use RPCs can still drain battery state on peers even when the master preserves a
    // charge. Publish the unchanged value so those peers do not retain a locally-decremented level.
    internal static bool SyncPreservedCharge(ItemBattery battery) {
        if (!battery) return false;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return false;
        if (!SemiFunc.IsMultiplayer()) return true;
        if (BatteryFullPercentChangeMethod == null) {
            LogRuntimeWarning(new System.MissingMethodException(typeof(ItemBattery).FullName,
                "BatteryFullPercentChange(int, bool, bool)"));
            return false;
        }

        try {
            BatteryFullPercentChangeMethod.Invoke(battery,
                new object[] { BatteryLifeIntRef.Invoke(battery), false, false });
            return true;
        }
        catch (System.Exception exception) {
            LogRuntimeWarning(exception);
            return false;
        }
    }

    internal static void SetBatteryLifeWithSafeTrigger(ItemBattery battery, float newBatteryLife) {
        if (!battery) return;

        if (newBatteryLife < battery.batteryLife &&
            ShouldPreserveCharge(battery) && SyncPreservedCharge(battery)) return;

        battery.batteryLife = newBatteryLife;
    }

    // Random rolls are authored by the master and cached per networked item. This lets peers make the
    // same preservation decision even when their local UnityEngine.Random streams differ.
    internal static bool ShouldPreserveSingleUseItem(ItemEquippable itemEquippable, string itemType) {
        if (!itemEquippable) return false;

        var state = ItemUseStates.GetValue(itemEquippable, _ => new ItemUseState());
        var useNumber = unchecked(++state.UseCount);
        try {
            var upgrade = SLRUpgradePack.SafeTriggerUpgradeInstance;
            if (upgrade == null || !upgrade.UpgradeEnabled.Value || upgrade.UpgradeRegister == null) return false;

            var player = itemEquippable.GetOwnerPlayerAvatar();
            if (!player) return false;

            var level = upgrade.UpgradeRegister.GetLevel(player);
            var chance = upgrade.Calculate(0f, player, level);
            if (chance <= 0f) return false;
            if (chance < 1f &&
                (!TryGetRoll(itemEquippable, itemEquippable, state, useNumber, out var roll) || roll >= chance))
                return false;

            if (SemiFunc.IsMasterClientOrSingleplayer())
                SLRUpgradePack.Logger.LogDebug(
                    $"Safe Trigger preserved a {itemType} for {SemiFunc.PlayerGetName(player)} (level {level}, chance {chance:P0}).");
            return true;
        }
        catch (System.Exception exception) {
            LogRuntimeWarning(exception);
            return false;
        }
    }

    internal static bool BeginUpgradeItemUse(ItemUpgrade itemUpgrade, ItemToggle itemToggle) {
        if (!itemUpgrade || !itemToggle || SemiFunc.RunIsShop() || IsLevelShop) return false;

        var itemEquippable = itemUpgrade.GetComponent<ItemEquippable>();
        if (!itemEquippable) return false;

        var state = UpgradeItemUseStates.GetValue(itemUpgrade, _ => new ItemUseState());
        state.UseCount = unchecked(state.UseCount + 1);
        state.HasPendingUse = true;
        state.PendingPreserved = false;

        try {
            var upgrade = SLRUpgradePack.SafeTriggerUpgradeInstance;
            if (upgrade == null || !upgrade.UpgradeEnabled.Value || upgrade.UpgradeRegister == null) return false;

            var player = SemiFunc.PlayerAvatarGetFromPhotonID(PlayerTogglePhotonIdRef.Invoke(itemToggle));
            if (!player) return false;

            var level = upgrade.UpgradeRegister.GetLevel(player);
            var chance = upgrade.Calculate(0f, player, level);
            if (chance <= 0f) return false;
            if (chance < 1f &&
                (!TryGetRoll(itemUpgrade, itemEquippable, state, state.UseCount, out var roll) || roll >= chance))
                return false;

            state.PendingPreserved = true;
            if (SemiFunc.IsMasterClientOrSingleplayer())
                SLRUpgradePack.Logger.LogDebug(
                    $"Safe Trigger preserved a player upgrade for {SemiFunc.PlayerGetName(player)} (level {level}, chance {chance:P0}).");
            return true;
        }
        catch (System.Exception exception) {
            LogRuntimeWarning(exception);
            return false;
        }
    }

    internal static bool TryTemporarilyRemoveUpgradePurchaseCount(ItemUpgrade itemUpgrade,
        out UpgradePurchaseCountTransaction? transaction) {
        transaction = null;
        if (!itemUpgrade || !IsUpgradeItemPreserved(itemUpgrade)) return false;

        var attributes = itemUpgrade.GetComponent<ItemAttributes>();
        var statsManager = StatsManager.instance;
        if (!attributes || !statsManager) return false;

        var itemInstanceName = ItemInstanceNameRef.Invoke(attributes);
        if (string.IsNullOrEmpty(itemInstanceName)) return false;

        var itemsPurchased = ItemsPurchasedRef.Invoke(statsManager);
        if (itemsPurchased == null) return false;

        var hadPurchaseCount = itemsPurchased.TryGetValue(itemInstanceName, out var purchaseCount);
        if (hadPurchaseCount) itemsPurchased.Remove(itemInstanceName);

        transaction = new UpgradePurchaseCountTransaction(itemsPurchased, itemInstanceName, purchaseCount,
            hadPurchaseCount);
        return true;
    }

    internal static void CancelUpgradeItemPreservation(ItemUpgrade itemUpgrade) {
        if (!itemUpgrade || !UpgradeItemUseStates.TryGetValue(itemUpgrade, out var state) || !state.HasPendingUse)
            return;

        state.PendingPreserved = false;
    }

    internal static bool CompleteUpgradeItemUse(ItemUpgrade itemUpgrade, bool prefixPreserved) {
        if (!itemUpgrade || !UpgradeItemUseStates.TryGetValue(itemUpgrade, out var state) || !state.HasPendingUse)
            return false;

        var preserved = prefixPreserved && state.PendingPreserved;
        state.HasPendingUse = false;
        state.PendingPreserved = false;
        return preserved;
    }

    internal static ItemRemovalContext? BeginHealthPackUpdate(ItemHealthPack healthPack) =>
        BeginItemRemovalContext(healthPack ? healthPack.GetComponent<ItemEquippable>() : null, "health pack", null);

    internal static ItemRemovalContext? BeginReviveItemUse(ItemReviveItem reviveItem) =>
        BeginItemRemovalContext(reviveItem ? reviveItem.GetComponent<ItemEquippable>() : null, "revive item",
            reviveItem);

    private static ItemRemovalContext? BeginItemRemovalContext(ItemEquippable? itemEquippable, string itemType,
        ItemReviveItem? reviveItem) {
        var previous = _activeItemRemovalContext;
        var attributes = itemEquippable ? itemEquippable.GetComponent<ItemAttributes>() : null;
        var itemInstanceName = attributes ? ItemInstanceNameRef.Invoke(attributes) : string.Empty;
        _activeItemRemovalContext = itemEquippable
            ? new ItemRemovalContext(itemEquippable, itemInstanceName, itemType, reviveItem)
            : null;
        return previous;
    }

    internal static bool TryPreserveActiveItemRemoval(string itemInstanceName) {
        var context = _activeItemRemovalContext;
        if (context == null) return false;

        if (context.RemovalAttempted) return context.Preserved;
        context.RemovalAttempted = true;

        if (!context.ItemEquippable) return false;
        var state = ItemUseStates.GetValue(context.ItemEquippable, _ => new ItemUseState());
        if (string.IsNullOrEmpty(context.ItemInstanceName) ||
            !string.Equals(itemInstanceName, context.ItemInstanceName, StringComparison.Ordinal)) {
            // Fail closed if this game's item-name field no longer matches the removal argument.
            // Mark the use consumed so UsedRPC cannot independently roll and preserve only the prop.
            state.PendingPreserved = false;
            state.HasPendingUse = true;
            context.Preserved = false;
            if (!_itemRemovalNameMismatchWarningLogged) {
                _itemRemovalNameMismatchWarningLogged = true;
                SLRUpgradePack.Logger.LogWarning(
                    $"Safe Trigger could not match a {context.ItemType} removal name to its item component; " +
                    "this use will be consumed normally.");
            }

            return false;
        }

        state.PendingPreserved = ShouldPreserveSingleUseItem(context.ItemEquippable, context.ItemType);
        state.HasPendingUse = true;
        context.Preserved = state.PendingPreserved;
        return context.Preserved;
    }

    internal static bool TryPreserveActiveReviveItemDestruction(PhysGrabObjectImpactDetector impactDetector) {
        var context = _activeItemRemovalContext;
        if (context?.ReviveItem == null || !impactDetector ||
            impactDetector.GetComponent<ItemReviveItem>() != context.ReviveItem || !context.ItemEquippable ||
            !ItemUseStates.TryGetValue(context.ItemEquippable, out var state) || !state.HasPendingUse)
            return false;

        var preserved = state.PendingPreserved;
        state.HasPendingUse = false;
        state.PendingPreserved = false;
        return preserved;
    }

    internal static void RestoreItemRemovalContext(ItemRemovalContext? previous) {
        var endingContext = _activeItemRemovalContext;
        if (endingContext?.ReviveItem && endingContext.ItemEquippable &&
            ItemUseStates.TryGetValue(endingContext.ItemEquippable, out var state)) {
            state.HasPendingUse = false;
            state.PendingPreserved = false;
        }

        _activeItemRemovalContext = previous;
    }

    internal static void EnsureHealthPackOutcome(ItemHealthPack healthPack) {
        var itemEquippable = healthPack ? healthPack.GetComponent<ItemEquippable>() : null;
        if (!itemEquippable) return;

        var state = ItemUseStates.GetValue(itemEquippable, _ => new ItemUseState());
        if (state.HasPendingUse) return;

        state.PendingPreserved = ShouldPreserveSingleUseItem(itemEquippable, "health pack");
        state.HasPendingUse = true;
    }

    internal static void DestroyHealthPackEquippableIfConsumed(ItemEquippable itemEquippable,
        ItemHealthPack healthPack) {
        if (!itemEquippable) return;
        var state = healthPack ? GetHealthPackState(healthPack) : null;
        if (state?.HasPendingUse == true && state.PendingPreserved) return;
        Object.Destroy(itemEquippable);
    }

    internal static bool CompleteHealthPackUse(ItemHealthPack healthPack) {
        var state = healthPack ? GetHealthPackState(healthPack) : null;
        if (state == null || !state.HasPendingUse) return false;

        var preserved = state.PendingPreserved;
        state.HasPendingUse = false;
        state.HasLastHealthPackResult = true;
        state.LastHealthPackPreserved = preserved;
        return preserved;
    }

    internal static bool CompleteHealthPackUpdate(ItemHealthPack healthPack) {
        var state = healthPack ? GetHealthPackState(healthPack) : null;
        if (state == null || !state.HasLastHealthPackResult) return false;

        var preserved = state.LastHealthPackPreserved;
        state.HasLastHealthPackResult = false;
        return preserved;
    }

    internal static void ResetMineAfterPreservedUse(ItemMine mine, ref bool mineDestroyed) {
        MineResetMethod?.Invoke(mine, null);
        mineDestroyed = false;
    }

    internal static bool CanResetMine => MineResetMethod != null;
    internal static bool CanResetGrenade => GrenadeResetMethod != null;

    internal static void ResetGrenadeAfterPreservedUse(ItemGrenade grenade) =>
        GrenadeResetMethod?.Invoke(grenade, null);

    private static ItemUseState? GetHealthPackState(ItemHealthPack healthPack) {
        var itemEquippable = healthPack ? healthPack.GetComponent<ItemEquippable>() : null;
        return itemEquippable ? ItemUseStates.GetValue(itemEquippable, _ => new ItemUseState()) : null;
    }

    internal static bool IsUpgradeItemPreserved(ItemUpgrade itemUpgrade) =>
        itemUpgrade && UpgradeItemUseStates.TryGetValue(itemUpgrade, out var state) &&
        state.HasPendingUse && state.PendingPreserved;

    internal static void PublishInitialRolls(ItemEquippable itemEquippable) {
        if (!itemEquippable || !SemiFunc.IsMasterClientOrSingleplayer() || !SemiFunc.IsMultiplayer()) return;
        var state = ItemUseStates.GetValue(itemEquippable, _ => new ItemUseState());
        if (state.HighestCachedRoll == 0) PublishRollBatch(itemEquippable, 1, RollBatchSize);
    }

    internal static void ReceiveRolls(ItemEquippable itemEquippable, int firstUseNumber, float[] rolls) {
        if (!itemEquippable || firstUseNumber < 1 || rolls == null || rolls.Length == 0) return;

        CacheRolls(ItemUseStates.GetValue(itemEquippable, _ => new ItemUseState()), firstUseNumber, rolls);
        var itemUpgrade = itemEquippable.GetComponent<ItemUpgrade>();
        if (itemUpgrade)
            CacheRolls(UpgradeItemUseStates.GetValue(itemUpgrade, _ => new ItemUseState()), firstUseNumber, rolls);
    }

    private static bool TryGetRoll(Component item, ItemEquippable itemEquippable, ItemUseState state,
        int useNumber, out float roll) {
        if (!SemiFunc.IsMultiplayer()) {
            roll = UnityEngine.Random.value;
            return true;
        }

        if (state.CachedRolls.TryGetValue(useNumber, out roll)) {
            if (SemiFunc.IsMasterClientOrSingleplayer() && state.HighestCachedRoll - useNumber <= RollRefillThreshold)
                PublishRollBatch(itemEquippable, state.HighestCachedRoll + 1, RollBatchSize);
            return true;
        }

        if (SemiFunc.IsMasterClientOrSingleplayer()) {
            var firstUseNumber = Mathf.Max(useNumber, state.HighestCachedRoll + 1);
            var count = Mathf.Max(RollBatchSize, useNumber - firstUseNumber + 1);
            if (PublishRollBatch(itemEquippable, firstUseNumber, count) &&
                state.CachedRolls.TryGetValue(useNumber, out roll)) return true;
        }

        if (!_rollCacheWarningLogged) {
            _rollCacheWarningLogged = true;
            SLRUpgradePack.Logger.LogWarning(
                $"Safe Trigger had no master-generated random roll for {item.GetType().Name} use {useNumber}; this use will consume normally.");
        }

        roll = 1f;
        return false;
    }

    private static bool PublishRollBatch(ItemEquippable itemEquippable, int firstUseNumber, int count) {
        if (!itemEquippable || firstUseNumber < 1 || count <= 0 || !SemiFunc.IsMasterClientOrSingleplayer())
            return false;

        PhotonView? photonView = null;
        if (SemiFunc.IsMultiplayer()) {
            photonView = itemEquippable.GetComponent<PhotonView>();
            if (!photonView || photonView.ViewID == 0) return false;
        }

        var rolls = new float[count];
        for (var i = 0; i < rolls.Length; i++) rolls[i] = UnityEngine.Random.value;

        var state = ItemUseStates.GetValue(itemEquippable, _ => new ItemUseState());
        CacheRolls(state, firstUseNumber, rolls);
        var itemUpgrade = itemEquippable.GetComponent<ItemUpgrade>();
        if (itemUpgrade)
            CacheRolls(UpgradeItemUseStates.GetValue(itemUpgrade, _ => new ItemUseState()), firstUseNumber, rolls);

        if (photonView) photonView.RPC(nameof(SafeTriggerRollSync.ReceiveRollsRPC), RpcTarget.Others,
            firstUseNumber, rolls);
        return true;
    }

    private static void CacheRolls(ItemUseState state, int firstUseNumber, float[] rolls) {
        for (var i = 0; i < rolls.Length; i++) {
            var value = rolls[i];
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f) continue;
            var useNumber = firstUseNumber + i;
            state.CachedRolls[useNumber] = value;
            state.HighestCachedRoll = Mathf.Max(state.HighestCachedRoll, useNumber);
        }
    }

    private static void LogRuntimeWarning(System.Exception exception) {
        if (_runtimeWarningLogged) return;
        _runtimeWarningLogged = true;
        SLRUpgradePack.Logger.LogWarning(
            $"Safe Trigger could not determine whether to preserve a charge or item; vanilla use will continue. {exception}");
    }
}

internal sealed class SafeTriggerRollSync : MonoBehaviour {
    private IEnumerator Start() {
        // Let Photon finish assigning the view ID and let every peer attach this receiver before
        // the master's first cache RPC is sent.
        yield return null;
        for (var frame = 0; frame < 5; frame++) {
            var photonView = GetComponent<PhotonView>();
            if (photonView && photonView.ViewID != 0) break;
            yield return null;
        }

        var itemEquippable = GetComponent<ItemEquippable>();
        if (itemEquippable) SafeTriggerRoll.PublishInitialRolls(itemEquippable);
    }

    [PunRPC]
    public void ReceiveRollsRPC(int firstUseNumber, float[] rolls, PhotonMessageInfo info) {
        if (!SemiFunc.MasterOnlyRPC(info)) return;
        var itemEquippable = GetComponent<ItemEquippable>();
        if (itemEquippable) SafeTriggerRoll.ReceiveRolls(itemEquippable, firstUseNumber, rolls);
    }
}

[HarmonyPatch(typeof(ItemEquippable), "Start")]
internal static class ItemEquippableSafeTriggerRollSyncPatch {
    private static void Postfix(ItemEquippable __instance) {
        if (!__instance || !NeedsSafeTriggerRollSync(__instance)) return;
        if (!__instance.GetComponent<SafeTriggerRollSync>())
            __instance.gameObject.AddComponent<SafeTriggerRollSync>();
    }

    private static bool NeedsSafeTriggerRollSync(ItemEquippable itemEquippable) =>
        itemEquippable.GetComponent<ItemUpgrade>() || itemEquippable.GetComponent<ItemGrenade>() ||
        itemEquippable.GetComponent<ItemMine>() || itemEquippable.GetComponent<ItemHealthPack>() ||
        itemEquippable.GetComponent<ItemReviveItem>();
}

[HarmonyPatch(typeof(ItemUpgrade), "PlayerUpgrade")]
[HarmonyPriority(Priority.Last)]
internal static class ItemUpgradeSafeTriggerPatch {
    private sealed class PatchState {
        internal bool Preserved;
        internal SafeTriggerRoll.UpgradePurchaseCountTransaction? PurchaseCountTransaction;
    }

    private static void Prefix(ItemUpgrade __instance, bool ___upgradeDone, bool ___isPlayerUpgrade,
        ItemToggle ___itemToggle, out PatchState __state) {
        __state = new PatchState();
        if (___upgradeDone || !___isPlayerUpgrade || !___itemToggle || !___itemToggle.toggleState) return;
        __state.Preserved = SafeTriggerRoll.BeginUpgradeItemUse(__instance, ___itemToggle);
        if (!__state.Preserved) return;

        if (!SafeTriggerRoll.TryTemporarilyRemoveUpgradePurchaseCount(__instance,
                out __state.PurchaseCountTransaction)) {
            // If this game version no longer exposes the purchase record here, fail closed and let
            // the vanilla path consume the upgrade rather than preserve it without accounting.
            SafeTriggerRoll.CancelUpgradeItemPreservation(__instance);
            __state.Preserved = false;
        }
    }

    private static void Postfix(ItemUpgrade __instance, PatchState __state, ref bool ___upgradeDone) {
        if (!SafeTriggerRoll.CompleteUpgradeItemUse(__instance, __state.Preserved)) return;

        // PlayerUpgrade still grants its effect and feedback, but the preserved pickup must be
        // visible and eligible for another interaction after the original method finishes.
        ___upgradeDone = false;
        var mesh = __instance.transform.Find("Mesh");
        if (mesh) {
            var renderer = mesh.GetComponent<MeshRenderer>();
            if (renderer) renderer.enabled = true;
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, ItemUpgrade __instance, PatchState __state) {
        __state.PurchaseCountTransaction?.Restore();
        if (__exception != null) SafeTriggerRoll.CompleteUpgradeItemUse(__instance, __state.Preserved);
        return __exception;
    }
}

[HarmonyPatch]
internal static class UpgradeItemDestroySafeTriggerPatch {
    private static MethodBase? TargetMethod() => AccessTools.Method(
        typeof(PhysGrabObjectImpactDetector), nameof(PhysGrabObjectImpactDetector.DestroyObject),
        new[] { typeof(bool), typeof(bool) });

    private static bool Prefix(PhysGrabObjectImpactDetector __instance) {
        if (!__instance) return true;
        var itemUpgrade = __instance.GetComponent<ItemUpgrade>();
        return !itemUpgrade || !SafeTriggerRoll.IsUpgradeItemPreserved(itemUpgrade);
    }
}

[HarmonyPatch(typeof(ItemBattery), nameof(ItemBattery.RemoveFullBar))]
internal static class RemoveFullBarSafeTriggerPatch {
    private static bool Prefix(ItemBattery __instance, int _bars) {
        if (_bars <= 0 || !__instance || __instance.batteryLife <= 0f) return true;
        var shootingItem = __instance.GetComponent<ItemGun>();
        if (!SafeTriggerRoll.ShouldPreserveCharge(__instance, !shootingItem)) return true;

        // If the state-sync API is unavailable after a game update, consume the bar normally rather
        // than leave clients believing a charge was preserved when only the host did.
        if (!SafeTriggerRoll.SyncPreservedCharge(__instance)) return true;

        if (shootingItem)
            SLRUpgradePack.Logger.LogDebug(
                $"Safe Trigger preserved a charge for shooting item {shootingItem.GetType().Name}.");
        return false;
    }
}

[HarmonyPatch(typeof(ItemBattery), "DrainContinuous")]
internal static class DrainContinuousSafeTriggerPatch {
    private static bool Prefix(ItemBattery __instance, float _rate, float _deltaTime) {
        if (!__instance || __instance.batteryLife <= 0f || _rate <= 0f || _deltaTime <= 0f) return true;
        return !SafeTriggerRoll.ShouldPreserveCharge(__instance, false);
    }
}

[HarmonyPatch(typeof(ItemGun), "ShootRPC")]
internal static class ItemGunSafeTriggerPatch {
    private static bool Prefix(ItemGun __instance, ref float ___batteryDrain, bool ___batteryDrainFullBar,
        ItemBattery ___itemBattery, out float __state) {
        __state = ___batteryDrain;
        if (!__instance || ___batteryDrainFullBar || ___batteryDrain <= 0f) return true;

        var itemEquippable = __instance.GetComponent<ItemEquippable>();
        if (itemEquippable && SafeTriggerRoll.ShouldPreserveCharge(itemEquippable, false) &&
            SafeTriggerRoll.SyncPreservedCharge(___itemBattery)) {
            ___batteryDrain = 0f;
            SLRUpgradePack.Logger.LogDebug(
                $"Safe Trigger preserved a charge for shooting item {__instance.GetType().Name}.");
        }

        return true;
    }

    private static void Postfix(ref float ___batteryDrain, float __state) => ___batteryDrain = __state;
}

// Several equipped items and vehicles drain ItemBattery.batteryLife directly instead of going
// through one of ItemBattery's public drain methods. Keep those writes on the host-authored sync path.
[HarmonyPatch]
internal static class DirectBatteryLifeSafeTriggerPatch {
    private static IEnumerable<MethodBase> TargetMethods() {
        var methods = new[] {
            AccessTools.Method(typeof(ItemLadder), "Update"),
            AccessTools.Method(typeof(ItemRubberDuck), "Quack"),
            AccessTools.Method(typeof(ItemMelee), "EnemyOrPVPSwingHitRPC"),
            AccessTools.Method(typeof(ItemMelee), "SwingHitRPC"),
            AccessTools.Method(typeof(ItemMeleeWhip), "ReleasePhysics"),
            AccessTools.Method(typeof(ItemMeleeWhip), "CrackHitWindowStart"),
            AccessTools.Method(typeof(ItemVehicle), "UpdateImpactDetection"),
            AccessTools.Method(typeof(ItemVehicle), "ApplyEnemyHitDurabilityLoss")
        };

        foreach (var method in methods)
            if (method != null) yield return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod) {
        var patched = new List<CodeInstruction>(instructions);
        var replacements = 0;
        for (var index = 0; index < patched.Count; index++) {
            var instruction = patched[index];
            if (SafeTriggerRoll.BatteryLifeField != null && SafeTriggerRoll.SetBatteryLifeMethod != null &&
                instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, SafeTriggerRoll.BatteryLifeField)) {
                // ItemLadder.Update also force-zeros the battery when its broken state is entered;
                // only replace the explicit extension-drain subtraction, not that state reset.
                var ladderStateReset = __originalMethod.DeclaringType == typeof(ItemLadder) &&
                                       __originalMethod.Name == "Update" &&
                                       (index == 0 || patched[index - 1].opcode != OpCodes.Sub);
                if (ladderStateReset) continue;

                instruction.opcode = OpCodes.Call;
                instruction.operand = SafeTriggerRoll.SetBatteryLifeMethod;
                replacements++;
            }
        }

        if (replacements == 0)
            SLRUpgradePack.Logger.LogError(
                $"No direct ItemBattery.batteryLife writes found in {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}; Safe Trigger may miss this drain.");

        return patched;
    }
}

[HarmonyPatch(typeof(ItemGrenade), "TickEndRPC")]
internal static class ItemGrenadeSafeTriggerPatch {
    private static bool Prefix(ItemGrenade __instance, PhotonMessageInfo _info, ItemEquippable ___itemEquippable,
        PlayerAvatar ___activatorPlayerAvatar) {
        if (!SemiFunc.MasterOnlyRPC(_info) || !__instance || !___itemEquippable || ___itemEquippable.IsEquipped() ||
            __instance.isSpawnedGrenade || SemiFunc.RunIsShop() ||
            SafeTriggerRoll.IsLevelShop ||
            !SafeTriggerRoll.CanResetGrenade) return true;

        if (!SafeTriggerRoll.ShouldPreserveSingleUseItem(___itemEquippable, "grenade")) return true;

        __instance.onDetonate?.Invoke();
        if (SemiFunc.IsMasterClientOrSingleplayer() && ___activatorPlayerAvatar)
            SemiFunc.PlayerShopKeeperExplosionDetonatedHost(___activatorPlayerAvatar);

        SafeTriggerRoll.ResetGrenadeAfterPreservedUse(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(ItemHealthPack), "Update")]
internal static class ItemHealthPackSafeTriggerUpdatePatch {
    private static void Prefix(ItemHealthPack __instance, out SafeTriggerRoll.ItemRemovalContext? __state) {
        __state = SafeTriggerRoll.BeginHealthPackUpdate(__instance);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, SafeTriggerRoll.ItemRemovalContext? __state) {
        SafeTriggerRoll.RestoreItemRemovalContext(__state);
        return __exception;
    }
}

[HarmonyPatch]
internal static class SafeTriggerItemInventoryRemovalPatch {
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(StatsManager),
        nameof(StatsManager.ItemRemove), new[] { typeof(string) });

    private static bool Prefix(string instanceName) => !SafeTriggerRoll.TryPreserveActiveItemRemoval(instanceName);
}

[HarmonyPatch(typeof(ItemHealthPack), "UsedRPC")]
internal static class ItemHealthPackSafeTriggerUsedPatch {
    private static readonly MethodInfo? DestroyMethod = AccessTools.Method(typeof(Object), nameof(Object.Destroy),
        new[] { typeof(Object) });
    private static readonly MethodInfo? ReplacementMethod = AccessTools.Method(
        typeof(SafeTriggerRoll), nameof(SafeTriggerRoll.DestroyHealthPackEquippableIfConsumed));

    [HarmonyPrefix]
    private static void Prefix(ItemHealthPack __instance, PhotonMessageInfo _info) {
        if (SemiFunc.MasterOnlyRPC(_info)) SafeTriggerRoll.EnsureHealthPackOutcome(__instance);
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        ReplaceInstanceCallWithItemArgument(instructions, DestroyMethod, ReplacementMethod);

    private static IEnumerable<CodeInstruction> ReplaceInstanceCallWithItemArgument(
        IEnumerable<CodeInstruction> instructions, MethodInfo? originalMethod, MethodInfo? replacementMethod) {
        var replacements = 0;
        foreach (var instruction in instructions) {
            if (originalMethod != null && replacementMethod != null && instruction.Calls(originalMethod)) {
                var loadItem = new CodeInstruction(OpCodes.Ldarg_0);
                loadItem.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacementMethod;
                replacements++;
                yield return loadItem;
            }

            yield return instruction;
        }

        if (replacements != 1)
            SLRUpgradePack.Logger.LogError(
                $"Expected one ItemHealthPack.UsedRPC item destruction call, replaced {replacements}; " +
                "single-use preservation may not apply.");
    }

    [HarmonyPostfix]
    private static void Postfix(ItemHealthPack __instance, PhotonMessageInfo _info, ref bool ___used,
        ItemAttributes ___itemAttributes) {
        if (!SemiFunc.MasterOnlyRPC(_info)) return;
        if (SafeTriggerRoll.CompleteHealthPackUse(__instance)) {
            ___used = false;
            if (___itemAttributes) ___itemAttributes.DisableUI(false);
        }
    }
}

[HarmonyPatch(typeof(ItemHealthPack), "Update")]
internal static class ItemHealthPackSafeTriggerReusablePatch {
    [HarmonyPostfix]
    private static void Postfix(ItemHealthPack __instance, ItemToggle ___itemToggle,
        ItemAttributes ___itemAttributes) {
        if (!SafeTriggerRoll.CompleteHealthPackUpdate(__instance)) return;
        if (___itemToggle) ___itemToggle.ToggleDisable(false);
        if (___itemAttributes) ___itemAttributes.DisableUI(false);
    }
}

[HarmonyPatch(typeof(ItemReviveItem), "ReviveAndDestroy")]
internal static class ItemReviveItemSafeTriggerPatch {
    private static void Prefix(ItemReviveItem __instance, out SafeTriggerRoll.ItemRemovalContext? __state) {
        __state = SafeTriggerRoll.BeginReviveItemUse(__instance);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, SafeTriggerRoll.ItemRemovalContext? __state) {
        SafeTriggerRoll.RestoreItemRemovalContext(__state);
        return __exception;
    }
}

[HarmonyPatch]
internal static class ReviveItemDestroySafeTriggerPatch {
    private static MethodBase? TargetMethod() => AccessTools.Method(
        typeof(PhysGrabObjectImpactDetector), nameof(PhysGrabObjectImpactDetector.DestroyObject),
        new[] { typeof(bool), typeof(bool) });

    private static bool Prefix(PhysGrabObjectImpactDetector __instance) =>
        !SafeTriggerRoll.TryPreserveActiveReviveItemDestruction(__instance);
}

[HarmonyPatch(typeof(ItemMine), nameof(ItemMine.DestroyMine))]
internal static class ItemMineSafeTriggerPatch {
    private static bool Prefix(ItemMine __instance, ref bool ___mineDestroyed, ItemEquippable ___itemEquippable) {
        if (!__instance || ___mineDestroyed || SemiFunc.RunIsShop() ||
            SafeTriggerRoll.IsLevelShop ||
            !SafeTriggerRoll.CanResetMine ||
            !SafeTriggerRoll.ShouldPreserveSingleUseItem(___itemEquippable, "mine")) return true;

        // Guard against another client update calling DestroyMine again before the host's reset RPC arrives.
        ___mineDestroyed = true;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return false;

        SafeTriggerRoll.ResetMineAfterPreservedUse(__instance, ref ___mineDestroyed);
        return false;
    }
}

[HarmonyPatch(typeof(ItemMine), "StateSetRPC")]
internal static class ItemMineSafeTriggerStatePatch {
    private static void Postfix(int newState, ref bool ___mineDestroyed) {
        if (newState == 0) ___mineDestroyed = false;
    }
}
