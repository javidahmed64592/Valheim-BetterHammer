using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using BepInEx.Configuration;

using TMPro;

using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace BetterHammer.Structures;

/// <summary>
/// <para>
/// Copying several pieces at once. Alt+middle-click adds the piece you're looking at to the selection,
/// Alt+right-click takes it out again, and Alt+left-click saves the selection as a new entry in the
/// hammer's Copies tab (see <see cref="CopyLibrary"/>) and selects it. Building works as normal while
/// you're selecting, and the selection survives putting the hammer away.
/// </para>
/// <para>
/// While a saved copy is the selected piece, the game's own ghost is used for one of its pieces, the
/// reference piece, so its placement checks, snapping and the build grid all apply to it as normal. The
/// other pieces get their own ghosts that follow it, keeping their places relative to it. Q/E steps
/// through the snap points of every piece in turn, and Shift+Q/E jumps from piece to piece.
/// </para>
/// </summary>
internal static class MultiCopy
{
    private const string RemoveButton = "Remove";
    private const string PlaceButton = "Attack";

    /// <summary>
    /// How long each page of materials shows when there are more than the build panel has room for.
    /// </summary>
    private const float MaterialPageSeconds = 2.5f;

    /// <summary>
    /// Teal, so it stands apart from the game's pale blue hover glow and the gold of a piece being moved.
    /// </summary>
    private static readonly Color HighlightColor = new(0.25f, 0.9f, 0.75f);
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    /// <summary>
    /// Built pieces picked with Alt+middle-click, in the order they were picked.
    /// </summary>
    private static readonly List<Piece> Selected = [];

    /// <summary>
    /// The saved copy being placed; its ghosts are a <see cref="GhostGroup"/>.
    /// </summary>
    private static CopyLibrary.Copy _active;

    /// <summary>
    /// The copy being placed, its reference piece and its manual snap point just before the game set up
    /// its ghost again, so a rebuild of the same copy (after placing it uses up materials, or taking the
    /// hammer out again) keeps them.
    /// </summary>
    private static (CopyLibrary.Copy Copy, int Reference, int SnapPoint) _kept;

    private static int _rightClickFrame = -10;
    private static bool _lit;

    private static Piece _summary;

    /// <summary>
    /// The stations the last summary needs beyond the one the game shows itself.
    /// </summary>
    private static readonly List<CraftingStation> ExtraStations = [];
    private static bool _selectionDirty = true;
    private static List<Piece.Requirement> _selectionRequirements = [];
    private static List<Piece> _selectionStations = [];
    private static string _selectionDescription = "";

    private static ConfigEntry<KeyCode> _clearKey;

    internal static KeyCode ClearKey => _clearKey != null ? _clearKey.Value : KeyCode.Backspace;

    internal static bool Placing => _active != null && GhostGroup.ActiveOwner == GhostGroup.Owner.Copy;

    internal static bool HasSelection => Selected.Count > 0;

    /// <summary>
    /// Alt+right-click deselects, so it mustn't also open the build menu or toggle repair mode or the grid.
    /// </summary>
    internal static bool RightClickConsumed => _rightClickFrame == Time.frameCount;


