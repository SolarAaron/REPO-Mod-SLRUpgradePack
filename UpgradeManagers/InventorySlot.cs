using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class InventorySlotUpgrade : UpgradeBase<int> {
    internal static readonly Dictionary<string, Dictionary<int, int>> serverMonitoredInventoryItems = new();
    public ConfigEntry<KeyboardShortcut> ItemSlot4 { get; }
    public ConfigEntry<KeyboardShortcut> ItemSlot5 { get; }
    public ConfigEntry<KeyboardShortcut> ItemSlot6 { get; }
    public ConfigEntry<KeyboardShortcut> ItemSlot7 { get; }
    public ConfigEntry<KeyboardShortcut> ItemSlot8 { get; }
    public ConfigEntry<KeyboardShortcut> ItemSlot9 { get; }
    internal string? BoundPlayer { get; set; }
    public Inventory? InventoryRef { get; set; }
    public InventoryUI? UIRef { get; set; }
    private Coroutine? _deferredInventoryRefresh;

    public InventorySlotUpgrade(bool enabled, int upgradeAmount, ConfigFile config, AssetBundle assetBundle,
        float priceMultiplier) :
        base("Inventory Slot", "assets/repo/mods/resources/items/items/item upgrade inventory slot lib.asset", enabled,
            upgradeAmount, false, 0, config, assetBundle, priceMultiplier, false, false, 6) {
        const string section = "Inventory Slot Upgrade";
        ItemSlot4 = config.Bind(section, "Item Slot 4", new KeyboardShortcut(KeyCode.Z),
            "Key to select inventory slot 4.");
        ItemSlot5 = config.Bind(section, "Item Slot 5", new KeyboardShortcut(KeyCode.X),
            "Key to select inventory slot 5.");
        ItemSlot6 = config.Bind(section, "Item Slot 6", new KeyboardShortcut(KeyCode.C),
            "Key to select inventory slot 6.");
        ItemSlot7 = config.Bind(section, "Item Slot 7", new KeyboardShortcut(KeyCode.N),
            "Key to select inventory slot 7.");
        ItemSlot8 = config.Bind(section, "Item Slot 8", new KeyboardShortcut(KeyCode.M),
            "Key to select inventory slot 8.");
        ItemSlot9 = config.Bind(section, "Item Slot 9", new KeyboardShortcut(KeyCode.Comma),
            "Key to select inventory slot 9.");
    }

    internal ConfigEntry<KeyboardShortcut>? GetSlotBinding(int index) => index switch {
        3 => ItemSlot4,
        4 => ItemSlot5,
        5 => ItemSlot6,
        6 => ItemSlot7,
        7 => ItemSlot8,
        8 => ItemSlot9,
        _ => null
    };

    public override int Calculate(int value, PlayerAvatar? player, int level) {
        return value + level;
    }

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        if (player == SemiFunc.PlayerAvatarLocal()) {
            BoundPlayer = SemiFunc.PlayerGetSteamID(player);
            RefreshLocalInventory();
        }
    }

    internal override void UseUpgrade(PlayerAvatar player, int level) {
        base.UseUpgrade(player, level);
        if (player == SemiFunc.PlayerAvatarLocal()) {
            BoundPlayer = SemiFunc.PlayerGetSteamID(player);
            RefreshLocalInventory();
            ScheduleLocalInventoryRefresh();
        }
    }

    private void RefreshLocalInventory() {
        if (InventoryRef) InventoryStartPatch.Postfix(InventoryRef!);
        if (UIRef) InventoryUIStartPatch.Postfix(UIRef!);
    }

    private void ScheduleLocalInventoryRefresh() {
        if (_deferredInventoryRefresh != null)
            SLRUpgradePack.Instance.StopCoroutine(_deferredInventoryRefresh);

        _deferredInventoryRefresh = SLRUpgradePack.Instance.StartCoroutine(RefreshLocalInventoryNextFrame());
    }

    private IEnumerator RefreshLocalInventoryNextFrame() {
        // Upgrade use can occur while the current frame is still laying out the inventory
        // UI. Give Unity's newly cloned InventorySpots a frame to run Start() before fixing
        // their positions; otherwise Start/layout work can put them back at the template slot.
        yield return null;
        _deferredInventoryRefresh = null;
        RefreshLocalInventory();
    }
}

[HarmonyPatch(typeof(InventoryUI), "Start")]
public class InventoryUIStartPatch {
    private static readonly FieldRef<InventoryUI, List<GameObject>> allChildren =
        FieldRefAccess<InventoryUI, List<GameObject>>("allChildren");

    private static readonly FieldRef<SemiUI, Vector3> initialPosition =
        FieldRefAccess<SemiUI, Vector3>("initialPosition");

    private static readonly FieldRef<SemiUI, Vector2> hidePositionCurrent =
        FieldRefAccess<SemiUI, Vector2>("hidePositionCurrent");

    private static readonly FieldRef<InventorySpot, ItemEquippable> currentItem =
        FieldRefAccess<InventorySpot, ItemEquippable>("<CurrentItem>k__BackingField");

    private static readonly FieldRef<BatteryVisualLogic, ItemBattery> previousItemBattery =
        FieldRefAccess<BatteryVisualLogic, ItemBattery>("itemBatteryPrev");

    private static readonly FieldRef<BatteryVisualLogic, List<GameObject>> batteryBars =
        FieldRefAccess<BatteryVisualLogic, List<GameObject>>("bars");

