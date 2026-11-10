using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using UnityEngine;

using Object = UnityEngine.Object;

namespace NoHammerBuildMenu;

/// <summary>
/// <para>
/// Copying several pieces at once. Alt+middle-click adds the piece you're looking at to the selection,
/// Alt+right-click takes it out again, and Alt+left-click picks up a copy of the whole selection as one
/// placement ghost. Left-click then builds it (as often as you like, like any piece) and right-click
/// puts it away. Building works as normal while you're selecting, and the selection survives putting the
/// hammer away. It's used up by the first copy you build.
/// </para>
/// <para>
/// The game's own ghost is used for one of the copied pieces, the reference piece, so its placement
/// checks, snapping and the build grid all apply to it as normal. The other pieces get their own ghosts
/// that follow it, keeping their places relative to it. Q/E steps through the snap points of every piece
/// in turn, and Shift+Q/E jumps from piece to piece.
/// </para>
/// </summary>
internal static class MultiCopy
{
    private const string RemoveButton = "Remove";
    private const string PlaceButton = "Attack";

    /// <summary>
    /// Teal, so it stands apart from the game's pale blue hover glow and the gold of a piece being moved.
    /// </summary>
    private static readonly Color HighlightColor = new(0.25f, 0.9f, 0.75f);
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private sealed class GroupPiece
    {
        public Piece Prefab;
        public Vector3 Position;
        public Quaternion Rotation;
        public int SnapCount;

        /// <summary>
        /// This piece's own ghost, or null for the reference piece, which uses the game's ghost.
        /// </summary>
        public GameObject Ghost;
    }

    /// <summary>
    /// Built pieces picked with Alt+middle-click, in the order they were picked.
    /// </summary>
    private static readonly List<Piece> Selected = [];

    /// <summary>
    /// The copy being placed, taken from the selection when it was confirmed.
    /// </summary>
    private static readonly List<GroupPiece> Group = [];
    private static readonly List<Piece> GroupPrefabs = [];

    private static int _reference;
    private static Piece _previousPiece;
    private static int _previousRotation;
    private static int _rightClickFrame = -10;
    private static int _snapFrame = -10;
    private static bool _lit;

    private static Piece _summary;
    private static bool _summaryDirty = true;
    private static bool _summaryOfSelection;
    private static Piece.Requirement[] _allRequirements = [];
    private static readonly List<Piece> SummaryStations = [];
    private static readonly List<Transform> SnapPoints = [];

    internal static bool Placing => Group.Count > 0;

    internal static bool HasSelection => Selected.Count > 0;

    /// <summary>
    /// Right-click puts the copy away, and Alt+right-click deselects, so neither may also open the build
    /// menu or toggle repair mode or the grid.
    /// </summary>
    internal static bool RightClickConsumed => Placing || _rightClickFrame == Time.frameCount;

    /// <summary>
    /// The combined materials while they're being taken for a copy, so the chest materials feature
    /// knows it's a build cost.
    /// </summary>
    internal static Piece.Requirement[] ConsumingRequirements { get; private set; }

    /// <summary>
    /// Runs before Player.UpdatePlacement for the local player. Returns whether vanilla placing must be
    /// blocked this frame.
    /// </summary>
    internal static bool BeforeUpdatePlacement(Player player, bool takeInput)
    {
        Prune();

        string abortReason = Placing ? AbortReason(player) : null;
        if (abortReason != null)
        {
            Stop(player);
            player.Message(MessageHud.MessageType.TopLeft, abortReason);
        }

        bool wasPlacing = Placing;
        if (!takeInput
            || !player.InPlaceMode()
            || player.IsDead()
            || Hud.IsPieceSelectionVisible()
            || Hud.InRadial()
            || ZInput.IsGamepadActive()
            || !NoHammerBuildMenuPlugin.IsHammerEquipped(player))
        {
            return wasPlacing;
        }

        bool alt = NoHammerBuildMenuPlugin.AltHeld();
        bool placePressed = ZInput.GetButtonDown(PlaceButton);
        bool block = wasPlacing;
        if (alt && ZInput.GetButtonDown(RemoveButton))
        {
            PieceMover.BlockRemoveUntilReleased(player);
            Select(player);
        }
        else if (alt && Input.GetMouseButtonDown(1))
        {
            _rightClickFrame = Time.frameCount;
            Deselect(player);
        }
        else if (alt && placePressed)
        {
            // Stops vanilla placing the selected piece this frame.
            player.m_lastToolUseTime = Time.time;
            block = true;
            Confirm(player);
        }
        else if (Placing && Input.GetMouseButtonDown(1))
        {
            _rightClickFrame = Time.frameCount;
            Stop(player);
            player.Message(MessageHud.MessageType.TopLeft, "$nhbm_copy_aborted");
        }
        else if (Placing && placePressed && Time.time - player.m_lastToolUseTime > player.m_placeDelay)
        {
            Place(player);
        }

        return block || Placing;
    }