    /// <summary>
    /// Runs before Player.UpdatePlacement for the local player. Returns whether vanilla placing must be
    /// blocked this frame.
    /// </summary>
    internal static bool BeforeUpdatePlacement(Player player, bool takeInput)
    {
        Prune();

        // The hammer was put away, or another piece selected without the ghost being set up again.
        if (Placing && (!player.InPlaceMode() || player.m_placementGhost == null || CopyLibrary.Find(player.GetSelectedPiece()) != _active))
        {
            Keep(player);
            Deactivate();
        }

        bool wasPlacing = Placing;
        if (!takeInput
            || !player.InPlaceMode()
            || player.IsDead()
            || Hud.IsPieceSelectionVisible()
            || Hud.InRadial()
            || ZInput.IsGamepadActive()
            || !BetterHammerPlugin.IsHammerEquipped(player))
        {
            return wasPlacing;
        }

        bool alt = BetterHammerPlugin.AltHeld();
        bool placePressed = ZInput.GetButtonDown(PlaceButton);
        bool block = wasPlacing;
        bool shift = BetterHammerPlugin.ShiftHeld();
        if (alt && ZInput.GetButtonDown(RemoveButton))
        {
            PieceMover.BlockRemoveUntilReleased(player);
            if (shift)
            {
                SelectConnected(player);
            }
            else
            {
                Select(player);
            }
        }
        else if (alt && Input.GetMouseButtonDown(1))
        {
            _rightClickFrame = Time.frameCount;
            if (shift)
            {
                DeselectConnected(player);
            }
            else
            {
                Deselect(player);
            }
        }
        else if (alt && placePressed)
        {
            // Stops vanilla placing the selected piece this frame.
            player.m_lastToolUseTime = Time.time;
            block = true;
            Confirm(player);
        }
        else if (Placing && placePressed && Time.time - player.m_lastToolUseTime > player.m_placeDelay)
        {
            Place(player);
        }

        return block || Placing;
    }

    public static void BindConfig(ConfigFile config)
    {
        _clearKey = config.Bind(
            "Keybinds",
            "Clear Selection",
            KeyCode.Backspace,
            "Deselects every piece selected for copying, without changing anything else.");
    }

    /// <summary>
    /// A selected piece that was moved is a new object (see <see cref="PieceMover"/>); it stays
    /// selected, so a copy saved afterwards uses where it is now.
    /// </summary>
    internal static void ReplaceSelected(Piece original, Piece replacement)
    {
        int index = Selected.IndexOf(original);
        if (index < 0 || replacement == null)
        {
            return;
        }

        ResetHighlight(original);
        Selected[index] = replacement;
        _selectionDirty = true;
    }

    /// <summary>
    /// With pieces selected and the build menu closed: the clear key deselects them all, and the delete
    /// key asks, then removes them all.
    /// </summary>
    internal static void UpdateSelectionKeys(Player player)
    {
        if (Selected.Count == 0
            || Hud.IsPieceSelectionVisible()
            || UnifiedPopup.IsVisible()
            || !player.InPlaceMode()
            || !BetterHammerPlugin.IsHammerEquipped(player)
            || CopyLibrary.TypingInTextField())
        {
            return;
        }

        if (Input.GetKeyDown(ClearKey))
        {
            int count = Selected.Count;
            ClearHighlights();
            Selected.Clear();
            _selectionDirty = true;
            player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$betterhammer_selection_cleared", Count(count)));
        }
        else if (Input.GetKeyDown(CopyLibrary.DeleteKey))
        {
            Prune();
            UnifiedPopup.Push(new YesNoPopup(
                "$betterhammer_selection_delete_title",
                Localization.instance.Localize("$betterhammer_selection_delete_text", Count(Selected.Count)),
                () =>
                {
                    UnifiedPopup.Pop();
                    RemoveSelected(player);
                },
                UnifiedPopup.Pop));
        }
    }

    /// <summary>
    /// Removes every selected piece the way the hammer does, materials dropping where it stood. Pieces
    /// the hammer couldn't remove either (ward, no-build zone, missing workbench, a private chest with
    /// items in it) are left alone and stay selected.
    /// </summary>
    private static void RemoveSelected(Player player)
    {
        Prune();
        int total = Selected.Count;
        int removed = 0;
        foreach (Piece piece in Selected.ToList())
        {
            if (!CanRemove(player, piece))
            {
                continue;
            }

            ResetHighlight(piece);
            Remove(player, piece);
            _ = Selected.Remove(piece);
            removed++;
        }

        _selectionDirty = true;
        player.Message(MessageHud.MessageType.TopLeft, removed == total
            ? Localization.instance.Localize("$betterhammer_selection_removed", Count(removed))
            : Localization.instance.Localize("$betterhammer_selection_removed_some", Count(removed), Count(total)));
    }