    private static readonly FieldRef<InventorySpot, BatteryVisualLogic> inventorySpotBatteryVisual =
        FieldRefAccess<InventorySpot, BatteryVisualLogic>("batteryVisualLogic");

    private static readonly FieldRef<BatteryVisualLogic, Vector3> batteryTargetPosition =
        FieldRefAccess<BatteryVisualLogic, Vector3>("targetPosition");

    private static readonly FieldRef<BatteryVisualLogic, Vector3> batteryTargetPositionOriginal =
        FieldRefAccess<BatteryVisualLogic, Vector3>("targetPositionOriginal");

    private static readonly FieldRef<BatteryVisualLogic, float> batteryTargetScale =
        FieldRefAccess<BatteryVisualLogic, float>("targetScale");

    private static readonly FieldRef<BatteryVisualLogic, float> batteryTargetScaleOriginal =
        FieldRefAccess<BatteryVisualLogic, float>("targetScaleOriginal");

    private static readonly FieldRef<BatteryVisualLogic, float> batteryTargetRotation =
        FieldRefAccess<BatteryVisualLogic, float>("targetRotation");

    private static readonly FieldRef<BatteryVisualLogic, float> batteryTargetRotationOriginal =
        FieldRefAccess<BatteryVisualLogic, float>("targetRotationOriginal");

    private static readonly FieldRef<BatteryVisualLogic, bool> batteryDoOutro =
        FieldRefAccess<BatteryVisualLogic, bool>("doOutro");

    private static readonly FieldRef<BatteryVisualLogic, SpringFloat> batterySpringScale =
        FieldRefAccess<BatteryVisualLogic, SpringFloat>("springScale");

    private static readonly FieldRef<BatteryVisualLogic, SpringFloat> batterySpringRotation =
        FieldRefAccess<BatteryVisualLogic, SpringFloat>("springRotation");

    private static readonly FieldRef<BatteryVisualLogic, SpringVector3> batterySpringPosition =
        FieldRefAccess<BatteryVisualLogic, SpringVector3>("springPosition");

    private static readonly FieldRef<SpringFloat, float> springFloatDamping =
        FieldRefAccess<SpringFloat, float>("damping");

    private static readonly FieldRef<SpringFloat, float> springFloatSpeed =
        FieldRefAccess<SpringFloat, float>("speed");

    private static readonly FieldRef<SpringFloat, float> springFloatLastPosition =
        FieldRefAccess<SpringFloat, float>("lastPosition");

    private static readonly FieldRef<SpringVector3, float> springVectorDamping =
        FieldRefAccess<SpringVector3, float>("damping");

    private static readonly FieldRef<SpringVector3, float> springVectorSpeed =
        FieldRefAccess<SpringVector3, float>("speed");

    private static readonly FieldRef<SpringVector3, Vector3> springVectorLastPosition =
        FieldRefAccess<SpringVector3, Vector3>("lastPosition");

    private static readonly FieldRef<InventoryBattery, Vector3> originalBatteryScale =
        FieldRefAccess<InventoryBattery, Vector3>("originalLocalScale");

    private static readonly Dictionary<int, (int frame, int batteryId)> batteryVisualRefreshes = new();
    private static readonly Dictionary<int, TextMeshProUGUI> slotNumberLabels = new();
    private static readonly Dictionary<int, (Vector3 position, float scale, float rotation)> clonedBatteryVisualsPendingStart = new();
    private static int batteryVisualRefreshFrame = -1;

