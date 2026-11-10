using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

using Object = UnityEngine.Object;

namespace BetterHammer.Structures;

/// <summary>
/// <para>
/// A placement ghost made of several pieces, used both for building a saved copy (<see cref="MultiCopy"/>)
/// and for moving several selected pieces at once (<see cref="PieceMover"/>).
/// </para>
/// <para>
/// The game's own ghost is used for one of the pieces, the reference piece, so its placement checks,
/// snapping and the build grid all apply to it as normal. The other pieces get their own ghosts that
/// follow it, keeping their places relative to it, and their spots are checked for wards and no-build
/// zones too. Q/E steps through the snap points of every piece in turn, and Shift+Q/E jumps from piece to
/// piece.
/// </para>
/// </summary>
internal static class GhostGroup
{
    internal enum Owner { None = 0, Copy = 1, Move = 2 }

    private sealed class Member
    {
        public CopyLibrary.Part Part;

        /// <summary>
        /// This piece's own ghost, or null for the reference piece, which uses the game's ghost.
        /// </summary>
        public GameObject Ghost;
    }

    private static readonly List<Member> Members = [];
    private static int _reference;
    private static int _snapFrame = -10;

    internal static Owner ActiveOwner { get; private set; }

    internal static bool Active => Members.Count > 0;

    internal static int Count => Members.Count;

    /// <summary>
    /// True while the group's own ghosts are being set up through the game's ghost setup.
    /// </summary>
    internal static bool CreatingGhosts { get; private set; }

    /// <summary>
    /// While the game sets up a ghost for one of the pieces, the prefab it should use instead of the
    /// selected piece (see the PieceTable.GetSelectedPrefab patch).
    /// </summary>
    internal static GameObject PrefabOverride { get; private set; }

    /// <summary>
    /// Replaces the game's ghost with ghosts of the given pieces, the first being the reference. The
    /// parts' positions only matter relative to each other.
    /// </summary>
    internal static void Begin(Player player, IList<CopyLibrary.Part> parts, Owner owner)
    {
        End();
        if (player.m_placementGhost != null)
        {
            Object.Destroy(player.m_placementGhost);
            player.m_placementGhost = null;
        }

        // Setting up a ghost for a piece that starts at a random turn re-rolls the build rotation.
        int rotation = player.m_placeRotation;
        CreatingGhosts = true;
        try
        {
            foreach (CopyLibrary.Part part in parts)
            {
                Members.Add(new Member { Part = part, Ghost = CreateGhost(player, part.Prefab) });
            }
        }
        finally
        {
            CreatingGhosts = false;
            PrefabOverride = null;
        }

        player.m_placeRotation = rotation;
        ActiveOwner = owner;
        UseAsReference(player, 0);
        player.m_manualSnapPoint = -1;
    }

    /// <summary>
    /// Destroys the other pieces' ghosts. The reference ghost is the game's, which it replaces itself.
    /// </summary>
    internal static void End()
    {
        foreach (Member member in Members)
        {
            if (member.Ghost != null)
            {
                Object.Destroy(member.Ghost);
            }
        }

        Members.Clear();
        ActiveOwner = Owner.None;
    }

    /// <summary>
    /// Where each piece's ghost is, in the order the parts were given.
    /// </summary>
    internal static List<(Vector3 Position, Quaternion Rotation)> Poses(Player player)
    {
        List<(Vector3, Quaternion)> poses = [];
        foreach (Member member in Members)
        {
            Transform ghost = member.Ghost != null ? member.Ghost.transform : player.m_placementGhost.transform;
            poses.Add((ghost.position, ghost.rotation));
        }

        return poses;
    }

    /// <summary>
    /// The build rotation that makes the reference ghost face the way the given rotation does.
    /// </summary>
    internal static int RotationSteps(Player player, Quaternion rotation)
    {
        return Mathf.RoundToInt(rotation.eulerAngles.y / player.m_placeRotationDegrees);
    }

