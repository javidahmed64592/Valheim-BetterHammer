using System.Collections.Generic;

using BepInEx.Configuration;

using UnityEngine;
using UnityEngine.Rendering;

namespace BetterHammer.Grid;

/// <summary>
/// Which surface the grid is drawn on. Terrain follows the ground height; the others are flat
/// planes whose normal points along world Y (Top / Bottom) or X / Z (walls).
/// </summary>
internal enum GridPlane { None = 0, Terrain = 1, Top = 2, Bottom = 3, WallX = 4, WallZ = 5 }

/// <summary>
/// Draws a square grid of lines around the build aim point. The grid is anchored to
/// world coordinates (vertices at multiples of the grid size), so it stays still while the
/// player moves their aim. On terrain the lines follow the ground height; on top of a
/// player-built piece (e.g. a floor) the grid is flat at that piece's surface height. It can also
/// be drawn on the underside of a piece or on its vertical faces.
/// </summary>
internal sealed class GridOverlay
{
    /// <summary>
    /// half-extent of the drawn grid
    /// </summary>
    private static ConfigEntry<float> _radius;
    /// <summary>
    /// distance from grid centre where lines start to fade
    /// </summary>
    private static ConfigEntry<float> _fadeStart;
    /// <summary>
    /// aim distance from grid centre that triggers a rebuild
    /// </summary>
    private static ConfigEntry<float> _rebuildDistance;
    /// <summary>
    /// height above the surface, keeps lines clear of grass and z-fighting
    /// </summary>
    private static ConfigEntry<float> _lift;
    /// <summary>
    /// line subdivision so lines can follow terrain
    /// </summary>
    private static ConfigEntry<float> _segmentLength;
    /// <summary>
    /// width of the regular grid lines
    /// </summary>
    private static ConfigEntry<float> _gridLineWidth;
    /// <summary>
    /// width of in-between lines relative to the 2m lines
    /// </summary>
    private static ConfigEntry<float> _minorLineScale;
    /// <summary>
    /// width of the thick lines drawn on world chunk (zone) borders
    /// </summary>
    private static ConfigEntry<float> _chunkLineWidth;
    /// <summary>
    /// slightly above the grid lines so they draw on top
    /// </summary>
    private static ConfigEntry<float> _chunkLineLift;
    private static ConfigEntry<Color> _lineColor;
    private static ConfigEntry<Color> _chunkLineColor;
    /// <summary>
    /// width of the lines through each chunk&#39;s centre
    /// </summary>
    private static ConfigEntry<float> _chunkCentreLineWidth;
    private static ConfigEntry<Color> _chunkCentreLineColor;

    private static float Radius => _radius.Value;
    /// <summary>
    /// Fade must start inside the radius, otherwise the fade division is by zero or negative.
    /// </summary>
    private static float FadeStart => Mathf.Min(_fadeStart.Value, Radius - 0.01f);
    private static float RebuildDistance => _rebuildDistance.Value;
    private static float Lift => _lift.Value;
    private static float SegmentLength => _segmentLength.Value;
    private static float GridLineWidth => _gridLineWidth.Value;
    private static float MinorLineWidth => GridLineWidth * _minorLineScale.Value;
    private static float ChunkLineWidth => _chunkLineWidth.Value;
    private static float ChunkLineLift => _chunkLineLift.Value;
    private static Color LineColor => _lineColor.Value;
    private static Color ChunkLineColor => _chunkLineColor.Value;
    private static float ChunkCentreLineWidth => _chunkCentreLineWidth.Value;
    private static Color ChunkCentreLineColor => _chunkCentreLineColor.Value;

