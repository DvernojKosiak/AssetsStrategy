using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Метка над только что построенным зданием (final-8: тестеры не замечали, где появилось новое здание).
/// Золотая прыгающая стрелка «Готово!» и мягкое кольцо вокруг здания держатся Seconds секунд
/// или пока здание не выберут. Только картинка поверх карты: на игру не влияет.
/// </summary>
public class FreshMarkers : MonoBehaviour
{
    const float Seconds = 25f;
    static FreshMarkers inst;
    readonly Dictionary<BuildingInstance, float> fresh = new Dictionary<BuildingInstance, float>();
    readonly List<BuildingInstance> drop = new List<BuildingInstance>();
    static GUIStyle arrow, label;

    public static void Mark(BuildingInstance b)
    {
        if (b == null) return;
        if (inst == null)
        {
            var go = new GameObject("FreshMarkers");
            inst = go.AddComponent<FreshMarkers>();
        }
        inst.fresh[b] = Time.unscaledTime + Seconds;
    }

    void OnDestroy() { if (inst == this) inst = null; }

    void OnGUI()
    {
        if (fresh.Count == 0 || Event.current.type != EventType.Repaint) return;
        if (RaidManager.ViewingBattle || WarView.IsOpen) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        UiKit.Apply();
        if (arrow == null)
        {
            arrow = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            label = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }
        drop.Clear();
        BuildingInstance sel = BuildingPlacer.Selected;
        float now = Time.unscaledTime;
        foreach (var kv in fresh)
        {
            BuildingInstance b = kv.Key;
            if (b == null || now > kv.Value || b == sel || !b.isActiveAndEnabled) { drop.Add(b); continue; }
            float top = b.type != null ? b.type.size.y * 0.5f + 0.3f : 1.2f;
            Vector3 sp = cam.WorldToScreenPoint((Vector3)b.Center + Vector3.up * top);
            if (sp.z < 0f) continue;
            Vector2 gp = UiKit.ToGui(sp);
            float bob = Mathf.Abs(Mathf.Sin(now * 4f)) * 8f;
            float fade = Mathf.Clamp01((kv.Value - now) / 3f);
            Color old = GUI.color;
            Rect ar = new Rect(gp.x - 18f, gp.y - 34f - bob, 36f, 30f);
            GUI.color = new Color(0f, 0f, 0f, 0.7f * fade);
            GUI.Label(new Rect(ar.x + 1.5f, ar.y + 1.5f, ar.width, ar.height), "▼", arrow);
            GUI.color = new Color(1f, 0.85f, 0.3f, fade);
            GUI.Label(ar, "▼", arrow);
            Rect lr = new Rect(gp.x - 60f, ar.y - 18f, 120f, 18f);
            GUI.color = new Color(0f, 0f, 0f, 0.7f * fade);
            GUI.Label(new Rect(lr.x + 1f, lr.y + 1f, lr.width, lr.height), Loc.T("Готово!"), label);
            GUI.color = new Color(1f, 0.92f, 0.55f, fade);
            GUI.Label(lr, Loc.T("Готово!"), label);
            GUI.color = old;
        }
        foreach (var b in drop) fresh.Remove(b);
    }
}