    internal static void Postfix(InventoryUI __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        inventorySlotUpgrade.UIRef = __instance;
        var upgradeRegister = inventorySlotUpgrade.UpgradeRegister;
        var boundPlayer = inventorySlotUpgrade.BoundPlayer;
        if (inventorySlotUpgrade.UpgradeEnabled.Value && boundPlayer != null && upgradeRegister != null) {
            var slots = inventorySlotUpgrade.Calculate(3, SemiFunc.PlayerAvatarLocal(),
                upgradeRegister.GetLevel(boundPlayer));

            if (slots > 3) {
                SLRUpgradePack.Logger.LogInfo($"Redrawing for {slots} slots");
                var num = -(slots * 40) / 2f + 20f;
                var existingSpots = __instance.GetComponentsInChildren<InventorySpot>(true)
                    .Where(spot => spot)
                    .GroupBy(spot => spot.inventorySpotIndex)
                    .ToDictionary(group => group.Key, group => group.First());

                if (!existingSpots.TryGetValue(0, out var templateSpot) || !templateSpot) {
                    SLRUpgradePack.Logger.LogWarning("Could not resize inventory UI: no base InventorySpot template was found.");
                    return;
                }

                var templateBatteryVisual = templateSpot.GetComponentInChildren<BatteryVisualLogic>(true);
                var templateSemiUI = templateSpot.GetComponent<SemiUI>();
                var templateShowPosition = templateSemiUI ? templateSemiUI.showPosition : (Vector2)templateSpot.transform.localPosition;
                var templateHideOffset = templateSemiUI
                    ? templateSemiUI.hidePosition - templateShowPosition
                    : Vector2.zero;
                var templateHideCurrentOffset = templateSemiUI
                    ? hidePositionCurrent.Invoke(templateSemiUI) - templateShowPosition
                    : templateHideOffset;
                var templateInitialOffset = templateSemiUI
                    ? initialPosition.Invoke(templateSemiUI) - (Vector3)templateShowPosition
                    : Vector3.zero;
                var detachedBatteryTemplate = __instance.GetComponentsInChildren<InventoryBattery>(true)
                    .FirstOrDefault(battery => battery && battery.inventorySpot == templateSpot.inventorySpotIndex &&
                                               !battery.GetComponentInParent<InventorySpot>());
                var detachedBatteryScale = detachedBatteryTemplate
                    ? GetBatteryDisplayScale(detachedBatteryTemplate)
                    : Vector3.zero;
                var detachedBatteryPositionOffset = detachedBatteryTemplate
                    ? templateSpot.transform.InverseTransformPoint(detachedBatteryTemplate.transform.position)
                    : Vector3.zero;
                var detachedBatteryRotationOffset = detachedBatteryTemplate
                    ? Quaternion.Inverse(templateSpot.transform.rotation) * detachedBatteryTemplate.transform.rotation
                    : Quaternion.identity;

                for (var j = 0; j < slots; j++) {
                    var position = new Vector2(num + j * 40f, -175.3f);
                    if (existingSpots.TryGetValue(j, out var existingSpot) && existingSpot) {
                        SetSpotPosition(existingSpot, position, templateShowPosition, templateHideOffset,
                            templateHideCurrentOffset, templateInitialOffset, true);
                        continue;
                    }

                    var val = Object.Instantiate(templateSpot.transform, templateSpot.transform.parent, false);
                    val.gameObject.name = $"Inventory Spot {j + 1}";
                    // InventorySpot.Start() searches active children for BatteryVisualLogic, then
                    // disables it. Cloning a started slot therefore copies that child as inactive;
                    // reactivate it so the cloned slot can initialize its reference on Start().
                    var batteryVisual = val.GetComponentInChildren<BatteryVisualLogic>(true);
                    if (batteryVisual) batteryVisual.gameObject.SetActive(true);

                    var component = val.GetComponent<InventorySpot>();
                    if (!component) {
                        Object.Destroy(val.gameObject);
                        SLRUpgradePack.Logger.LogWarning($"Could not create inventory slot {j + 1}: cloned template has no InventorySpot.");
                        continue;
                    }

                    component.inventorySpotIndex = j;
                    currentItem.Invoke(component) = null!;
                    if (batteryVisual) {
                        inventorySpotBatteryVisual.Invoke(component) = batteryVisual;
                        batteryVisual.itemBattery = null!;
                        previousItemBattery.Invoke(batteryVisual) = null!;
                        RebindBatteryBars(batteryVisual);
                        if (templateBatteryVisual)
                            ResetClonedBatteryVisual(batteryVisual, templateBatteryVisual);
                    }

                    foreach (var batteryIndicator in component.GetComponentsInChildren<InventoryBattery>(true))
                        batteryIndicator.inventorySpot = j;
                    RestoreClonedBatteryScales(templateSpot, val);
                    InventorySpotUpdatePatch.RefreshSlotBindingLabel(component);
                    SetSpotPosition(component, position, templateShowPosition, templateHideOffset,
                        templateHideCurrentOffset, templateInitialOffset, false);
                    existingSpots[j] = component;
                }

                // InventoryBattery may be a sibling overlay rather than a child of InventorySpot,
                // so cloning the slot hierarchy alone would omit it. Clone the matching overlay
                // from slot 1 when that is how this game's UI prefab is wired.
                if (detachedBatteryTemplate) {
                    var batteryIndices = new HashSet<int>();
                    foreach (var batteryIndicator in __instance.GetComponentsInChildren<InventoryBattery>(true)) {
                        var owningSpot = batteryIndicator.GetComponentInParent<InventorySpot>();
                        if (owningSpot) batteryIndicator.inventorySpot = owningSpot.inventorySpotIndex;
                        batteryIndices.Add(batteryIndicator.inventorySpot);
                    }

                    for (var j = 3; j < slots; j++) {
                        if (batteryIndices.Contains(j) || !existingSpots.TryGetValue(j, out var targetSpot) || !targetSpot)
                            continue;

                        var clone = Object.Instantiate(detachedBatteryTemplate.transform,
                            detachedBatteryTemplate.transform.parent, false);
                        clone.gameObject.name = $"Inventory Battery {j + 1}";
                        clone.position = targetSpot.transform.TransformPoint(detachedBatteryPositionOffset);
                        clone.rotation = targetSpot.transform.rotation * detachedBatteryRotationOffset;
                        var batteryIndicator = clone.GetComponent<InventoryBattery>();
                        if (!batteryIndicator) {
                            Object.Destroy(clone.gameObject);
                            SLRUpgradePack.Logger.LogWarning(
                                $"Could not create battery indicator for inventory slot {j + 1}: cloned template has no InventoryBattery.");
                            continue;
                        }

                        batteryIndicator.inventorySpot = j;
                        clone.localScale = detachedBatteryScale;
                        batteryIndices.Add(j);
                    }
                }

                foreach (var (index, spot) in existingSpots) {
                    if (!spot) continue;
                    foreach (var batteryIndicator in spot.GetComponentsInChildren<InventoryBattery>(true))
                        batteryIndicator.inventorySpot = index;
                    InventoryStartPatch.RegisterSpot(spot, index);
                }

                // SemiUI caches its direct children in Start(). Rebuild that cache after
                // runtime additions so Show/Hide and redraws include every current slot.
                var children = allChildren.Invoke(__instance);
                children.Clear();
                foreach (Transform child in __instance.transform)
                    children.Add(child.gameObject);

                // New InventorySpot instances have not run Start() yet, which initializes the
                // Image fields used by UpdateUI()/SetEmoji(). Their own Start() refreshes the UI.
                Canvas.ForceUpdateCanvases();
            }
        }
    }

