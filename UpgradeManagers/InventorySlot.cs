using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KeybindLib.Classes;
using Photon.Pun;
using TMPro;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class InventorySlotUpgrade : UpgradeBase<int> {
    internal static readonly Dictionary<string, Dictionary<int, int>> serverMonitoredInventoryItems = new();
    public Keybind ItemSlot4 { get; set; }
    public Keybind ItemSlot5 { get; set; }
    public Keybind ItemSlot6 { get; set; }
    public Keybind ItemSlot7 { get; set; }
    public Keybind ItemSlot8 { get; set; }
    public Keybind ItemSlot9 { get; set; }
    internal string BoundPlayer { get; set; }
    public Inventory InventoryRef { get; set; }
    public InventoryUI UIRef { get; set; }

    public InventorySlotUpgrade(bool enabled, int upgradeAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier) :
        base("Inventory Slot", "assets/repo/mods/resources/items/items/item upgrade inventory slot lib.asset", enabled, upgradeAmount, false, 0, config, assetBundle, priceMultiplier, false, false, 6) {
        var RegisterMethod = Method(typeof(Keybinds), "Register");
        var plugin = typeof(SLRUpgradePack).GetCustomAttribute<BepInPlugin>();
        ItemSlot4 = Keybinds.Bind("Item slots", "Item Slot 4", "<keyboard>/z");
        ItemSlot5 = Keybinds.Bind("Item Slots", "Item Slot 5", "<keyboard>/x");
        ItemSlot6 = Keybinds.Bind("Item Slots", "Item Slot 6", "<keyboard>/c");
        ItemSlot7 = Keybinds.Bind("Item Slots", "Item Slot 7", "<keyboard>/n");
        ItemSlot8 = Keybinds.Bind("Item Slots", "Item Slot 8", "<keyboard>/m");
        ItemSlot9 = Keybinds.Bind("Item Slots", "Item Slot 9", "<keyboard>/,");
    }

    public override int Calculate(int value, PlayerAvatar player, int level) {
        return value + level;
    }

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        if (player == SemiFunc.PlayerAvatarLocal()) {
            BoundPlayer = SemiFunc.PlayerGetSteamID(player);
        }
    }

    internal override void UseUpgrade(PlayerAvatar player, int level) {
        base.UseUpgrade(player, level);
        InventoryStartPatch.Postfix(InventoryRef);
        InventoryUIStartPatch.Postfix(UIRef);
    }
}

[HarmonyPatch(typeof(InventoryUI), "Start")]
public class InventoryUIStartPatch {
    private static FieldRef<InventoryUI, List<GameObject>> allChildren = FieldRefAccess<InventoryUI, List<GameObject>>("allChildren");

    internal static void Postfix(InventoryUI __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        inventorySlotUpgrade.UIRef = __instance;
        if (inventorySlotUpgrade.UpgradeEnabled.Value && inventorySlotUpgrade.BoundPlayer != null) {
            var slots = inventorySlotUpgrade.Calculate(3, SemiFunc.PlayerAvatarLocal(), inventorySlotUpgrade.UpgradeRegister.GetLevel(inventorySlotUpgrade.BoundPlayer));

            if (slots > 3) {
                SLRUpgradePack.Logger.LogInfo($"Redrawing for {slots} slots");
                var num = -(slots * 40) / 2f + 20f;
                var child = __instance.transform.GetChild(0);
                for (var j = 0; j < slots; j++) {
                    if (__instance.transform.Find($"Inventory Spot {j + 1}") != null) {
                        __instance.transform.Find($"Inventory Spot {j + 1}").localPosition = new Vector2(num + j * 40f, -175.3f);
                        continue;
                    }
                    var val = Object.Instantiate(child, child.parent);
                    val.name = $"Inventory Spot {j + 1}";
                    var component = val.GetComponent<InventorySpot>();
                    component.inventorySpotIndex = j;
                    var component2 = val.Find("Numbers").GetComponent<TextMeshProUGUI>();
                    var text2 = (component.noItem.text = (j + 1).ToString());
                    component2.text = text2;
                    val.localPosition = new Vector2(num + j * 40f, -175.3f);
                    allChildren.Invoke(__instance).Add(val.gameObject);
                }
            }
        }
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
    }
}