    private static bool CanRemove(Player player, Piece piece)
    {
        ZNetView nview = piece.GetComponent<ZNetView>();
        Vector3 position = piece.transform.position;
        bool stationInRange = player.m_noPlacementCost
            || piece.m_craftingStation == null
            || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench)
            || CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position) != null;
        return nview != null
            && nview.IsValid()
            && piece.m_canBeRemoved
            && stationInRange
            && !Location.IsInsideNoBuildLocation(position)
            && PrivateArea.CheckAccess(position, 0f, false)
            && piece.CanBeRemoved();
    }

    /// <summary>
    /// The same as the hammer's remove, without the swing.
    /// </summary>
    private static void Remove(Player player, Piece piece)
    {
        piece.GetComponent<IRemoved>()?.OnRemoved();
        WearNTear wearNTear = piece.GetComponent<WearNTear>();
        if (wearNTear != null)
        {
            wearNTear.Remove();
            return;
        }

        Character character = piece.GetComponent<Character>();
        if (character != null)
        {
            character.Damage(new HitData(1E+10f));
            return;
        }

        piece.GetComponent<ZNetView>().ClaimOwnership();
        piece.DropResources();
        _ = piece.m_placeEffect.Create(piece.transform.position, piece.transform.rotation, piece.transform, 1f, -1, player.GetZDOID());
        _ = player.m_removeEffects.Create(piece.transform.position, Quaternion.identity, null, 1f, -1, player.GetZDOID());
        ZNetScene.instance.Destroy(piece.gameObject);
    }

    internal static void Reset()
    {
        ClearHighlights();
        Selected.Clear();
        Deactivate();
        _kept = default;
        _selectionDirty = true;
    }

    private static void Select(Player player)
    {
        Piece piece = player.GetHoveringPiece();
        if (piece == null)
        {
            return;
        }

        string name = Localization.instance.Localize(piece.m_name);
        if (Selected.Contains(piece))
        {
            player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$betterhammer_copy_already", name));
            return;
        }

        if (!CanSelect(player, piece))
        {
            // Same as copying a single piece: this hammer can't build it, or you don't know it yet.
            player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
            return;
        }

        Selected.Add(piece);
        _selectionDirty = true;
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$betterhammer_copy_selected", name, Count(Selected.Count)));
    }

    /// <summary>
    /// Whether the piece can be selected: it exists, and this hammer can build it.
    /// </summary>
    private static bool CanSelect(Player player, Piece piece)
    {
        ZNetView nview = piece.GetComponent<ZNetView>();
        return nview != null && nview.IsValid() && player.m_buildPieces.GetPieceIndex(piece, out _, out _);
    }

    /// <summary>
    /// Alt+Shift+middle-click: selects the piece looked at and every piece connected to it. Pieces this
    /// hammer can't build are left out, but still connect the pieces either side of them.
    /// </summary>
    private static void SelectConnected(Player player)
    {
        Piece start = player.GetHoveringPiece();
        if (start == null)
        {
            return;
        }

        Prune();
        int added = 0;
        int skipped = 0;
        bool limited;
        foreach (Piece piece in ConnectedPieces.Find(start, out limited))
        {
            if (Selected.Contains(piece))
            {
                continue;
            }

            if (!CanSelect(player, piece))
            {
                skipped++;
                continue;
            }

            Selected.Add(piece);
            added++;
        }

        _selectionDirty = true;
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$betterhammer_copy_selected_connected", Count(added), Count(Selected.Count)));
        ReportConnected(player, skipped, limited);
    }

    /// <summary>
    /// Alt+Shift+right-click: deselects the piece looked at and every selected piece connected to it,
    /// whether or not the pieces in between are selected.
    /// </summary>
    private static void DeselectConnected(Player player)
    {
        Piece start = player.GetHoveringPiece();
        if (start == null)
        {
            return;
        }

        Prune();
        int removed = 0;
        bool limited;
        foreach (Piece piece in ConnectedPieces.Find(start, out limited))
        {
            if (Selected.Remove(piece))
            {
                ResetHighlight(piece);
                removed++;
            }
        }

        _selectionDirty = true;
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$betterhammer_copy_deselected_connected", Count(removed), Count(Selected.Count)));
        ReportConnected(player, 0, limited);
    }

    private static void ReportConnected(Player player, int skipped, bool limited)
    {
        if (skipped > 0)
        {
            player.Message(MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$betterhammer_copy_connected_skipped", Count(skipped)));
        }

        if (limited)
        {
            player.Message(MessageHud.MessageType.Center,
                Localization.instance.Localize("$betterhammer_copy_connected_limit", Count(ConnectedPieces.MaxPieces)));
        }
    }

    private static void Deselect(Player player)
    {
        Piece piece = player.GetHoveringPiece();
        int index = piece != null ? Selected.IndexOf(piece) : -1;
        if (index < 0)
        {
            return;
        }

        ResetHighlight(piece);
        Selected.RemoveAt(index);
        _selectionDirty = true;
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$betterhammer_copy_deselected", Localization.instance.Localize(piece.m_name), Count(Selected.Count)));
    }

    /// <summary>
    /// Alt+left-click: asks for a name, then saves the selection as a new copy in the Copies tab and
    /// selects it.
    /// </summary>
    private static void Confirm(Player player)
    {
        Prune();
        if (Selected.Count == 0)
        {
            player.Message(MessageHud.MessageType.Center, "$betterhammer_copy_nothing");
            return;
        }

        List<(Piece Prefab, Vector3 Position, Quaternion Rotation)> pieces = [];
        foreach (Piece piece in Selected)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(Utils.GetPrefabName(piece.gameObject));
            Piece prefabPiece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (prefabPiece == null || !player.m_buildPieces.GetPieceIndex(prefabPiece, out _, out _))
            {
                // The hammer was swapped for one with different pieces since this one was selected.
                player.Message(MessageHud.MessageType.Center,
                    Localization.instance.Localize("$betterhammer_copy_unavailable", Localization.instance.Localize(piece.m_name)));
                return;
            }

            pieces.Add((prefabPiece, piece.transform.position, piece.transform.rotation));
        }

        // The pieces are noted now; the copy is saved once a name is entered (cancelling saves nothing).
        CopyLibrary.AskForName("$betterhammer_copy_name_title", CopyLibrary.NextDefaultName(), name => Save(player, pieces, name));
    }

    private static void Save(Player player, List<(Piece Prefab, Vector3 Position, Quaternion Rotation)> pieces, string name)
    {
        PieceMover.Abort(player);
        CopyLibrary.Copy copy = CopyLibrary.Add(player, pieces, name);
        if (copy == null)
        {
            player.Message(MessageHud.MessageType.Center, "$betterhammer_copy_failed");
            return;
        }

        ClearHighlights();
        Selected.Clear();
        _selectionDirty = true;

        // Selecting it sets up its ghosts (see AfterSetupPlacementGhost), facing the way the originals do.
        if (player.SetSelectedPiece(copy.Piece))
        {
            player.m_placeRotation = GhostGroup.RotationSteps(player, copy.Parts[0].Rotation);
        }

        player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$betterhammer_copy_saved", copy.Name));
    }

    /// <summary>
    /// Runs before Player.SetupPlacementGhost, which resets the manual snap point when the ghost changes:
    /// remembers where the copy being placed was snapping from.
    /// </summary>
    internal static void BeforeSetupPlacementGhost(Player player)
    {
        if (!GhostGroup.CreatingGhosts)
        {
            Keep(player);
        }
    }

    /// <summary>
    /// Remembers the copy being placed, its reference piece and its snap point, if one is being placed.
    /// What's remembered stays while the hammer is away, as the game keeps a single piece's snap point.
    /// </summary>
    private static void Keep(Player player)
    {
        if (Placing)
        {
            _kept = (_active, GhostGroup.Reference, player.m_manualSnapPoint);
        }
    }

    /// <summary>
    /// Runs after Player.SetupPlacementGhost, which the game calls whenever the selected piece changes, the
    /// hammer is equipped or the inventory changes. If the selected piece is a saved copy, swaps the game's
    /// stand-in ghost for ghosts of the copy's pieces. If it's the copy that was already being placed, the
    /// reference piece and snap point stay as they were.
    /// </summary>
    internal static void AfterSetupPlacementGhost(Player player)
    {
        if (GhostGroup.CreatingGhosts)
        {
            return;
        }

        // The game just replaced its ghost, so a group's reference ghost is gone. A move notices and ends.
        GhostGroup.End();
        _active = null;
        if (!player.InPlaceMode() || player.IsDead())
        {
            // The hammer was put away: keep what was remembered for when it's taken out again.
            return;
        }

        (CopyLibrary.Copy keptCopy, int reference, int snapPoint) = _kept;
        _kept = default;
        CopyLibrary.Copy copy = CopyLibrary.Find(player.GetSelectedPiece());
        if (copy != null)
        {
            if (copy == keptCopy)
            {
                GhostGroup.Begin(player, copy.Parts, GhostGroup.Owner.Copy, reference, snapPoint);
            }
            else
            {
                GhostGroup.Begin(player, copy.Parts, GhostGroup.Owner.Copy);
            }

            _active = copy;
        }
    }

    /// <summary>
    /// Ends placing a saved copy without touching a move's group.
    /// </summary>
    private static void Deactivate()
    {
        if (GhostGroup.ActiveOwner == GhostGroup.Owner.Copy)
        {
            GhostGroup.End();
        }

        _active = null;
    }

    /// <summary>
    /// What Ctrl+middle-click moves. Looking at a piece that isn't selected: just that piece. Otherwise
    /// the whole selection, in the order it was picked, snapping by the piece looked at, or by the first
    /// one picked when looking at nothing. Null if there's nothing to move.
    /// </summary>
    internal static List<Piece> PiecesToMove(Piece hovered, out int reference)
    {
        Prune();
        reference = 0;
        if (hovered != null && !Selected.Contains(hovered))
        {
            return [hovered];
        }

        if (Selected.Count == 0)
        {
            return null;
        }

        if (hovered != null)
        {
            reference = Selected.IndexOf(hovered);
        }

        return [.. Selected];
    }

    /// <summary>
    /// Whether Ctrl+middle-click would move several pieces (see <see cref="PiecesToMove"/>).
    /// </summary>
    internal static bool MovesSelection(Piece hovered)
    {
        return Selected.Count >= 2 && (hovered == null || Selected.Contains(hovered));
    }

    internal static bool IsSelected(Piece piece)
    {
        return piece != null && Selected.Contains(piece);
    }

    /// <summary>
    /// Left-click while placing a copy: builds every piece, paying for all of them at once.
    /// </summary>
    private static void Place(Player player)
    {
        // Whatever happens, this click is used up; the delay also stops vanilla placing this frame.
        player.m_lastToolUseTime = Time.time;

        ItemDrop.ItemData tool = player.GetRightItem();
        if (!player.HaveStamina(tool.m_shared.m_attack.m_attackStamina))
        {
            Hud.instance.StaminaBarEmptyFlash();
            return;
        }

        player.UpdatePlacementGhost(flashGuardStone: true);
        GameObject reference = player.m_placementGhost;
        if (player.m_placementStatus != Player.PlacementStatus.Valid || reference == null)
        {
            player.Message(MessageHud.MessageType.Center,
                PieceMover.StatusMessages.TryGetValue(player.m_placementStatus, out string message) ? message : "$msg_invalidplacement");
            return;
        }

        if (!HaveStations(player, _active.Prefabs))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_missingstation");
            return;
        }

        // The copy's menu entry carries the combined materials, so the game's own check (which the chest
        // materials feature extends) covers them all.
        if (!player.m_noPlacementCost && !player.HaveRequirements(_active.Piece, Player.RequirementMode.CanBuild))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
            return;
        }

        // Placing a piece that starts at a random turn re-rolls the build rotation; keep the copy's.
        int rotation = player.m_placeRotation;
        List<(Vector3 Position, Quaternion Rotation)> poses = GhostGroup.Poses(player);
        for (int i = 0; i < poses.Count; i++)
        {
            player.PlacePiece(_active.Parts[i].Prefab, poses[i].Position, poses[i].Rotation, doAttack: i == 0);
        }

        player.m_placeRotation = rotation;

        if (!player.m_noPlacementCost && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost))
        {
            player.ConsumeResources(_active.Piece.m_resources, 0);
        }

        player.UseStamina(player.GetBuildStamina());
        if (tool.m_shared.m_useDurability)
        {
            tool.m_durability -= player.GetPlaceDurability(tool) * Game.m_durabilityRate;
        }

        if (player.m_buildPieces.m_skill != Skills.SkillType.None)
        {
            player.RaiseSkill(player.m_buildPieces.m_skill);
        }

        _ = tool.m_shared.m_buildEffect.Create(player.transform.position, Quaternion.identity, null, 1f, -1, player.GetZDOID());
        player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$betterhammer_copy_placed", Count(poses.Count)));
    }

    private static bool HaveStations(Player player, List<Piece> pieces)
    {
        if (player.m_noPlacementCost || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench))
        {
            return true;
        }

        foreach (Piece piece in pieces)
        {
            if (piece.m_craftingStation != null
                && CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position) == null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// What the build panel should describe instead of the given piece: the selection while Alt is held,
    /// or a saved copy (selected, or hovered in the build menu). The materials are shown a page at a time
    /// when there are more than the panel has room for.
    /// </summary>
    internal static bool TryGetSummary(Piece piece, out Piece summary)
    {
        summary = null;
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            return false;
        }

        bool menu = Hud.IsPieceSelectionVisible();
        Prune();
        if (!menu && Selected.Count > 0 && BetterHammerPlugin.AltHeld() && BetterHammerPlugin.IsHammerEquipped(player))
        {
            if (_selectionDirty)
            {
                _selectionDirty = false;
                _selectionRequirements = CopyLibrary.CombineRequirements(Selected, out _selectionStations);
                _selectionDescription = CopyLibrary.DescribePieces(Selected);
            }

            summary = Fill(player,
                Localization.instance.Localize("$betterhammer_copy_summary_selected", Count(Selected.Count)),
                _selectionDescription,
                Selected[0].m_icon,
                _selectionRequirements,
                _selectionStations);
            return true;
        }

        CopyLibrary.Copy copy = CopyLibrary.Find(piece);
        if (copy == null)
        {
            return false;
        }

        string description = menu ? copy.Description + "\n" + CopyLibrary.MenuHint() : copy.Description;
        summary = Fill(player, copy.Name, description, copy.Piece.m_icon, copy.Piece.m_resources, copy.StationPieces);
        return true;
    }

    private static Piece Fill(Player player, string name, string description, Sprite icon, IList<Piece.Requirement> requirements, List<Piece> stationPieces)
    {
        if (_summary == null)
        {
            GameObject holder = new("BetterHammer_CopySummary");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            _summary = holder.AddComponent<Piece>();
        }

        // A requirement slot is kept for each station (at least one material slot is left), out-of-range
        // stations first so they're the ones shown if they don't all fit.
        List<CraftingStation> stations = StationsToShow(player, stationPieces);
        int totalSlots = Hud.instance != null ? Hud.instance.m_requirementItems.Length : requirements.Count + stations.Count;
        int stationSlots = Mathf.Min(stations.Count, Mathf.Max(0, totalSlots - 1));
        int slots = Mathf.Max(1, totalSlots - Mathf.Max(1, stationSlots));
        int pages = Mathf.Max(1, Mathf.CeilToInt(requirements.Count / (float)slots));
        int page = (int)(Time.time / MaterialPageSeconds) % pages;
        if (pages > 1)
        {
            description += "\n" + Localization.instance.Localize("$betterhammer_copy_materials_page", Count(page + 1), Count(pages));
        }

        _summary.m_name = name;
        _summary.m_description = description;
        _summary.m_icon = icon;
        _summary.m_resources = [.. requirements.Skip(page * slots).Take(slots)];
        _summary.m_craftingStation = stationSlots > 0 ? stations[0] : null;
        ExtraStations.Clear();
        ExtraStations.AddRange(stations.Skip(1).Take(stationSlots - 1));
        return _summary;
    }

    /// <summary>
    /// The stations the pieces need, those out of range first.
    /// </summary>
    private static List<CraftingStation> StationsToShow(Player player, List<Piece> stationPieces)
    {
        List<CraftingStation> stations = [.. stationPieces.Select(piece => piece.m_craftingStation).Where(station => station != null)];
        return [.. stations.OrderBy(station => CraftingStation.HaveBuildStationInRange(station.m_name, player.transform.position) != null)];
    }

    /// <summary>
    /// Runs after Hud.SetupPieceInfo when it showed a summary: the game filled in the first station's
    /// slot after the materials, so fill in the other stations' slots after it the same way.
    /// </summary>
    internal static void AfterSetupPieceInfo(Hud hud, Piece shown)
    {
        int slot = shown.m_resources.Length + 1;
        foreach (CraftingStation station in ExtraStations)
        {
            if (slot >= hud.m_requirementItems.Length)
            {
                break;
            }

            GameObject item = hud.m_requirementItems[slot++];
            item.SetActive(true);
            Image icon = item.transform.Find("res_icon").GetComponent<Image>();
            TMP_Text name = item.transform.Find("res_name").GetComponent<TMP_Text>();
            TMP_Text amount = item.transform.Find("res_amount").GetComponent<TMP_Text>();
            icon.sprite = station.m_icon;
            name.text = Localization.instance.Localize(station.m_name);
            item.GetComponent<UITooltip>().m_text = station.m_name;

            // As the game shows its one station: lit and marked when in range, grey with a flashing "None"
            // when not.
            CraftingStation inRange = CraftingStation.HaveBuildStationInRange(station.m_name, Player.m_localPlayer.transform.position);
            if (inRange != null)
            {
                inRange.ShowAreaMarker();
                icon.color = Color.white;
                amount.text = "";
                amount.color = Color.white;
            }
            else
            {
                icon.color = Color.gray;
                amount.text = Localization.instance.Localize("$menu_none");
                amount.color = Mathf.Sin(Time.time * 10f) > 0f && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost) ? Color.red : Color.white;
            }
        }
    }

    /// <summary>
    /// Makes the selected pieces glow, pulsing, while a hammer is out. Runs in LateUpdate so it wins over
    /// the game's own hover highlight.
    /// </summary>
    internal static void UpdateHighlight()
    {
        Player player = Player.m_localPlayer;
        bool show = Selected.Count > 0
            && MaterialMan.instance != null
            && player != null
            && player.InPlaceMode()
            && BetterHammerPlugin.IsHammerEquipped(player);
        if (!show)
        {
            if (_lit)
            {
                ClearHighlights();
            }

            return;
        }

        Prune();
        float pulse = Mathf.Lerp(0.3f, 0.7f, (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f);
        foreach (Piece piece in Selected)
        {
            MaterialMan.instance.SetValue(piece.gameObject, EmissionColor, HighlightColor * pulse);
        }

        _lit = true;
    }

    private static void ClearHighlights()
    {
        foreach (Piece piece in Selected)
        {
            ResetHighlight(piece);
        }

        _lit = false;
    }

    private static void ResetHighlight(Piece piece)
    {
        if (piece != null && MaterialMan.instance != null)
        {
            MaterialMan.instance.ResetValue(piece.gameObject, EmissionColor);
        }
    }

    /// <summary>
    /// Drops selected pieces that no longer exist (removed, destroyed or unloaded).
    /// </summary>
    private static void Prune()
    {
        int removed = Selected.RemoveAll(piece => piece == null || piece.GetComponent<ZNetView>() is not { } nview || !nview.IsValid());
        if (removed > 0)
        {
            _selectionDirty = true;
        }
    }

    private static string Count(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