    /// <summary>
    /// Ends the copy because a move is starting.
    /// </summary>
    internal static void Abort(Player player)
    {
        if (Placing)
        {
            Stop(player);
            player.Message(MessageHud.MessageType.TopLeft, "$nhbm_copy_aborted");
        }
    }

    internal static void Reset()
    {
        ClearHighlights();
        Selected.Clear();
        DestroyGhosts();
        Group.Clear();
        GroupPrefabs.Clear();
        _previousPiece = null;
        _summaryDirty = true;
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
            player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$nhbm_copy_already", name));
            return;
        }

        ZNetView nview = piece.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid() || !player.m_buildPieces.GetPieceIndex(piece, out _, out _))
        {
            // Same as copying a single piece: this hammer can't build it, or you don't know it yet.
            player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
            return;
        }

        Selected.Add(piece);
        _summaryDirty = true;
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$nhbm_copy_selected", name, Count(Selected.Count)));
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
        _summaryDirty = true;
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$nhbm_copy_deselected", Localization.instance.Localize(piece.m_name), Count(Selected.Count)));
    }

    /// <summary>
    /// Alt+left-click: picks up a copy of the selection, with the first piece selected as the reference.
    /// </summary>
    private static void Confirm(Player player)
    {
        Prune();
        if (Selected.Count == 0)
        {
            player.Message(MessageHud.MessageType.Center, "$nhbm_copy_nothing");
            return;
        }

        List<GroupPiece> group = [];
        foreach (Piece piece in Selected)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(Utils.GetPrefabName(piece.gameObject));
            Piece prefabPiece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (prefabPiece == null || !player.m_buildPieces.GetPieceIndex(prefabPiece, out _, out _))
            {
                // The hammer was swapped for one with different pieces since this one was selected.
                player.Message(MessageHud.MessageType.Center,
                    Localization.instance.Localize("$nhbm_copy_unavailable", Localization.instance.Localize(piece.m_name)));
                return;
            }

            SnapPoints.Clear();
            prefabPiece.GetSnapPoints(SnapPoints);
            group.Add(new GroupPiece
            {
                Prefab = prefabPiece,
                Position = piece.transform.position,
                Rotation = piece.transform.rotation,
                SnapCount = SnapPoints.Count,
            });
        }

        SnapPoints.Clear();
        PieceMover.Abort(player);

        if (Placing)
        {
            // Swapping one copy for another: keep the piece to go back to from before the first.
            DestroyGhosts();
            Group.Clear();
        }
        else
        {
            _previousPiece = player.GetSelectedPiece();
            _previousRotation = player.m_placeRotation;
        }

        if (player.m_placementGhost != null)
        {
            Object.Destroy(player.m_placementGhost);
            player.m_placementGhost = null;
        }

        foreach (GroupPiece piece in group)
        {
            piece.Ghost = CreateGhost(player, piece.Prefab);
        }

        Group.AddRange(group);
        GroupPrefabs.Clear();
        GroupPrefabs.AddRange(group.Select(piece => piece.Prefab));
        _summaryDirty = true;

        UseAsReference(player, 0);
        player.m_placeRotation = RotationSteps(player, Group[0].Rotation);
        player.m_manualSnapPoint = -1;
        player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$nhbm_copy_started", Count(Group.Count)));
    }

    /// <summary>
    /// A ghost for the piece, made by the game's own ghost setup (so it's stripped down the same way)
    /// and then taken from it. Expects the game to have no ghost of its own at the time.
    /// </summary>
    private static GameObject CreateGhost(Player player, Piece prefab)
    {
        if (!player.SetSelectedPiece(prefab))
        {
            return null;
        }

        if (player.m_placementGhost == null)
        {
            player.SetupPlacementGhost();
        }

        GameObject ghost = player.m_placementGhost;
        player.m_placementGhost = null;
        if (ghost != null)
        {
            ghost.SetActive(false);
        }

        return ghost;
    }

    /// <summary>
    /// Makes the piece the reference: selects it in the hammer and hands its ghost to the game.
    /// Expects the game to have no ghost of its own at the time.
    /// </summary>
    private static void UseAsReference(Player player, int index)
    {
        GroupPiece piece = Group[index];
        _ = player.SetSelectedPiece(piece.Prefab);
        if (player.m_placementGhost != null)
        {
            Object.Destroy(player.m_placementGhost);
        }

        player.m_placementGhost = piece.Ghost;
        if (player.m_placementGhost != null)
        {
            player.m_placementGhost.SetActive(true);
        }

        piece.Ghost = null;
        _reference = index;
    }

    /// <summary>
    /// Moves the game's ghost to another piece of the copy, keeping the copy's current turn.
    /// </summary>
    private static void SwitchReference(Player player, int index)
    {
        GameObject ghost = player.m_placementGhost;
        Quaternion turn = ghost != null ? ghost.transform.rotation * Quaternion.Inverse(Group[_reference].Rotation) : Quaternion.identity;

        Group[_reference].Ghost = ghost;
        if (ghost != null)
        {
            ghost.SetActive(false);
        }

        player.m_placementGhost = null;
        UseAsReference(player, index);
        player.m_placeRotation = RotationSteps(player, turn * Group[index].Rotation);
    }

    private static int RotationSteps(Player player, Quaternion rotation)
    {
        return Mathf.RoundToInt(rotation.eulerAngles.y / player.m_placeRotationDegrees);
    }

    /// <summary>
    /// Runs before Player.UpdatePlacementGhost. Q/E normally steps through the ghost's snap points; while
    /// placing a copy it steps through every piece's snap points in turn (switching the reference piece
    /// as needed), and Shift+Q/E steps from piece to piece.
    /// </summary>
    internal static void BeforeUpdatePlacementGhost(Player player)
    {
        if (!Placing || player.m_placementGhost == null || _snapFrame == Time.frameCount)
        {
            return;
        }

        // The same conditions the game reads Q/E under.
        if (ZInput.GetButton("JoyAltKeys")
            || Hud.IsPieceSelectionVisible()
            || (Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large)
            || Console.IsVisible()
            || (Chat.instance != null && Chat.instance.HasFocus()))
        {
            return;
        }

        int step = 0;
        if (ZInput.GetButtonDown("TabRight"))
        {
            step = 1;
        }
        else if (ZInput.GetButtonDown("TabLeft"))
        {
            step = -1;
        }

        if (step == 0)
        {
            return;
        }

        _snapFrame = Time.frameCount;
        int piece = _reference;
        int point = player.m_manualSnapPoint;
        if (NoHammerBuildMenuPlugin.ShiftHeld())
        {
            piece = (piece + step + Group.Count) % Group.Count;
            point = Group[piece].SnapCount > 0 ? 0 : -1;
        }
        else
        {
            NextSnapPoint(ref piece, ref point, step);
        }

        if (piece != _reference)
        {
            SwitchReference(player, piece);
        }

        // The game applies the same step to the snap point right after this, so start one step back.
        player.m_manualSnapPoint = point - step;
        player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$nhbm_copy_reference",
            Localization.instance.Localize(Group[piece].Prefab.m_name),
            Count(piece + 1),
            Count(Group.Count)));
    }

    /// <summary>
    /// One step through the list "automatic, then every snap point of the first piece, then of the
    /// second, ...", wrapping round at either end. Automatic keeps the current reference piece.
    /// </summary>
    private static void NextSnapPoint(ref int piece, ref int point, int step)
    {
        List<(int Piece, int Point)> entries = [(-1, -1)];
        for (int i = 0; i < Group.Count; i++)
        {
            for (int s = 0; s < Group[i].SnapCount; s++)
            {
                entries.Add((i, s));
            }
        }

        int current = point < 0 ? 0 : entries.IndexOf((piece, point));
        if (current < 0)
        {
            current = 0;
        }

        (int nextPiece, int nextPoint) = entries[(current + step + entries.Count) % entries.Count];
        if (nextPiece >= 0)
        {
            piece = nextPiece;
        }

        point = nextPoint;
    }

    /// <summary>
    /// Runs after Player.UpdatePlacementGhost: places the other pieces' ghosts around the reference
    /// ghost, and checks their spots for wards and no-build zones too.
    /// </summary>
    internal static void AfterUpdatePlacementGhost(Player player, bool flashGuardStone)
    {
        if (!Placing)
        {
            return;
        }

        GameObject reference = player.m_placementGhost;
        bool active = reference != null && reference.activeSelf;
        Player.PlacementStatus status = player.m_placementStatus;
        Quaternion turn = reference != null ? reference.transform.rotation * Quaternion.Inverse(Group[_reference].Rotation) : Quaternion.identity;
        Vector3 origin = Group[_reference].Position;

        foreach (GroupPiece piece in Group)
        {
            if (piece.Ghost == null)
            {
                continue;
            }

            piece.Ghost.SetActive(active);
            if (!active)
            {
                continue;
            }

            Vector3 position = reference.transform.position + (turn * (piece.Position - origin));
            piece.Ghost.transform.SetPositionAndRotation(position, turn * piece.Rotation);
            if (status != Player.PlacementStatus.Valid)
            {
                continue;
            }

            if (Location.IsInsideNoBuildLocation(position))
            {
                status = Player.PlacementStatus.NoBuildZone;
            }
            else if (!PrivateArea.CheckAccess(position, 0f, flashGuardStone))
            {
                status = Player.PlacementStatus.PrivateZone;
            }
        }

        if (!active)
        {
            return;
        }

        if (status != player.m_placementStatus)
        {
            player.m_placementStatus = status;
            player.SetPlacementGhostValid(false);
        }

        bool valid = status == Player.PlacementStatus.Valid;
        foreach (GroupPiece piece in Group)
        {
            Piece ghostPiece = piece.Ghost != null ? piece.Ghost.GetComponent<Piece>() : null;
            if (ghostPiece != null)
            {
                ghostPiece.SetInvalidPlacementHeightlight(!valid);
            }
        }
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

        if (!HaveStations(player))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_missingstation");
            return;
        }

        if (!HaveMaterials(player))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
            return;
        }

        // Placing a piece that starts at a random turn re-rolls the build rotation; keep the copy's.
        int rotation = player.m_placeRotation;
        foreach (GroupPiece piece in Group)
        {
            Transform ghost = piece.Ghost != null ? piece.Ghost.transform : reference.transform;
            player.PlacePiece(piece.Prefab, ghost.position, ghost.rotation, doAttack: piece.Ghost == null);
        }

        player.m_placeRotation = rotation;

        if (!player.m_noPlacementCost && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost))
        {
            ConsumingRequirements = _allRequirements;
            try
            {
                player.ConsumeResources(_allRequirements, 0);
            }
            finally
            {
                ConsumingRequirements = null;
            }
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
        player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$nhbm_copy_placed", Count(Group.Count)));

        // The selection has served its purpose; the copy stays in hand to build again.
        ClearHighlights();
        Selected.Clear();
        _summaryDirty = true;
    }

    private static bool HaveStations(Player player)
    {
        if (player.m_noPlacementCost || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench))
        {
            return true;
        }

        foreach (Piece piece in GroupPrefabs)
        {
            if (piece.m_craftingStation != null
                && CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position) == null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveMaterials(Player player)
    {
        if (player.m_noPlacementCost)
        {
            return true;
        }

        // The summary piece, with every material and no station (stations were checked separately), goes
        // through the game's own check, which the chest materials feature extends.
        Piece summary = Summary(GroupPrefabs, forSelection: false);
        Piece.Requirement[] shown = summary.m_resources;
        CraftingStation station = summary.m_craftingStation;
        summary.m_resources = _allRequirements;
        summary.m_craftingStation = null;
        try
        {
            return player.HaveRequirements(summary, Player.RequirementMode.CanBuild);
        }
        finally
        {
            summary.m_resources = shown;
            summary.m_craftingStation = station;
        }
    }

    /// <summary>
    /// The piece the build panel should describe instead of the selected one: the selection while Alt is
    /// held, or the copy while it's being placed.
    /// </summary>
    internal static bool TryGetSummary(out Piece summary)
    {
        summary = null;
        Player player = Player.m_localPlayer;
        if (player == null || Hud.IsPieceSelectionVisible() || !NoHammerBuildMenuPlugin.IsHammerEquipped(player))
        {
            return false;
        }

        Prune();
        bool forSelection = Selected.Count > 0 && NoHammerBuildMenuPlugin.AltHeld();
        if (!forSelection && !Placing)
        {
            return false;
        }

        summary = Summary(forSelection ? Selected : GroupPrefabs, forSelection);
        summary.m_craftingStation = StationToShow(player);
        return true;
    }

    /// <summary>
    /// The build panel has room for one station: show one that's out of range if there is one.
    /// </summary>
    private static CraftingStation StationToShow(Player player)
    {
        CraftingStation first = null;
        foreach (Piece piece in SummaryStations)
        {
            CraftingStation station = piece.m_craftingStation;
            first ??= station;
            if (CraftingStation.HaveBuildStationInRange(station.m_name, player.transform.position) == null)
            {
                return station;
            }
        }

        return first;
    }

    /// <summary>
    /// A stand-in piece describing several: their count, what they are, and their combined materials.
    /// </summary>
    private static Piece Summary(List<Piece> pieces, bool forSelection)
    {
        if (_summary == null)
        {
            GameObject holder = new("NoHammerBuildMenu_CopySummary");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            _summary = holder.AddComponent<Piece>();
        }

        if (!_summaryDirty && _summaryOfSelection == forSelection)
        {
            return _summary;
        }

        _summaryDirty = false;
        _summaryOfSelection = forSelection;

        List<Piece.Requirement> combined = [];
        Dictionary<string, Piece.Requirement> byItem = [];
        SummaryStations.Clear();
        HashSet<string> stations = [];
        foreach (Piece piece in pieces)
        {
            foreach (Piece.Requirement requirement in piece.m_resources)
            {
                if (requirement.m_resItem == null || requirement.m_amount <= 0)
                {
                    continue;
                }

                string item = requirement.m_resItem.m_itemData.m_shared.m_name;
                if (byItem.TryGetValue(item, out Piece.Requirement total))
                {
                    total.m_amount += requirement.m_amount;
                    continue;
                }

                total = new Piece.Requirement
                {
                    m_resItem = requirement.m_resItem,
                    m_amount = requirement.m_amount,
                    m_recover = requirement.m_recover,
                };
                byItem[item] = total;
                combined.Add(total);
            }

            if (piece.m_craftingStation != null && stations.Add(piece.m_craftingStation.m_name))
            {
                SummaryStations.Add(piece);
            }
        }

        _allRequirements = [.. combined];

        // One requirement slot is kept for the station.
        int slots = Hud.instance != null ? Mathf.Max(0, Hud.instance.m_requirementItems.Length - 1) : combined.Count;
        _summary.m_resources = [.. combined.Take(slots)];

        IEnumerable<string> counts = pieces
            .GroupBy(piece => piece.m_name)
            .Select(same => Count(same.Count()) + "× " + Localization.instance.Localize(same.Key));
        string description = string.Join(", ", counts);
        if (combined.Count > slots)
        {
            description += "\n" + Localization.instance.Localize("$nhbm_copy_more_materials", Count(combined.Count - slots));
        }

        _summary.m_name = Localization.instance.Localize(
            forSelection ? "$nhbm_copy_summary_selected" : "$nhbm_copy_summary", Count(pieces.Count));
        _summary.m_description = description;
        _summary.m_icon = pieces.Count > 0 ? pieces[0].m_icon : null;
        return _summary;
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
            && NoHammerBuildMenuPlugin.IsHammerEquipped(player);
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
            _summaryDirty = true;
        }
    }

    private static string AbortReason(Player player)
    {
        bool unequipped = !player.InPlaceMode() || !NoHammerBuildMenuPlugin.IsHammerEquipped(player);
        return (unequipped, ReferenceSelected(player)) switch
        {
            (true, _) => "$nhbm_copy_aborted_unequipped",
            (_, false) => "$nhbm_copy_aborted_selection",
            _ => null,
        };
    }

    private static bool ReferenceSelected(Player player)
    {
        Piece selected = player.GetSelectedPiece();
        return selected != null && Utils.GetPrefabName(selected.gameObject) == Group[_reference].Prefab.name;
    }

    /// <summary>
    /// Puts the copy away and, if its reference piece is still the selected one, goes back to the piece
    /// and rotation selected before it. The selection itself is kept.
    /// </summary>
    private static void Stop(Player player)
    {
        bool restore = player.InPlaceMode() && ReferenceSelected(player);
        DestroyGhosts();
        Group.Clear();
        GroupPrefabs.Clear();
        _summaryDirty = true;

        if (restore)
        {
            if (_previousPiece != null)
            {
                _ = player.SetSelectedPiece(_previousPiece);
            }

            player.m_placeRotation = _previousRotation;
        }

        _previousPiece = null;
    }

    private static void DestroyGhosts()
    {
        foreach (GroupPiece piece in Group)
        {
            if (piece.Ghost != null)
            {
                Object.Destroy(piece.Ghost);
                piece.Ghost = null;
            }
        }
    }

    private static string Count(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