    private static void SetSpotPosition(InventorySpot spot, Vector2 position, Vector2 templateShowPosition,
        Vector2 templateHideOffset, Vector2 templateHideCurrentOffset, Vector3 templateInitialOffset,
        bool startHasRun) {
        var offset = position - templateShowPosition;
        var localPosition = (Vector3)position;
        localPosition.z = spot.transform.localPosition.z;
        spot.transform.localPosition = localPosition;

        var semiUI = spot.GetComponent<SemiUI>();
        if (!semiUI) return;

        // SemiUI.UpdatePositionLogic writes from these cached coordinates every frame. Moving only
        // the Transform (as the original redraw did) is therefore undone, and new slots stack on
        // the template's position. Translate the cached show/hide coordinates with each slot.
        semiUI.showPosition = position;
        // Before Start, hidePosition is still a relative offset: SemiUI.Start adds showPosition
        // to it. After Start it is already an absolute local position.
        semiUI.hidePosition = startHasRun ? position + templateHideOffset : templateHideOffset;
        hidePositionCurrent.Invoke(semiUI) = position + templateHideCurrentOffset;
        initialPosition.Invoke(semiUI) = (Vector3)position + templateInitialOffset;
    }

    private static Vector3 GetBatteryDisplayScale(InventoryBattery battery) {
        var displayScale = originalBatteryScale.Invoke(battery);
        return displayScale == Vector3.zero ? battery.transform.localScale : displayScale;
    }

    private static void RebindBatteryBars(BatteryVisualLogic batteryVisual) {
        if (!batteryVisual.batteryBarContainer) {
            SLRUpgradePack.Logger.LogWarning("Could not rebind cloned battery bars: the bar container reference is missing.");
            batteryBars.Invoke(batteryVisual) = new List<GameObject>();
            return;
        }

        // The cloned component's runtime list can still reference the template's bars. Rebuild
        // it from the cloned container so BatteryBarsSet() only clears and tracks this slot's bars.
        var bars = new List<GameObject>(batteryVisual.batteryBarContainer.childCount);
        foreach (Transform bar in batteryVisual.batteryBarContainer)
            bars.Add(bar.gameObject);
        batteryBars.Invoke(batteryVisual) = bars;
    }

    internal static BatteryVisualLogic? BindBatteryVisual(InventorySpot spot, bool activateForVanillaStart) {
        if (!spot) return null;

        var batteryVisual = inventorySpotBatteryVisual.Invoke(spot);
        if (batteryVisual && batteryVisual.transform != spot.transform &&
            !batteryVisual.transform.IsChildOf(spot.transform))
            batteryVisual = null!;

        if (!batteryVisual)
            batteryVisual = spot.GetComponentInChildren<BatteryVisualLogic>(true);
        if (!batteryVisual) return null;

        inventorySpotBatteryVisual.Invoke(spot) = batteryVisual;
        if (activateForVanillaStart && !batteryVisual.gameObject.activeSelf)
            batteryVisual.gameObject.SetActive(true);
        return batteryVisual;
    }

    private static void ResetClonedBatteryVisual(BatteryVisualLogic visual, BatteryVisualLogic template) {
        var targetPosition = batteryTargetPositionOriginal.Invoke(template);
        if (float.IsNaN(targetPosition.x) || float.IsInfinity(targetPosition.x) ||
            float.IsNaN(targetPosition.y) || float.IsInfinity(targetPosition.y) ||
            float.IsNaN(targetPosition.z) || float.IsInfinity(targetPosition.z))
            targetPosition = visual.transform.localPosition;

        var targetScale = batteryTargetScaleOriginal.Invoke(template);
        if (!IsUsableScale(targetScale))
            targetScale = batteryTargetScale.Invoke(template);
        if (!IsUsableScale(targetScale))
            targetScale = Mathf.Max(visual.transform.localScale.x, visual.transform.localScale.y);
        if (!IsUsableScale(targetScale))
            targetScale = 0.5f;

        var targetRotation = batteryTargetRotationOriginal.Invoke(template);
        if (float.IsNaN(targetRotation) || float.IsInfinity(targetRotation))
            targetRotation = 0f;

        ApplyClonedBatteryVisualState(visual, targetPosition, targetScale, targetRotation);
        clonedBatteryVisualsPendingStart[visual.GetInstanceID()] =
            (targetPosition, targetScale, targetRotation);
    }

    private static void ApplyClonedBatteryVisualState(
        BatteryVisualLogic visual, Vector3 targetPosition, float targetScale, float targetRotation) {
        visual.transform.localPosition = targetPosition;
        visual.transform.localScale = Vector3.one * targetScale;
        visual.transform.localRotation = Quaternion.Euler(0f, 0f, targetRotation);
        batteryTargetPosition.Invoke(visual) = targetPosition;
        batteryTargetPositionOriginal.Invoke(visual) = targetPosition;
        batteryTargetScale.Invoke(visual) = targetScale;
        batteryTargetScaleOriginal.Invoke(visual) = targetScale;
        batteryTargetRotation.Invoke(visual) = targetRotation;
        batteryTargetRotationOriginal.Invoke(visual) = targetRotation;
        batteryDoOutro.Invoke(visual) = false;
        ResetBatteryVisualSprings(visual, targetScale);
    }

