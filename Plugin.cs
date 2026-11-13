using System;
using System.Globalization;
using System.Linq;

using BepInEx;
using BepInEx.Configuration;

using BetterHammer.Core;
using BetterHammer.Grid;
using BetterHammer.Materials;
using BetterHammer.Structures;

using HarmonyLib;

using Jotunn.Configs;
using Jotunn.Managers;
using Jotunn.Utils;

using UnityEngine;

namespace BetterHammer;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
public class BetterHammerPlugin : BaseUnityPlugin
{
    public const string PluginGUID = "javidahmed64592.betterhammer";
    public const string PluginName = "Better Hammer";
    public const string PluginVersion = "1.0.1";

    private const string HammerPrefabName = "Hammer";
    private const string ButtonName = "BetterHammer_Toggle";
    private const string ButtonQueryName = ButtonName + "!" + PluginGUID;

    private static readonly float[] GridSizes = [0.25f, 0.5f, 1f, 2f, 4f];

    internal static ItemDrop.ItemData _phantomHammer;

    private static ConfigEntry<bool> _gridEnabled;
    internal static bool GridEnabled => _gridEnabled.Value;
    internal static ConfigEntry<float> _gridSize;
    private static readonly GridOverlay Overlay = new();

    /// <summary>
    /// Latest grid-snapped aim point from Player.PieceRayTest, used to place the grid overlay.
    /// </summary>
    internal static int _aimFrame = -10;
    internal static Vector3 _aimPoint;
    internal static GridPlane _aimPlane;
    internal static float _aimSign;

    private ConfigEntry<KeyCode> _keybind;
    private Harmony _harmony;
    private bool _knownRecipesRefreshed;
    private static Piece _lastBuildPiece;

    private void Awake()
    {
        _keybind = Config.Bind(
            "Keybinds",
            "Open Build Menu",
            KeyCode.B,
            "Equips a virtual hammer and opens the build menu, without needing a real " +
            "hammer in your inventory. Rebindable from the in-game controls menu.");

        InputManager.Instance.AddButton(PluginGUID, new ButtonConfig
        {
            Name = ButtonName,
            Config = _keybind
        });

        _gridEnabled = Config.Bind(
            "Grid",
            "Enabled",
            true,
            "Show a world grid while building and snap pieces to its vertices.");

        _gridSize = Config.Bind(
            "Grid",
            "Snap Distance",
            GridSizes[1],
            new ConfigDescription(
                "Distance in metres between grid vertices (0.25, 0.5, 1, 2 or 4). While building, " +
                "hold Ctrl and scroll to double or halve it.",
                new AcceptableValueRange<float>(GridSizes[0], GridSizes[GridSizes.Length - 1])));

        GridOverlay.BindConfig(Config);
        ChestMaterials.BindConfig(Config);
        CopyLibrary.BindConfig(Config);
        MultiCopy.BindConfig(Config);
        ConnectedPieces.BindConfig(Config);
        Translations.Load();

        // Reject hand-edited values that aren't one of the doubling/halving steps.
        if (Array.IndexOf(GridSizes, _gridSize.Value) < 0)
        {
            _gridSize.Value = GridSizes[1];
        }

        _harmony = new Harmony(PluginGUID);
        _harmony.PatchAll();
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        Overlay.Destroy();
    }

    private void Update()
    {
        // Keep phantom hammer at full durability so it never breaks or needs repair.
        if (_phantomHammer != null)
        {
            _phantomHammer.m_durability = _phantomHammer.m_shared.m_maxDurability;
        }

        Player player = Player.m_localPlayer;
        if (player == null)
        {
            _phantomHammer = null;
            _knownRecipesRefreshed = false;
            _lastBuildPiece = null;
            PieceMover.Reset();
            MultiCopy.Reset();
            Overlay.Hide();
            return;
        }

        // Build the phantom as soon as ObjectDB is ready.
        EnsurePhantomHammer();
        CopyLibrary.EnsureLoaded(player, _phantomHammer?.m_shared.m_buildPieces);

        // Force a recipe scan once per session with the phantom's piece table.
        if (!_knownRecipesRefreshed && _phantomHammer != null)
        {
            _knownRecipesRefreshed = true;
            player.UpdateKnownRecipesList();
        }

        // Same guards vanilla input uses - don't fire while a menu/inventory/chat is open.
        if (Menu.IsVisible() || InventoryGui.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()))
        {
            Overlay.Hide();
            return;
        }

        CopyLibrary.UpdateMenuInput(player);
        MultiCopy.UpdateSelectionKeys(player);

        if (ZInput.GetButtonDown(ButtonQueryName))
        {
            ToggleBuildMenu(player);
        }

        // Alt+right-click is for the selection. While moving a piece, a plain right-click cancels the move,
        // but Shift+ and Ctrl+right-click still toggle repair mode (pausing the move) and the grid.
        if (player.InPlaceMode() && Input.GetMouseButtonDown(1) && !AltHeld() && !MultiCopy.RightClickConsumed)
        {
            if (ShiftHeld())
            {
                ToggleRepairMode(player);
            }
            else if (CtrlHeld())
            {
                ToggleGrid(player);
            }
        }

        UpdateGrid(player);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Unity message")]
    private void LateUpdate()
    {
        // The gold of a piece being moved goes on top of the teal of a selected one.
        MultiCopy.UpdateHighlight();
        PieceMover.UpdateHighlight();
    }

