using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Маршруты своих бойцов: от каждого бойца, которого игрок куда-то послал, бежит пунктир к цели,
/// а на цели стоит полупрозрачный маркер. Рисуется только у того, кто отдал приказ: маршрут не уходит
/// ни на сервер, ни в снимки живого боя, поэтому защитник не видит, куда идёт нападающий, и наоборот.
/// Откуда берутся маршруты (каждый кадр заново):
///  - дома и в военном городе: бойцы казарм с приказом «Послать...» и бойцы, ушедшие в поход к краю карты
///    (CitizenAgent.RouteGoal);
///  - в набеге: наши налётчики - идти к точке, ломать здание, отступать; в живом бою - по нашим приказам
///    (RaidUnit.RouteGoal);
///  - карта клановой войны: наши бойцы, которым мы дали приказ (WarView.AddRoutes).
/// Пунктир «бежит» к цели. Толщина растёт вместе с отдалением камеры, чтобы на большой карте было видно.
/// </summary>
public class RouteMarks : MonoBehaviour
{
    static RouteMarks instance;

    // Поверх ночного затемнения (50) и тумана войны (60): это наш приказ, его должно быть видно всегда
    const int LineOrder = 62, GoalOrder = 61;
    const int MaxDashes = 1600, MaxGoals = 64;
    const float Dash = 0.32f, Gap = 0.22f, Thick = 0.07f, GoalRadius = 0.42f, Speed = 1.1f;

    public static readonly Color MoveColor = new Color(0.62f, 0.9f, 1f);
    public static readonly Color AttackColor = new Color(1f, 0.45f, 0.35f);
    public static readonly Color RetreatColor = new Color(1f, 0.85f, 0.35f);

    /// <summary>Показывать маршруты (можно выключить, например на время записи видео).</summary>
    public static bool Enabled = true;

    struct Line { public Vector2 a, b; public Color c; }
    struct Goal { public Vector2 p; public Color c; public float r; }

    static readonly List<Line> lines = new List<Line>();
    static readonly List<Goal> goals = new List<Goal>();

