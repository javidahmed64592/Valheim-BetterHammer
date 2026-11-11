using System.Collections.Generic;

using BepInEx.Configuration;

using UnityEngine;

namespace BetterHammer.Structures;

/// <summary>
/// Finds every built piece connected to a piece, directly or through other pieces. Two pieces are
/// connected when they touch, tested the way the game finds a piece's supports: each of a piece's solid
/// colliders is grown slightly and anything it overlaps counts. The ground doesn't connect pieces, and
/// neither do ships or carts.
/// </summary>
internal static class ConnectedPieces
{
    /// <summary>
    /// How far each collider is grown on every side; the game grows them by the same amount when it
    /// looks for supports.
    /// </summary>
    private const float Margin = 0.15f;

    private static readonly Collider[] Hits = new Collider[512];
    private static readonly List<Collider> Colliders = [];
    private static int _mask;

    private static ConfigEntry<int> _maxPieces;

    internal static int MaxPieces => _maxPieces != null ? Mathf.Max(1, _maxPieces.Value) : 1000;

    public static void BindConfig(ConfigFile config)
    {
        _maxPieces = config.Bind(
            "Copying",
            "Max Connected Pieces",
            1000,
            "The most pieces selecting or deselecting connected pieces looks at in one go, so a click on a large build can't freeze the game.");
    }

    /// <summary>
    /// The start piece and every piece connected to it, nearest first. Stops at <see cref="MaxPieces"/>,
    /// setting `limited`.
    /// </summary>
    internal static List<Piece> Find(Piece start, out bool limited)
    {
        if (_mask == 0)
        {
            _mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "Default_small");
        }

        int max = MaxPieces;
        limited = false;
        List<Piece> found = [start];
        HashSet<Piece> seen = [start];
        for (int next = 0; next < found.Count; next++)
        {
            Piece piece = found[next];
            if (piece == null)
            {
                continue;
            }

            Colliders.Clear();
            piece.GetComponentsInChildren(includeInactive: false, Colliders);
            foreach (Collider collider in Colliders)
            {
                if (!IsSolid(collider))
                {
                    continue;
                }

                GetBox(collider, out Vector3 center, out Vector3 halfSize, out Quaternion rotation);
                int count = Physics.OverlapBoxNonAlloc(center, halfSize, Hits, rotation, _mask);
                for (int i = 0; i < count; i++)
                {
                    if (!IsSolid(Hits[i]))
                    {
                        continue;
                    }

                    Piece other = Hits[i].GetComponentInParent<Piece>();
                    if (other == null || !seen.Add(other) || other.GetComponent<ZNetView>() is not { } nview || !nview.IsValid())
                    {
                        continue;
                    }

                    if (found.Count >= max)
                    {
                        limited = true;
                        Colliders.Clear();
                        return found;
                    }

                    found.Add(other);
                }
            }
        }

        Colliders.Clear();
        return found;
    }

    /// <summary>
    /// Colliders the game counts for support: not triggers, and not part of anything that moves on its
    /// own (ships, carts).
    /// </summary>
    private static bool IsSolid(Collider collider)
    {
        return collider != null && collider.enabled && !collider.isTrigger && collider.attachedRigidbody == null;
    }

    /// <summary>
    /// The collider as a box, grown by <see cref="Margin"/>: a box collider exactly, anything else by
    /// its world bounds.
    /// </summary>
    private static void GetBox(Collider collider, out Vector3 center, out Vector3 halfSize, out Quaternion rotation)
    {
        if (collider is BoxCollider box)
        {
            Transform transform = box.transform;
            Vector3 scale = transform.lossyScale;
            center = transform.TransformPoint(box.center);
            rotation = transform.rotation;
            halfSize = new Vector3(Mathf.Abs(scale.x * box.size.x), Mathf.Abs(scale.y * box.size.y), Mathf.Abs(scale.z * box.size.z)) * 0.5f;
        }
        else
        {
            Bounds bounds = collider.bounds;
            center = bounds.center;
            rotation = Quaternion.identity;
            halfSize = bounds.extents;
        }

        halfSize += Vector3.one * Margin;
    }
}