    private static bool IsUsableScale(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.001f;

    private static void ResetBatteryVisualSprings(BatteryVisualLogic visual, float targetScale) {
        var scaleSpring = new SpringFloat();
        springFloatDamping.Invoke(scaleSpring) = 0.4f;
        springFloatSpeed.Invoke(scaleSpring) = 30f;
        springFloatLastPosition.Invoke(scaleSpring) = targetScale;
        batterySpringScale.Invoke(visual) = scaleSpring;

        var rotationSpring = new SpringFloat();
        springFloatDamping.Invoke(rotationSpring) = 0.3f;
        springFloatSpeed.Invoke(rotationSpring) = 40f;
        springFloatLastPosition.Invoke(rotationSpring) = 0f;
        batterySpringRotation.Invoke(visual) = rotationSpring;

        var positionSpring = new SpringVector3();
        springVectorDamping.Invoke(positionSpring) = 0.35f;
        springVectorSpeed.Invoke(positionSpring) = 30f;
        springVectorLastPosition.Invoke(positionSpring) = visual.transform.localPosition;
        batterySpringPosition.Invoke(visual) = positionSpring;
    }

    internal static void ResetPendingClonedBatteryVisualStart(BatteryVisualLogic visual) {
        if (!visual) return;
        var visualId = visual.GetInstanceID();
        if (!clonedBatteryVisualsPendingStart.TryGetValue(visualId, out var state)) return;
        clonedBatteryVisualsPendingStart.Remove(visualId);

        // BatteryVisualLogic.Start can capture the collapsed clone transform as its original
        // state. Re-apply the template's targets after vanilla initialization so its first
        // BatteryOutro/ResetOutro cycle uses the proper slot geometry.
        ApplyClonedBatteryVisualState(visual, state.position, state.scale, state.rotation);
    }

    internal static void ClearPendingClonedBatteryVisuals() => clonedBatteryVisualsPendingStart.Clear();

    internal static void ClearSlotNumberLabels() => slotNumberLabels.Clear();

    internal static void RefreshSlotBindingLabel(InventorySpot spot, string bindingDisplay) {
        if (!spot) return;

        if (spot.noItem && spot.noItem.text != bindingDisplay)
            spot.noItem.text = bindingDisplay;

        var spotId = spot.GetInstanceID();
        if (!slotNumberLabels.TryGetValue(spotId, out var numberLabel) || !numberLabel) {
            numberLabel = spot.transform.Find("Numbers")?.GetComponent<TextMeshProUGUI>();
            if (numberLabel)
                slotNumberLabels[spotId] = numberLabel;
            else
                slotNumberLabels.Remove(spotId);
        }

        if (numberLabel && numberLabel.text != bindingDisplay)
            numberLabel.text = bindingDisplay;
    }

    internal static void RefreshBatteryVisual(InventorySpot spot, bool forceRefresh = true) {
        if (!spot) return;

        var currentItem = spot.CurrentItem;
        var itemBattery = currentItem ? currentItem.GetComponent<ItemBattery>() : null;
        var batteryId = itemBattery ? itemBattery.GetInstanceID() : 0;
        var frame = Time.frameCount;
        if (batteryVisualRefreshFrame != frame) {
            batteryVisualRefreshes.Clear();
            batteryVisualRefreshFrame = frame;
        }

        var spotId = spot.GetInstanceID();
        if (!forceRefresh && batteryVisualRefreshes.TryGetValue(spotId, out var lastRefresh) &&
            lastRefresh.batteryId == batteryId)
            return;

        var batteryVisual = BindBatteryVisual(spot, false);

        if (!batteryVisual || !batteryVisual.batteryBarContainer) return;

        // The cloned component can retain bar objects owned by the template slot. Make sure
        // BatteryBarsSet only destroys and rebuilds bars inside this visual's own container.
        var bars = batteryBars.Invoke(batteryVisual);
        if (bars == null || bars.Count != batteryVisual.batteryBarContainer.childCount ||
            bars.Any(bar => !bar || bar.transform.parent != batteryVisual.batteryBarContainer))
            RebindBatteryBars(batteryVisual);

        batteryVisual.itemBattery = itemBattery!;
        previousItemBattery.Invoke(batteryVisual) = itemBattery!;
        if (itemBattery) {
            if (!batteryVisual.gameObject.activeSelf)
                batteryVisual.gameObject.SetActive(true);
            if (batteryTargetScale.Invoke(batteryVisual) == 0f)
                batteryVisual.ResetOutro();
        }

        batteryVisual.BatteryBarsSet();
        if (itemBattery)
            batteryVisual.BatteryBarsUpdate(-1, true);
        else
            batteryVisual.BatteryOutro();

        batteryVisualRefreshes[spotId] = (frame, batteryId);
    }

    private static void RestoreClonedBatteryScales(InventorySpot templateSpot, Transform clonedSpot) {
        var templateBatteries = templateSpot.GetComponentsInChildren<InventoryBattery>(true);
        var clonedBatteries = clonedSpot.GetComponentsInChildren<InventoryBattery>(true);
        for (var i = 0; i < templateBatteries.Length && i < clonedBatteries.Length; i++) {
            if (templateBatteries[i] && clonedBatteries[i])
                clonedBatteries[i].transform.localScale = GetBatteryDisplayScale(templateBatteries[i]);
        }
    }
}

[HarmonyPatch(typeof(InventorySpot), "EquipItem")]
public class InventorySpotEquipBatteryVisualPatch {
    private static void Postfix(InventorySpot __instance) {
        // UpdateUI also refreshes the visual; this fallback covers equip paths that do not
        // invoke it and is deduplicated when both callbacks occur in the same frame.
        InventoryUIStartPatch.RefreshBatteryVisual(__instance, false);
    }
}

[HarmonyPatch(typeof(InventorySpot), "UnequipItem")]
public class InventorySpotUnequipBatteryVisualPatch {
    private static void Postfix(InventorySpot __instance) {
        InventoryUIStartPatch.RefreshBatteryVisual(__instance, false);
    }
}

[HarmonyPatch(typeof(InventorySpot), "UpdateUI")]
public class InventorySpotUpdateUIBatteryVisualPatch {
    private static void Postfix(InventorySpot __instance) {
        InventoryUIStartPatch.RefreshBatteryVisual(__instance);
    }
}

[HarmonyPatch(typeof(InventorySpot), "Start")]
public class InventorySpotStartPatch {
    private static void Prefix(InventorySpot __instance) {
        // Runtime clones start from the already-started first slot, whose battery visual child
        // is inactive. Activate and bind it before vanilla Start performs its active-child lookup.
        InventoryUIStartPatch.BindBatteryVisual(__instance, __instance.inventorySpotIndex >= 3);
    }

