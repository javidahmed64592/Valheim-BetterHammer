using HarmonyLib;

using UnityEngine;

namespace BetterHammer.Patches;

/// <summary>
/// Moves the aimed placement point so one of the ghost's snap points (or its origin, if it
/// has none) lands on the nearest grid vertex. This runs before vanilla positions the ghost
/// and applies its own piece-to-piece snapping, so snap points of existing pieces within
/// vanilla's snap range still win over the grid, and validity checks see the final position.
/// </summary>
[HarmonyPatch(typeof(Player), "PieceRayTest")]
internal static class Player_PieceRayTest_Patch
{
    /// <summary>
    /// Minimum normal component for a vertical face to count as axis-aligned (about 25 degrees).
    /// </summary>
    private const float AxisAlignedWall = 0.9f;

    private static float SnapTo(float value, float size)
    {
        return Mathf.Round(value / size) * size;
    }

    [HarmonyPostfix]
    private static void Postfix(Player __instance, bool __result, ref Vector3 point, ref Vector3 normal, ref Piece piece)
    {
        // Only touch `point`: the other out params must keep the values vanilla produced.
        if (!__result
            || __instance != Player.m_localPlayer
            || !BetterHammerPlugin.GridActive(__instance))
        {
            return;
        }

        // The game positions the ghost so that its anchor sits exactly at `point`: the
        // selected snap point in manual mode, otherwise the point on the piece's colliders
        // closest to the aim point (which varies with orientation and targeted surface).
        // So whatever the anchor is, snapping `point` itself puts it on the vertex.
        //
        // Only the coordinates lying in the targeted surface's plane are snapped. The one
        // along its normal stays where the aim ray hit, so the point never leaves the surface
        // and nothing snaps to empty air. Terrain and other non-piece surfaces keep snapping
        // horizontally only.
        float size = BetterHammerPlugin._gridSize.Value;
        GridPlane plane = GridPlane.Terrain;
        float sign = 1f;

        if (piece == null)
        {
            point.x = SnapTo(point.x, size);
            point.z = SnapTo(point.z, size);
        }
        else if (Mathf.Abs(normal.y) > 0.5f)
        {
            // Top or underside of a piece.
            point.x = SnapTo(point.x, size);
            point.z = SnapTo(point.z, size);
            plane = normal.y > 0f ? GridPlane.Top : GridPlane.Bottom;
            sign = Mathf.Sign(normal.y);
        }
        else if (Mathf.Abs(normal.x) > AxisAlignedWall)
        {
            point.y = SnapTo(point.y, size);
            point.z = SnapTo(point.z, size);
            plane = GridPlane.WallX;
            sign = Mathf.Sign(normal.x);
        }
        else if (Mathf.Abs(normal.z) > AxisAlignedWall)
        {
            point.x = SnapTo(point.x, size);
            point.y = SnapTo(point.y, size);
            plane = GridPlane.WallZ;
            sign = Mathf.Sign(normal.z);
        }
        else
        {
            // Diagonal wall: a horizontal snap would slide the point off the surface, so
            // snap the height only and skip the overlay.
            point.y = SnapTo(point.y, size);
            plane = GridPlane.None;
        }

        BetterHammerPlugin._aimFrame = Time.frameCount;
        BetterHammerPlugin._aimPoint = point;
        BetterHammerPlugin._aimPlane = plane;
        BetterHammerPlugin._aimSign = sign;
    }
}