    /// <summary>
    /// A ghost for the piece, made by the game's own ghost setup (so it's stripped down the same way)
    /// and then taken from it. Expects the game to have no ghost of its own at the time.
    /// </summary>
    private static GameObject CreateGhost(Player player, Piece prefab)
    {
        PrefabOverride = prefab.gameObject;
        player.SetupPlacementGhost();
        PrefabOverride = null;

        GameObject ghost = player.m_placementGhost;
        player.m_placementGhost = null;
        if (ghost != null)
        {
            ghost.SetActive(false);
        }

        return ghost;
    }

    /// <summary>
    /// Hands the piece's ghost to the game. Expects the game to have no ghost of its own at the time.
    /// </summary>
    private static void UseAsReference(Player player, int index)
    {
        Member member = Members[index];
        player.m_placementGhost = member.Ghost;
        if (player.m_placementGhost != null)
        {
            player.m_placementGhost.SetActive(true);
        }

        member.Ghost = null;
        _reference = index;
    }

    /// <summary>
    /// Moves the game's ghost to another piece of the group, keeping the group's current turn.
    /// </summary>
    private static void SwitchReference(Player player, int index)
    {
        GameObject ghost = player.m_placementGhost;
        Quaternion turn = ghost != null ? ghost.transform.rotation * Quaternion.Inverse(Members[_reference].Part.Rotation) : Quaternion.identity;

        Members[_reference].Ghost = ghost;
        if (ghost != null)
        {
            ghost.SetActive(false);
        }

        player.m_placementGhost = null;
        UseAsReference(player, index);
        player.m_placeRotation = RotationSteps(player, turn * Members[index].Part.Rotation);
    }

    /// <summary>
    /// Runs before Player.UpdatePlacementGhost. Q/E normally steps through the ghost's snap points; for a
    /// group it steps through every piece's snap points in turn (switching the reference piece as needed),
    /// and Shift+Q/E steps from piece to piece.
    /// </summary>
    internal static void BeforeUpdatePlacementGhost(Player player)
    {
        if (!Active || player.m_placementGhost == null || _snapFrame == Time.frameCount)
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
        if (BetterHammerPlugin.ShiftHeld())
        {
            piece = (piece + step + Members.Count) % Members.Count;
            point = Members[piece].Part.SnapCount > 0 ? 0 : -1;
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
        player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$betterhammer_copy_reference",
            Localization.instance.Localize(Members[piece].Part.Prefab.m_name),
            (piece + 1).ToString(CultureInfo.InvariantCulture),
            Members.Count.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// One step through the list "automatic, then every snap point of the first piece, then of the
    /// second, ...", wrapping round at either end. Automatic keeps the current reference piece.
    /// </summary>
    private static void NextSnapPoint(ref int piece, ref int point, int step)
    {
        List<(int Piece, int Point)> entries = [(-1, -1)];
        for (int i = 0; i < Members.Count; i++)
        {
            for (int s = 0; s < Members[i].Part.SnapCount; s++)
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
        if (!Active)
        {
            return;
        }

        GameObject reference = player.m_placementGhost;
        bool active = reference != null && reference.activeSelf;
        Player.PlacementStatus status = player.m_placementStatus;
        Quaternion turn = reference != null ? reference.transform.rotation * Quaternion.Inverse(Members[_reference].Part.Rotation) : Quaternion.identity;
        Vector3 origin = Members[_reference].Part.Position;

        foreach (Member member in Members)
        {
            if (member.Ghost == null)
            {
                continue;
            }

            member.Ghost.SetActive(active);
            if (!active)
            {
                continue;
            }

            Vector3 position = reference.transform.position + (turn * (member.Part.Position - origin));
            member.Ghost.transform.SetPositionAndRotation(position, turn * member.Part.Rotation);
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
        foreach (Member member in Members)
        {
            Piece ghostPiece = member.Ghost != null ? member.Ghost.GetComponent<Piece>() : null;
            if (ghostPiece != null)
            {
                ghostPiece.SetInvalidPlacementHeightlight(!valid);
            }
        }
    }
}