    private static void Postfix(InventorySpot __instance) {
        InventoryUIStartPatch.BindBatteryVisual(__instance, false);
    }
}

[HarmonyPatch(typeof(BatteryVisualLogic), "Start")]
public class BatteryVisualLogicStartPatch {
    private static void Postfix(BatteryVisualLogic __instance) {
        InventoryUIStartPatch.ResetPendingClonedBatteryVisualStart(__instance);
    }
}

[HarmonyPatch(typeof(InventorySpot), "StateOccupied")]
public class InventorySpotStateOccupiedBatteryVisualPatch {
    private static void Prefix(InventorySpot __instance) {
        InventoryUIStartPatch.BindBatteryVisual(__instance, false);
    }
}

[HarmonyPatch(typeof(StatsManager), "PlayerInventoryUpdate")]
public class StatsManagerUpdatePatch {
    private static void Postfix(StatsManager __instance, string _steamID, string itemName, int spot, bool sync) {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || spot < 3) {
            return;
        }

        var flag = InventorySlotUpgrade.serverMonitoredInventoryItems.TryGetValue(_steamID, out var value);
        if (string.IsNullOrEmpty(itemName)) {
            if (flag) {
                value.Remove(spot);
                if (value.Count == 0) {
                    InventorySlotUpgrade.serverMonitoredInventoryItems.Remove(_steamID);
                }
            }
        } else {
            if (!flag) {
                InventorySlotUpgrade.serverMonitoredInventoryItems.Add(_steamID, value = new Dictionary<int, int>());
            }

            value[spot] = itemName.GetHashCode();
        }
    }
}

[HarmonyPatch(typeof(MainMenuOpen), "Start")]
public class MainMenuOpenStartPatch {
    private static void Postfix(MainMenuOpen __instance) {
        InventorySlotUpgrade.serverMonitoredInventoryItems.Clear();
        InventoryUIStartPatch.ClearPendingClonedBatteryVisuals();
        InventoryUIStartPatch.ClearSlotNumberLabels();
    }
}

[HarmonyPatch(typeof(Inventory), "Start")]
public class InventoryStartPatch {
    private static readonly FieldRef<Inventory, List<InventorySpot>> inventorySpots =
        FieldRefAccess<Inventory, List<InventorySpot>>("inventorySpots");

    internal static void Postfix(Inventory __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        inventorySlotUpgrade.InventoryRef = __instance;
        var upgradeRegister = inventorySlotUpgrade.UpgradeRegister;
        var boundPlayer = inventorySlotUpgrade.BoundPlayer;
        if (inventorySlotUpgrade.UpgradeEnabled.Value && boundPlayer != null && upgradeRegister != null) {
            var extraSlots = inventorySlotUpgrade.Calculate(3, SemiFunc.PlayerAvatarLocal(),
                upgradeRegister.GetLevel(boundPlayer));

            SLRUpgradePack.Logger.LogInfo($"Player has {extraSlots} slots");
            while (inventorySpots.Invoke(__instance).Count < extraSlots)
                inventorySpots.Invoke(__instance).Add(null!);
        }
    }

    internal static void RegisterSpot(InventorySpot spot, int index) {
        // Vanilla slots register themselves through InventorySpot.LateStart. Only register the
        // added slots here, so refreshing the UI doesn't disturb the game's base inventory list.
        if (index < 3 || !spot || SemiFunc.RunIsArena()) return;

        var inventory = Inventory.instance;
        if (!inventory) return;

        var spots = inventorySpots.Invoke(inventory);
        while (spots.Count <= index)
            spots.Add(null!);

        inventory.InventorySpotAddAtIndex(spot, index);
    }
}

internal sealed class InventorySlotRequestValidationContext {
    internal int RequestedSlotIndex { get; }
    internal int AllowedSlotCount { get; }
    internal bool NeedsInventoryList { get; set; } = true;

    internal InventorySlotRequestValidationContext(int requestedSlotIndex, int allowedSlotCount) {
        RequestedSlotIndex = requestedSlotIndex;
        AllowedSlotCount = allowedSlotCount;
    }
}

internal static class InventorySlotRequestValidation {
    [ThreadStatic]
    private static InventorySlotRequestValidationContext? _current;

