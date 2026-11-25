using System;
using System.Reflection;

using BepInEx.Bootstrap;

using UnityEngine;

namespace BetterHammer.Core;

/// <summary>
/// Paint from the PaintBrush mod, if it's installed: read a piece's paint, set it on a piece just built, and show
/// it on a ghost. PaintBrush's public PaintApi is found through its plugin when first needed, so BetterHammer
/// doesn't reference it, and every call does nothing without it. A paint is an opaque int; 0 means unpainted.
/// </summary>
internal static class PaintBridge
{
    internal const string PaintBrushGuid = "javidahmed64592.paintbrush";
    private const string ApiType = "PaintBrush.Api.PaintApi";

    private static bool _looked;
    private static Func<GameObject, int> _get;
    private static Action<GameObject, int> _set;
    private static Action<GameObject, int> _show;

    internal static bool Available
    {
        get
        {
            Find();
            return _get != null;
        }
    }

    internal static int GetPaint(GameObject piece)
    {
        return piece != null && Available ? _get(piece) : 0;
    }

    /// <summary>
    /// Paints a piece that was just built. A paint of 0 is skipped, as a new piece has none.
    /// </summary>
    internal static void SetPaint(GameObject piece, int paint)
    {
        if (paint != 0 && piece != null && Available)
        {
            _set(piece, paint);
        }
    }

    /// <summary>
    /// Shows the paint on a ghost or another object that isn't saved; 0 takes it off.
    /// </summary>
    internal static void ShowOnGhost(GameObject ghost, int paint)
    {
        if (ghost != null && Available)
        {
            _show(ghost, paint);
        }
    }

    private static void Find()
    {
        if (_looked)
        {
            return;
        }

        _looked = true;
        if (!Chainloader.PluginInfos.TryGetValue(PaintBrushGuid, out BepInEx.PluginInfo plugin) || plugin.Instance == null)
        {
            return;
        }

        Type api = plugin.Instance.GetType().Assembly.GetType(ApiType);
        MethodInfo get = api?.GetMethod("GetPaint", [typeof(GameObject)]);
        MethodInfo set = api?.GetMethod("SetPaint", [typeof(GameObject), typeof(int)]);
        MethodInfo show = api?.GetMethod("ShowOnGhost", [typeof(GameObject), typeof(int)]);
        if (get == null || set == null || show == null)
        {
            Debug.LogWarning("BetterHammer: PaintBrush is installed but its PaintApi wasn't found, so copies won't keep paint.");
            return;
        }

        _get = (Func<GameObject, int>)Delegate.CreateDelegate(typeof(Func<GameObject, int>), get);
        _set = (Action<GameObject, int>)Delegate.CreateDelegate(typeof(Action<GameObject, int>), set);
        _show = (Action<GameObject, int>)Delegate.CreateDelegate(typeof(Action<GameObject, int>), show);
    }
}
