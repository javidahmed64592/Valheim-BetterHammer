using BetterHammer.Core;

using UnityEngine;

namespace BetterHammer.Structures;

/// <summary>
/// <para>
/// Carries PaintBrush's paint through BetterHammer's building (see <see cref="PaintBridge"/>; without PaintBrush
/// nothing here does anything).
/// </para>
/// <para>
/// Pieces built by a saved copy or rebuilt by undo get the paint the caller sets in <see cref="PendingPaint"/>
/// while it places them. Shift+Middle-Click copies a piece's paint along with its type, and the pieces then built
/// with that selection get the paint, until another piece is chosen. The game's placement ghost shows the paint of
/// the piece being moved or copied; ghosts of a group (a saved copy or a moved selection) are painted by
/// <see cref="GhostGroup"/>.
/// </para>
/// </summary>
internal static class PaintCopy
{
    private static string _copiedPrefab;
    private static int _copiedPaint;
    private static int _copyingPaint;
    private static string _copyingPrefab;

    /// <summary>
    /// The paint for the pieces placed while it's set, or null to use the copied piece's paint.
    /// </summary>
    internal static int? PendingPaint { get; set; }

    internal static void Reset()
    {
        PendingPaint = null;
        ClearCopied();
    }

    /// <summary>
    /// Runs after Piece.SetCreator while the local player's Player.PlacePiece is running, on the piece it made.
    /// </summary>
    internal static void OnPiecePlaced(Piece piece)
    {
        if (piece == null)
        {
            return;
        }

        int paint = PendingPaint ?? (Utils.GetPrefabName(piece.gameObject) == _copiedPrefab ? _copiedPaint : 0);
        PaintBridge.SetPaint(piece.gameObject, paint);
    }

    /// <summary>
    /// Runs before Player.CopyPiece: notes the paint of the piece it's about to copy, found the way it finds it.
    /// </summary>
    internal static void BeforeCopyPiece(Player player)
    {
        _copyingPaint = 0;
        _copyingPrefab = null;
        if (!PaintBridge.Available || player != Player.m_localPlayer || !BetterHammerPlugin.IsHammerEquipped(player))
        {
            return;
        }

        Transform camera = GameCamera.instance != null ? GameCamera.instance.transform : null;
        if (camera == null
            || !Physics.Raycast(camera.position, camera.forward, out RaycastHit hit, 50f, player.m_removeRayMask)
            || Vector3.Distance(hit.point, player.m_eye.position) >= player.m_maxPlaceDistance)
        {
            return;
        }

        Piece piece = hit.collider.GetComponentInParent<Piece>();
        if (piece != null)
        {
            _copyingPaint = PaintBridge.GetPaint(piece.gameObject);
            _copyingPrefab = Utils.GetPrefabName(piece.gameObject);
        }
    }

    /// <summary>
    /// Runs after Player.CopyPiece: if it selected the piece, its paint goes with the selection.
    /// </summary>
    internal static void AfterCopyPiece(bool copied)
    {
        if (copied && _copyingPrefab != null)
        {
            _copiedPrefab = _copyingPaint != 0 ? _copyingPrefab : null;
            _copiedPaint = _copyingPaint;
        }

        _copyingPrefab = null;
    }

    /// <summary>
    /// Choosing a piece in the build menu starts a fresh selection, without a copied paint.
    /// </summary>
    internal static void ClearCopied()
    {
        _copiedPrefab = null;
        _copiedPaint = 0;
    }

    /// <summary>
    /// Every frame: drops the copied paint once another piece is selected, and shows the right paint on the game's
    /// placement ghost.
    /// </summary>
    internal static void Update(Player player)
    {
        if (!PaintBridge.Available)
        {
            return;
        }

        Piece selected = player.GetSelectedPiece();
        if (_copiedPrefab != null && (selected == null || Utils.GetPrefabName(selected.gameObject) != _copiedPrefab))
        {
            ClearCopied();
        }

        GameObject ghost = player.m_placementGhost;
        if (ghost == null || GhostGroup.Active)
        {
            return;
        }

        int paint = 0;
        if (BetterHammerPlugin.IsHammerEquipped(player))
        {
            paint = PieceMover.Moving ? PieceMover.SinglePaint : _copiedPaint;
        }

        PaintBridge.ShowOnGhost(ghost, paint);
    }
}