    internal static InventorySlotRequestValidationContext? Begin(int requestedSlotIndex,
        int requesterPhotonViewId) {
        var previous = _current;
        _current = CreateContext(requestedSlotIndex, requesterPhotonViewId);
        return previous;
    }

    internal static void Restore(InventorySlotRequestValidationContext? previous) => _current = previous;

    internal static bool TryOverrideInventoryList(List<InventorySpot> original,
        out List<InventorySpot> validationList) {
        validationList = original;
        var context = _current;
        if (context == null || !context.NeedsInventoryList) return false;

        // RPC_RequestEquip calls GetAllSpots once for its range check. Consume the context here so
        // any nested inventory queries by the original RPC continue to see the real host inventory.
        context.NeedsInventoryList = false;
        if (original.Count == context.AllowedSlotCount) return false;

        validationList = new List<InventorySpot>(context.AllowedSlotCount);
        for (var i = 0; i < context.AllowedSlotCount; i++)
            validationList.Add(null!);

        if (context.RequestedSlotIndex >= original.Count &&
            context.RequestedSlotIndex < context.AllowedSlotCount) {
            SLRUpgradePack.Logger.LogDebug(
                $"Validating remote inventory slot {context.RequestedSlotIndex} against the requester's " +
                $"{context.AllowedSlotCount}-slot capacity (host list has {original.Count}).");
        }

        return true;
    }

    private static InventorySlotRequestValidationContext? CreateContext(int requestedSlotIndex,
        int requesterPhotonViewId) {
        if (!SemiFunc.IsMultiplayer()) return null;

        var player = SemiFunc.PlayerAvatarGetFromPhotonID(requesterPhotonViewId);
        var upgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        if (!player || !player.photonView || player.photonView.IsMine || upgrade == null ||
            !upgrade.UpgradeEnabled.Value || upgrade.UpgradeRegister == null)
            return null;

        var level = upgrade.UpgradeRegister.GetLevel(player);
        return new InventorySlotRequestValidationContext(requestedSlotIndex,
            upgrade.Calculate(3, player, level));
    }
}

[HarmonyPatch(typeof(ItemEquippable), "RPC_RequestEquip")]
internal static class InventorySlotEquipRequestValidationPatch {
    [HarmonyPrefix]
    private static void Prefix(int spotIndex, int physGrabberPhotonViewID,
        out InventorySlotRequestValidationContext? __state) {
        __state = InventorySlotRequestValidation.Begin(spotIndex, physGrabberPhotonViewID);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, InventorySlotRequestValidationContext? __state) {
        InventorySlotRequestValidation.Restore(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.GetAllSpots))]
internal static class InventorySlotValidationListPatch {
    [HarmonyPostfix]
    private static void Postfix(ref List<InventorySpot> __result) {
        if (InventorySlotRequestValidation.TryOverrideInventoryList(__result, out var validationList))
            __result = validationList;
    }
}

[HarmonyPatch(typeof(InventorySpot), "Update")]
public class InventorySpotUpdatePatch {
    private static readonly MethodInfo HandleInputMethod = Method(typeof(InventorySpot), "HandleInput");

    private static void Postfix(InventorySpot __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        var upgradeRegister = inventorySlotUpgrade.UpgradeRegister;
        var boundPlayer = inventorySlotUpgrade.BoundPlayer;
        if (!inventorySlotUpgrade.UpgradeEnabled.Value || boundPlayer == null || upgradeRegister == null) return;

        var index = __instance.inventorySpotIndex;
        if (index < 3 || index >= inventorySlotUpgrade.Calculate(3, SemiFunc.PlayerAvatarLocal(),
                upgradeRegister.GetLevel(boundPlayer))) return;

        var slotBinding = inventorySlotUpgrade.GetSlotBinding(index);
        if (slotBinding == null) return;

        var shortcut = slotBinding.Value;
        // BepInEx reads the configured shortcut directly; routing through the game's InputManager
        // would apply its action filters and could suppress the extra-slot input when a vanilla
        // action is held at the same time. KeyboardShortcut.IsDown() also rejects any unrelated
        // key being held, which prevents these slots from working while movement keys are down.
        RefreshSlotBindingLabel(__instance, InventorySlotInput.GetDisplay(shortcut));
        if (InventorySlotInput.IsDown(shortcut))
            HandleInputMethod.Invoke(__instance, null);
    }

    internal static void RefreshSlotBindingLabel(InventorySpot spot) {
        var upgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        var binding = upgrade.GetSlotBinding(spot.inventorySpotIndex);
        if (binding != null)
            RefreshSlotBindingLabel(spot, InventorySlotInput.GetDisplay(binding.Value));
    }

    internal static void RefreshSlotBindingLabel(InventorySpot spot, string display) =>
        InventoryUIStartPatch.RefreshSlotBindingLabel(spot, display);
}

internal static class InventorySlotInput {
    private static readonly KeyCode[] modifierKeys = {
        KeyCode.LeftShift, KeyCode.RightShift,
        KeyCode.LeftControl, KeyCode.RightControl,
        KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr,
        KeyCode.LeftCommand, KeyCode.RightCommand,
        KeyCode.LeftApple, KeyCode.RightApple,
        KeyCode.LeftWindows, KeyCode.RightWindows,
        KeyCode.LeftMeta, KeyCode.RightMeta
    };