    private static void ToggleGrid(Player player)
    {
        _gridEnabled.Value = !_gridEnabled.Value;
        player.Message(MessageHud.MessageType.TopLeft, _gridEnabled.Value ? "$betterhammer_grid_on" : "$betterhammer_grid_off");
    }

    private static void UpdateGrid(Player player)
    {
        // The aim point is refreshed every frame by the PieceRayTest patch while placing.
        if (!GridActive(player) || Time.frameCount - _aimFrame > 2 || _aimPlane == GridPlane.None)
        {
            Overlay.Hide();
            return;
        }

        HandleGridScroll(player);
        Overlay.Show(_aimPoint, _gridSize.Value, _aimPlane, _aimSign);
    }

    internal static bool GridActive(Player player)
    {
        return _gridEnabled.Value
            && player.InPlaceMode()
            && !player.InRepairMode()
            && player.m_placementGhost != null
            && IsHammerEquipped(player);
    }

    /// <summary>
    /// The grid is for hammers only (not the hoe, cultivator, etc.). Besides the phantom
    /// hammer, any build tool with a piece table whose item, prefab or table name mentions
    /// "hammer" counts, which covers vanilla and modded hammers.
    /// </summary>
    internal static bool IsHammerEquipped(Player player)
    {
        ItemDrop.ItemData item = player.GetRightItem();
        if (item == null)
        {
            return false;
        }

        if (item == _phantomHammer)
        {
            return true;
        }

        PieceTable table = item.m_shared?.m_buildPieces;
        return table != null && (ContainsHammer(item.m_shared.m_name)
            || ContainsHammer(item.m_dropPrefab != null ? item.m_dropPrefab.name : null)
            || ContainsHammer(table.name));
    }

    private static bool ContainsHammer(string name)
    {
        return name != null && name.IndexOf("hammer", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool ShiftHeld()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    internal static bool CtrlHeld()
    {
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    }

    internal static bool AltHeld()
    {
        return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    }

    /// <summary>
    /// Alt+scroll zooms the camera while placing with a hammer.
    /// </summary>
    internal static bool AltZoomActive(Player player)
    {
        return player == Player.m_localPlayer && AltHeld() && player.InPlaceMode() && IsHammerEquipped(player);
    }

    /// <summary>
    /// Ctrl+scroll: double / halve the grid distance.
    /// </summary>
    private static void HandleGridScroll(Player player)
    {
        float scroll = Input.mouseScrollDelta.y;
        if (scroll == 0f || !CtrlHeld())
        {
            return;
        }

        float size = scroll > 0f ? _gridSize.Value * 2f : _gridSize.Value / 2f;
        size = Mathf.Clamp(size, GridSizes[0], GridSizes[GridSizes.Length - 1]);
        if (Mathf.Approximately(size, _gridSize.Value))
        {
            return;
        }

        _gridSize.Value = size;
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$betterhammer_grid_distance", size.ToString("0.###", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The piece Shift+right-click goes back to when leaving repair mode.
    /// </summary>
    internal static void SetRepairReturnPiece(Piece piece)
    {
        _lastBuildPiece = piece;
    }

    private static void ToggleRepairMode(Player player)
    {
        if (player.InRepairMode())
        {
            if (_lastBuildPiece != null)
            {
                _ = player.SetSelectedPiece(_lastBuildPiece);
            }
        }
        else
        {
            _lastBuildPiece = player.GetSelectedPiece();
            Piece repairPiece = player.GetBuildPieces()?.FirstOrDefault(p => p.m_repairPiece);
            if (repairPiece != null)
            {
                _ = player.SetSelectedPiece(repairPiece);
            }
        }
    }

    private static void ToggleBuildMenu(Player player)
    {
        EnsurePhantomHammer();
        if (_phantomHammer == null)
        {
            return; // ObjectDB isn't loaded yet (e.g. still on the main menu)
        }

        // Checking the player's actual right-hand item (rather than tracking our own
        // bool) keeps this correct across death/respawn or if something else unequips us.
        bool isEquipped = player.GetRightItem() == _phantomHammer;

        if (!isEquipped)
        {
            // Same call path as picking the hammer from a hotbar slot: shows it
            // in-hand and enables the build piece grid.
            _ = player.EquipItem(_phantomHammer);
        }
        else
        {
            player.UnequipItem(_phantomHammer);
        }
    }

    private static void EnsurePhantomHammer()
    {
        if (_phantomHammer != null || ObjectDB.instance == null)
        {
            return;
        }

        GameObject hammerPrefab = ObjectDB.instance.GetItemPrefab(HammerPrefabName);
        if (hammerPrefab == null)
        {
            return;
        }

        ItemDrop itemDrop = hammerPrefab.GetComponent<ItemDrop>();

        // Build a fresh ItemData rather than Clone()-ing the prefab's data: raw prefab
        // item data has uninitialised fields (e.g. m_customData = null) that
        // SetupVisEquipment dereferences in Valheim l-1.0.x, causing a crash.
        _phantomHammer = new ItemDrop.ItemData
        {
            m_shared = itemDrop.m_itemData.m_shared,
            m_dropPrefab = hammerPrefab,
            m_stack = 1,
            m_quality = 1,
            m_variant = 0,
            m_durability = itemDrop.m_itemData.m_shared.m_maxDurability,
            m_customData = []
        };
    }
}