    public static void BindConfig(ConfigFile config)
    {
        const string section = "Grid Appearance";

        _radius = config.Bind(section, "Radius", 8f,
            new ConfigDescription("Half-extent in metres of the drawn grid around the aim point.",
                new AcceptableValueRange<float>(2f, 32f)));
        _fadeStart = config.Bind(section, "Fade Start", 4f,
            new ConfigDescription("Distance in metres from the grid centre where lines start to fade out (kept below Radius).",
                new AcceptableValueRange<float>(0f, 32f)));
        _rebuildDistance = config.Bind(section, "Rebuild Distance", 2f,
            new ConfigDescription("How far in metres the aim point can move from the grid centre before the grid is redrawn.",
                new AcceptableValueRange<float>(0.25f, 16f)));
        _lift = config.Bind(section, "Lift", 0.12f,
            new ConfigDescription("Height in metres above the surface, keeps lines clear of grass and z-fighting.",
                new AcceptableValueRange<float>(0f, 1f)));
        _segmentLength = config.Bind(section, "Segment Length", 0.5f,
            new ConfigDescription("Line subdivision in metres so lines can follow terrain. Smaller is smoother but heavier.",
                new AcceptableValueRange<float>(0.1f, 4f)));
        _gridLineWidth = config.Bind(section, "Line Width", 0.06f,
            new ConfigDescription("Width in metres of the grid lines at 2m intervals.",
                new AcceptableValueRange<float>(0.01f, 1f)));
        _minorLineScale = config.Bind(section, "Minor Line Width Scale", 0.5f,
            new ConfigDescription("Width of the grid lines between the 2m lines, as a fraction of Line Width. Set to 1 to draw every line the same thickness.",
                new AcceptableValueRange<float>(0.1f, 1f)));
        _chunkLineWidth = config.Bind(section, "Chunk Line Width", 0.2f,
            new ConfigDescription("Width in metres of the thick lines drawn on world chunk (zone) borders.",
                new AcceptableValueRange<float>(0.01f, 2f)));
        _chunkLineLift = config.Bind(section, "Chunk Line Lift", 0.13f,
            new ConfigDescription("Height in metres above the surface of chunk border lines; keep slightly above Lift so they draw on top.",
                new AcceptableValueRange<float>(0f, 1f)));
        _chunkCentreLineWidth = config.Bind(section, "Chunk Centre Line Width", 0.12f,
            new ConfigDescription("Width in metres of the lines drawn through the centre of each world chunk (zone).",
                new AcceptableValueRange<float>(0.01f, 2f)));
        _chunkCentreLineColor = config.Bind(section, "Chunk Centre Line Color", new Color(0.1f, 1f, 0.4f, 0.75f),
            "Colour (including alpha) of the lines through the centre of each world chunk.");
        _lineColor = config.Bind(section, "Line Color", new Color(1f, 1f, 1f, 0.3f),
            "Colour (including alpha) of the regular grid lines.");
        _chunkLineColor = config.Bind(section, "Chunk Line Color", new Color(1f, 0.1f, 0.1f, 0.75f),
            "Colour (including alpha) of the world chunk border lines.");
    }

    private GameObject _object;
    private Mesh _mesh;
    private bool _hasMesh;
    private bool _warned;

    private float _builtSize;
    /// <summary>
    /// grid centre in plane coordinates (a, b)
    /// </summary>
    private Vector2 _builtCenter;
    private GridPlane _builtPlane;
    private float _builtSign;
    private float _builtNormalValue;

    /// <summary>
    /// `aim` is the grid-snapped aim point. `sign` is the direction the surface faces along its
    /// normal axis, so the grid is lifted off the surface towards the viewer.
    /// </summary>
    public void Show(Vector3 aim, float size, GridPlane plane, float sign)
    {
        if (!EnsureObject())
        {
            return;
        }

        Vector2 ab = ToPlane(plane, aim);
        float normalValue = NormalCoord(plane, aim);
        Vector2 center = new(Mathf.Round(ab.x / size) * size, Mathf.Round(ab.y / size) * size);

        bool stale = !_hasMesh
            || !Mathf.Approximately(size, _builtSize)
            || plane != _builtPlane
            || sign != _builtSign
            || (plane != GridPlane.Terrain && Mathf.Abs(normalValue - _builtNormalValue) > 0.01f)
            || (ab - _builtCenter).magnitude > RebuildDistance;

        if (stale)
        {
            Rebuild(center, size, plane, sign, normalValue);
        }

        _object.SetActive(true);
    }

    public void Hide()
    {
        if (_object != null && _object.activeSelf)
        {
            _object.SetActive(false);
        }
    }