    internal static bool IsDown(KeyboardShortcut shortcut) {
        var mainKey = shortcut.MainKey;
        if (mainKey == KeyCode.None) return false;

        var input = UnityInput.Current;
        if (!input.GetKeyDown(mainKey)) return false;

        var modifiers = shortcut.Modifiers.ToArray();
        if (modifiers.Any(modifier => !input.GetKey(modifier))) return false;

        // Preserve modifier-chord behavior, but don't treat held movement/action keys as
        // modifiers. BepInEx's built-in check rejects every held non-modifier key.
        return !modifierKeys.Any(modifier => modifier != mainKey && !modifiers.Contains(modifier) &&
                                             input.GetKey(modifier));
    }

    internal static string GetDisplay(KeyboardShortcut shortcut) {
        var keys = shortcut.Modifiers.Select(FormatKeyCode).ToList();
        keys.Add(FormatKeyCode(shortcut.MainKey));
        return string.Join(" + ", keys);
    }

    private static string FormatKeyCode(KeyCode key) => key switch {
        KeyCode.None => "Not set",
        KeyCode.Alpha0 => "0",
        KeyCode.Alpha1 => "1",
        KeyCode.Alpha2 => "2",
        KeyCode.Alpha3 => "3",
        KeyCode.Alpha4 => "4",
        KeyCode.Alpha5 => "5",
        KeyCode.Alpha6 => "6",
        KeyCode.Alpha7 => "7",
        KeyCode.Alpha8 => "8",
        KeyCode.Alpha9 => "9",
        KeyCode.Keypad0 => "Num 0",
        KeyCode.Keypad1 => "Num 1",
        KeyCode.Keypad2 => "Num 2",
        KeyCode.Keypad3 => "Num 3",
        KeyCode.Keypad4 => "Num 4",
        KeyCode.Keypad5 => "Num 5",
        KeyCode.Keypad6 => "Num 6",
        KeyCode.Keypad7 => "Num 7",
        KeyCode.Keypad8 => "Num 8",
        KeyCode.Keypad9 => "Num 9",
        KeyCode.Exclaim => "!",
        KeyCode.DoubleQuote => "\"",
        KeyCode.Hash => "#",
        KeyCode.Dollar => "$",
        KeyCode.Percent => "%",
        KeyCode.Ampersand => "&",
        KeyCode.Quote => "'",
        KeyCode.LeftParen => "(",
        KeyCode.RightParen => ")",
        KeyCode.Asterisk => "*",
        KeyCode.Plus => "+",
        KeyCode.Comma => ",",
        KeyCode.Minus => "-",
        KeyCode.Period => ".",
        KeyCode.Slash => "/",
        KeyCode.Colon => ":",
        KeyCode.Semicolon => ";",
        KeyCode.Less => "<",
        KeyCode.Equals => "=",
        KeyCode.Greater => ">",
        KeyCode.Question => "?",
        KeyCode.At => "@",
        KeyCode.LeftBracket => "[",
        KeyCode.Backslash => "\\",
        KeyCode.RightBracket => "]",
        KeyCode.Caret => "^",
        KeyCode.Underscore => "_",
        KeyCode.BackQuote => "`",
        KeyCode.LeftCurlyBracket => "{",
        KeyCode.Pipe => "|",
        KeyCode.RightCurlyBracket => "}",
        KeyCode.Tilde => "~",
        KeyCode.Return => "Enter",
        KeyCode.LeftControl or KeyCode.RightControl => "Ctrl",
        KeyCode.LeftShift or KeyCode.RightShift => "Shift",
        KeyCode.LeftAlt or KeyCode.RightAlt or KeyCode.AltGr => "Alt",
        KeyCode.LeftCommand or KeyCode.RightCommand or KeyCode.LeftApple or KeyCode.RightApple or
            KeyCode.LeftWindows or KeyCode.RightWindows or KeyCode.LeftMeta or KeyCode.RightMeta => "Cmd",
        _ => key.ToString()
    };
}

[HarmonyPatch(typeof(PunManager), "SetItemNameLOGIC")]
public class PunManagerSetItemNameLOGICPatch {
    private static bool Prefix(PunManager __instance, string _name, int photonViewID, ItemAttributes _itemAttributes,
        StatsManager ___statsManager) {
        if (photonViewID == -1 && SemiFunc.IsMultiplayer())
            return true;
        var itemAttributes = _itemAttributes;
        if (SemiFunc.IsMultiplayer()) {
            var photonView = PhotonView.Find(photonViewID);
            if (!photonView) return true;
            itemAttributes = photonView.GetComponent<ItemAttributes>();
        }

        if (!itemAttributes)
            return true;

        var instanceNameRef = FieldRefAccess<ItemAttributes, string>("instanceName");
        instanceNameRef.Invoke(itemAttributes) = _name;
        var component1 = itemAttributes.GetComponent<ItemBattery>();
        if (component1)
            component1.SetBatteryLife(___statsManager.GetBatteryLevel(_name));
        var itemEquippable = itemAttributes.GetComponent<ItemEquippable>();
        if (!itemEquippable)
            return true;
        var hashCode = _name.GetHashCode();

        foreach (var item in SemiFunc.PlayerGetList()) {
            if (InventorySlotUpgrade.serverMonitoredInventoryItems.TryGetValue(SemiFunc.PlayerGetSteamID(item),
                    out var value) && value.ContainsValue(hashCode)) {
                itemEquippable.RequestEquip(value.First(element => element.Value == hashCode).Key,
                    SemiFunc.IsMultiplayer() ? item.photonView.ViewID : -1);
                return false;
            }
        }

        return true;
    }
}