    readonly List<SpriteRenderer> dashes = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> discs = new List<SpriteRenderer>();
    readonly List<SpriteRenderer> rings = new List<SpriteRenderer>();
    static Sprite ringSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; lines.Clear(); goals.Clear(); ringSprite = null; Enabled = true; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("RouteMarks");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RouteMarks>();
    }

    /// <summary>
    /// Боец идёт из from в to: пунктир и маркер цели. goalRadius - размер маркера (у здания - побольше).
    /// Звать можно из любого Update этого кадра; RouteMarks сам собирает маршруты бойцов в LateUpdate.
    /// </summary>
    public static void Add(Vector2 from, Vector2 to, Color c, float goalRadius = GoalRadius)
    {
        lines.Add(new Line { a = from, b = to, c = c });
        // Один маркер на одну цель: отряд из 20 бойцов идёт в одну точку - кружок один
        float merge = Mathf.Max(0.5f, goalRadius);
        for (int i = 0; i < goals.Count; i++)
            if (goals[i].c == c && (goals[i].p - to).sqrMagnitude < merge * merge) return;
        if (goals.Count < MaxGoals) goals.Add(new Goal { p = to, c = c, r = goalRadius });
    }

    void LateUpdate()
    {
        if (Enabled) Collect();
        Camera cam = Camera.main;
        int usedDash = 0, usedGoal = 0;
        if (Enabled && cam != null && cam.orthographic)
        {
            // Камеру отдалили - всё крупнее (иначе на карте войны пунктира не видно)
            float k = Mathf.Clamp(cam.orthographicSize / 7f, 0.7f, 8f);
            Vector2 cp = cam.transform.position;
            float hh = cam.orthographicSize + 2f * k, hw = hh * cam.aspect + 2f * k;
            Rect view = new Rect(cp.x - hw, cp.y - hh, hw * 2f, hh * 2f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);

            foreach (var g in goals)
            {
                if (usedGoal >= MaxGoals || !view.Contains(g.p)) continue;
                float r = Mathf.Max(g.r, GoalRadius * k * 0.8f);
                SpriteRenderer disc = Pooled(discs, usedGoal, "RouteGoal", CombatFx.Circle(), GoalOrder);
                disc.transform.position = new Vector3(g.p.x, g.p.y, 0f);
                disc.transform.localScale = new Vector3(r * 2f, r * 2f, 1f);
                disc.color = new Color(g.c.r, g.c.g, g.c.b, 0.22f);
                SpriteRenderer ring = Pooled(rings, usedGoal, "RouteRing", Ring(), GoalOrder);
                float rr = r * (1.05f + 0.15f * pulse);
                ring.transform.position = disc.transform.position;
                ring.transform.localScale = new Vector3(rr * 2f, rr * 2f, 1f);
                ring.color = new Color(g.c.r, g.c.g, g.c.b, 0.45f + 0.2f * pulse);
                usedGoal++;
            }

            float dash = Dash * k, gap = Gap * k, thick = Thick * k, period = dash + gap;
            float shift = Mathf.Repeat(Time.unscaledTime * Speed * k, period);
            foreach (var l in lines)
            {
                if (usedDash >= MaxDashes) break;
                Vector2 d = l.b - l.a;
                float len = d.magnitude;
                // Не от центра бойца и не до центра маркера: пунктир между ними
                float start = 0.3f * k, end = len - GoalRadius * 0.6f * k;
                if (end - start < period) continue;
                if (!SegmentNear(view, l.a, l.b)) continue;
                Vector2 dir = d / len;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                Quaternion rot = Quaternion.Euler(0f, 0f, angle);
                // Очень длинный путь - реже штрихи, чтобы не съесть весь запас
                float step = period * Mathf.Max(1f, (end - start) / period / 80f);
                Color col = new Color(l.c.r, l.c.g, l.c.b, 0.55f);
                for (float s = start + shift * (step / period); s < end && usedDash < MaxDashes; s += step)
                {
                    float a = s, b = Mathf.Min(s + dash, end);
                    if (b - a < 0.02f) continue;
                    Vector2 mid = l.a + dir * ((a + b) * 0.5f);
                    if (!view.Contains(mid)) continue;
                    SpriteRenderer sr = Pooled(dashes, usedDash, "RouteDash", CombatFx.Square(), LineOrder);
                    sr.transform.position = new Vector3(mid.x, mid.y, 0f);
                    sr.transform.rotation = rot;
                    sr.transform.localScale = new Vector3(b - a, thick, 1f);
                    sr.color = col;
                    usedDash++;
                }
            }
        }
        Hide(dashes, usedDash);
        Hide(discs, usedGoal);
        Hide(rings, usedGoal);
        lines.Clear();
        goals.Clear();
    }

    // Маршруты бойцов игрока
    static void Collect()
    {
        foreach (var c in CitizenAgent.All)
            if (c != null && c.RouteGoal(out Vector2 to))
                Add(c.transform.position, to, MoveColor);

        foreach (var u in RaidUnit.All)
        {
            if (u == null) continue;
            int kind = u.RouteGoal(out Vector2 to, out float r);
            if (kind > 0) Add(u.Position, to, kind == 2 ? AttackColor : kind == 3 ? RetreatColor : MoveColor, r);
        }

        if (WarView.Instance != null) WarView.Instance.AddRoutes();
    }

    static bool SegmentNear(Rect r, Vector2 a, Vector2 b)
    {
        Rect box = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        return box.Overlaps(r);
    }

    SpriteRenderer Pooled(List<SpriteRenderer> pool, int i, string n, Sprite sprite, int order)
    {
        if (i < pool.Count)
        {
            if (!pool[i].enabled) pool[i].enabled = true;
            return pool[i];
        }
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        pool.Add(sr);
        return sr;
    }

    static void Hide(List<SpriteRenderer> pool, int from)
    {
        for (int i = from; i < pool.Count; i++)
            if (pool[i] != null && pool[i].enabled) pool[i].enabled = false;
    }

    // Кольцо диаметром 1 (обводка маркера)
    static Sprite Ring()
    {
        if (ringSprite != null) return ringSprite;
        const int n = 64;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - d) * 16f) * Mathf.Clamp01((d - 0.82f) * 16f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return ringSprite;
    }
}