[HarmonyPatch(typeof(Inventory), "Start")]
public class InventoryStartPatch {
    private static FieldRef<Inventory, List<InventorySpot>> inventorySpots = FieldRefAccess<Inventory, List<InventorySpot>>("inventorySpots");

    internal static void Postfix(Inventory __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        inventorySlotUpgrade.InventoryRef = __instance;
        if (inventorySlotUpgrade.UpgradeEnabled.Value && inventorySlotUpgrade.BoundPlayer != null) {
            var extraSlots = inventorySlotUpgrade.Calculate(3, SemiFunc.PlayerAvatarLocal(), inventorySlotUpgrade.UpgradeRegister.GetLevel(inventorySlotUpgrade.BoundPlayer));

            SLRUpgradePack.Logger.LogInfo($"Player has {extraSlots} slots");
            while (inventorySpots.Invoke(__instance).Count < extraSlots)
                inventorySpots.Invoke(__instance).Add(null);
        }
    }
}

[HarmonyPatch(typeof(InventorySpot), "Update")]
public class InventorySpotUpdatePatch {
    private static readonly MethodInfo HandleInputMethod = Method(typeof(InventorySpot), "HandleInput");

    private static void Prefix(InventorySpot __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        if (inventorySlotUpgrade.UpgradeEnabled.Value && inventorySlotUpgrade.BoundPlayer != null) {
            var slotCount = inventorySlotUpgrade.Calculate(3, SemiFunc.PlayerAvatarLocal(), inventorySlotUpgrade.UpgradeRegister.GetLevel(inventorySlotUpgrade.BoundPlayer));
            List<Keybind> slotInputs = [null, null, null, inventorySlotUpgrade.ItemSlot4, inventorySlotUpgrade.ItemSlot5, inventorySlotUpgrade.ItemSlot6, inventorySlotUpgrade.ItemSlot7, inventorySlotUpgrade.ItemSlot8, inventorySlotUpgrade.ItemSlot9];
            if (__instance.inventorySpotIndex > 2 && InputManager.instance.KeyDown(slotInputs.GetRange(0, slotCount)[__instance.inventorySpotIndex].inputKey)) {
                HandleInputMethod.Invoke(__instance, null);
            }
        }
    }
}

[HarmonyPatch(typeof(PunManager), "SetItemNameLOGIC")]
public class PunManagerSetItemNameLOGICPatch {
    private static bool Prefix(PunManager __instance, string name, int photonViewID, ItemAttributes _itemAttributes, StatsManager ___statsManager) {
        if (photonViewID == -1 && SemiFunc.IsMultiplayer())
            return true;
        var itemAttributes = _itemAttributes;
        if (SemiFunc.IsMultiplayer())
            itemAttributes = PhotonView.Find(photonViewID).GetComponent<ItemAttributes>();
        if ((Object) _itemAttributes == null && !SemiFunc.IsMultiplayer())
            return true;
        var instanceNameRef = FieldRefAccess<ItemAttributes, string>("instanceName");
        instanceNameRef.Invoke(itemAttributes) = name;
        var component1 = itemAttributes.GetComponent<ItemBattery>();
        if (component1)
            component1.SetBatteryLife(___statsManager.itemStatBattery[name]);
        var itemEquippable = itemAttributes.GetComponent<ItemEquippable>();
        if (!itemEquippable)
            return true;
        var hashCode = name.GetHashCode();

        if (!itemEquippable) {
            return true;
        }

        foreach (var item in SemiFunc.PlayerGetList()) {
            if (InventorySlotUpgrade.serverMonitoredInventoryItems.TryGetValue(SemiFunc.PlayerGetSteamID(item), out var value) && value.ContainsValue(hashCode)) {
                itemEquippable.RequestEquip(value.First(element => element.Value == hashCode).Key, SemiFunc.IsMultiplayer() ? item.photonView.ViewID : -1);
                return false;
            }
        }

        return true;
    }
}