    public void Destroy()
    {
        if (_object != null)
        {
            Object.Destroy(_object);
        }

        if (_mesh != null)
        {
            Object.Destroy(_mesh);
        }

        _object = null;
        _mesh = null;
        _hasMesh = false;
    }

    private bool EnsureObject()
    {
        if (_object != null)
        {
            return true;
        }

        Shader shader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
            ?? Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            if (!_warned)
            {
                Debug.LogWarning("BetterHammer: no usable shader found for the build grid overlay.");
            }

            _warned = true;
            return false;
        }

        _mesh = new Mesh { name = "BetterHammer_Grid", indexFormat = IndexFormat.UInt32 };
        _mesh.MarkDynamic();

        _object = new GameObject("BetterHammer_Grid");
        Object.DontDestroyOnLoad(_object);
        _object.AddComponent<MeshFilter>().sharedMesh = _mesh;

        MeshRenderer renderer = _object.AddComponent<MeshRenderer>();
        Material material = new(shader);
        // Submesh 0 holds the grid line ribbons, submesh 1 the thick chunk border ribbons.
        renderer.sharedMaterials = [material, material];
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        _object.SetActive(false);
        return true;
    }

    /// <summary>
    /// Plane coordinates: (a, b) are the two in-plane axes, n is the axis along the normal.
    /// Terrain / Top / Bottom: a = x, b = z, n = y
    /// WallX:                  a = z, b = y, n = x
    /// WallZ:                  a = x, b = y, n = z
    /// </summary>
    private static Vector2 ToPlane(GridPlane plane, Vector3 p)
    {
        return plane switch
        {
            GridPlane.WallX => new Vector2(p.z, p.y),
            GridPlane.WallZ => new Vector2(p.x, p.y),
            _ => new Vector2(p.x, p.z),
        };
    }

    private static float NormalCoord(GridPlane plane, Vector3 p)
    {
        return plane switch
        {
            GridPlane.WallX => p.x,
            GridPlane.WallZ => p.z,
            _ => p.y,
        };
    }

    private static Vector3 ToWorld(GridPlane plane, float a, float b, float n)
    {
        return plane switch
        {
            GridPlane.WallX => new Vector3(n, b, a),
            GridPlane.WallZ => new Vector3(a, b, n),
            _ => new Vector3(a, n, b),
        };
    }

    private void Rebuild(Vector2 center, float size, GridPlane plane, float sign, float normalValue)
    {
        int half = Mathf.FloorToInt(Radius / size);
        int segments = Mathf.CeilToInt(2f * Radius / SegmentLength);
        float step = 2f * Radius / segments;

        List<Vector3> vertices = [];
        List<Color> colors = [];
        List<int> indices = [];
        List<int> chunkIndices = [];

        for (int i = -half; i <= half; i++)
        {
            float line = i * size;
            AddRibbon(plane, sign, normalValue, 0, center.x + line, center.y, line, LineWidthAt(center.x + line), Lift, LineColor, segments, step, vertices, colors, indices);
            AddRibbon(plane, sign, normalValue, 1, center.y + line, center.x, line, LineWidthAt(center.y + line), Lift, LineColor, segments, step, vertices, colors, indices);
        }

        AddChunkLines(plane, sign, normalValue, center, segments, step, vertices, colors, chunkIndices);

        _mesh.Clear();
        _mesh.subMeshCount = 2;
        _mesh.SetVertices(vertices);
        _mesh.SetColors(colors);
        _mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        _mesh.SetIndices(chunkIndices, MeshTopology.Triangles, 1);
        _mesh.RecalculateBounds();

        _hasMesh = true;
        _builtSize = size;
        _builtCenter = center;
        _builtPlane = plane;
        _builtSign = sign;
        _builtNormalValue = normalValue;
    }

    /// <summary>
    /// Lines on a world coordinate that is a multiple of MajorInterval get the full width, the lines in between are thinner.
    /// </summary>
    private const float MajorInterval = 2f;

    private static float LineWidthAt(float coordinate)
    {
        float fromMajor = Mathf.Abs(coordinate - (Mathf.Round(coordinate / MajorInterval) * MajorInterval));
        return fromMajor < 0.001f ? GridLineWidth : MinorLineWidth;
    }

    /// <summary>
    /// Draws a thick ribbon along every world chunk (zone) border within the grid's radius.
    /// Zone borders sit half a zone size from the zone centres, i.e. at 32 + 64k. Chunks only
    /// divide x and z, so a wall plane has borders along its horizontal axis only.
    /// </summary>
    private static void AddChunkLines(GridPlane plane, float sign, float normalValue, Vector2 center, int segments, float step,
        List<Vector3> vertices, List<Color> colors, List<int> indices)
    {
        const float zoneSize = ZoneSystem.c_ZoneSize;
        const float halfZone = zoneSize / 2f;

        bool horizontalPlane = plane is GridPlane.Terrain or GridPlane.Top or GridPlane.Bottom;
        int axes = horizontalPlane ? 2 : 1;

        for (int axis = 0; axis < axes; axis++)
        {
            float centerAlong = axis == 0 ? center.x : center.y;
            float centerAcross = axis == 0 ? center.y : center.x;

            AddZoneLines(plane, sign, normalValue, axis, halfZone, centerAlong, centerAcross, ChunkLineWidth, ChunkLineColor,
                segments, step, vertices, colors, indices);
            AddZoneLines(plane, sign, normalValue, axis, 0f, centerAlong, centerAcross, ChunkCentreLineWidth, ChunkCentreLineColor,
                segments, step, vertices, colors, indices);
        }
    }

    /// <summary>
    /// Adds ribbons at every `offset + k * zoneSize` along the given axis that fall within the grid radius.
    /// </summary>
    private static void AddZoneLines(GridPlane plane, float sign, float normalValue, int axis, float offset, float centerAlong,
        float centerAcross, float width, Color color, int segments, float step,
        List<Vector3> vertices, List<Color> colors, List<int> indices)
    {
        const float zoneSize = ZoneSystem.c_ZoneSize;

        int first = Mathf.CeilToInt((centerAlong - Radius - offset) / zoneSize);
        int last = Mathf.FloorToInt((centerAlong + Radius - offset) / zoneSize);

        for (int k = first; k <= last; k++)
        {
            float line = (k * zoneSize) + offset;
            AddRibbon(plane, sign, normalValue, axis, line, centerAcross, line - centerAlong, width, ChunkLineLift,
                color, segments, step, vertices, colors, indices);
        }
    }

    /// <summary>
    /// Adds a flat ribbon running along the other in-plane axis at `line` (an `a` coordinate for
    /// axis 0, `b` for axis 1), spanning the grid radius around `centerAcross`. `lineOffset` is
    /// the line's distance from the grid centre, used for the distance fade.
    /// </summary>
    private static void AddRibbon(GridPlane plane, float sign, float normalValue, int axis, float line, float centerAcross,
        float lineOffset, float width, float lift, Color baseColor, int segments, float step,
        List<Vector3> vertices, List<Color> colors, List<int> indices)
    {
        float halfWidth = width / 2f;
        int start = vertices.Count;

        for (int s = 0; s <= segments; s++)
        {
            float t = -Radius + (s * step);
            float dist = Mathf.Sqrt((lineOffset * lineOffset) + (t * t));
            float fade = Mathf.Clamp01(1f - ((dist - FadeStart) / (Radius - FadeStart)));
            Color color = new(baseColor.r, baseColor.g, baseColor.b, baseColor.a * fade);

            for (int side = -1; side <= 1; side += 2)
            {
                float along = line + (side * halfWidth);
                float across = centerAcross + t;
                float a = axis == 0 ? along : across;
                float b = axis == 0 ? across : along;
                float n = plane == GridPlane.Terrain ? GroundHeight(a, b) : normalValue;
                vertices.Add(ToWorld(plane, a, b, n + (sign * lift)));
                colors.Add(color);
            }

            if (s > 0)
            {
                int i = start + ((s - 1) * 2);
                indices.Add(i); indices.Add(i + 1); indices.Add(i + 2);
                indices.Add(i + 1); indices.Add(i + 3); indices.Add(i + 2);
            }
        }
    }

    private static float GroundHeight(float x, float z)
    {
        return ZoneSystem.instance == null ? 0f : ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0f, z));
    }
}
