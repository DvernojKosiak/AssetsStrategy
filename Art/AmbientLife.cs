using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Живая природа вокруг города (только для красоты, на игру не влияет):
///  - по земле медленно ползут тени облаков;
///  - днём пролетают птицы (с тенью на земле), над травой порхают бабочки;
///  - ночью в лесу и у воды мерцают светлячки;
///  - деревья покачиваются на ветру, на воде вспыхивают блики.
/// Всё рисуется только около камеры. Выключается в ⚙ Настройки → Общие → «Облака, птицы и светлячки».
/// Создаётся сам, добавлять в сцену не нужно.
/// </summary>
public class AmbientLife : MonoBehaviour
{
    static AmbientLife instance;

    // Порядок рисования: тени облаков выше зданий, но ниже ночного затемнения (50) и тумана войны (60)
    const int CloudOrder = 45, BirdShadowOrder = 44, BirdOrder = 47, MothOrder = 25, FireflyOrder = 52, GlintOrder = -94;

    class Mover
    {
        public SpriteRenderer sr, shadow;
        public Vector2 pos, vel;
        public float t, life, phase, size;
    }

    readonly List<Mover> clouds = new List<Mover>();
    readonly List<Mover> birds = new List<Mover>();
    readonly List<Mover> moths = new List<Mover>();
    readonly List<Mover> flies = new List<Mover>();
    readonly List<Mover> glints = new List<Mover>();
    Transform root;
    Camera cam;
    Vector2 wind = new Vector2(0.35f, 0.12f);
    float birdTimer = 6f;

    // Деревья качаются
    readonly List<Transform> trees = new List<Transform>();
    readonly List<float> treePhase = new List<float>();
    float nextTreeScan;

    SpriteRenderer nightOverlay;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("AmbientLife");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AmbientLife>();
    }

    void Awake() { root = transform; }

    /// <summary>Насколько сейчас ночь: 0 - день, 1 - полная ночь (по затемнению GameClock).</summary>
    public static float Night
    {
        get
        {
            if (instance == null) return 0f;
            return instance.NightNow();
        }
    }

    float NightNow()
    {
        if (GameClock.Instance == null) return 0f;
        if (nightOverlay == null)
        {
            Transform t = GameClock.Instance.transform.Find("NightOverlay");
            if (t != null) nightOverlay = t.GetComponent<SpriteRenderer>();
            if (nightOverlay == null) return 0f;
        }
        return Mathf.Clamp01(nightOverlay.color.a / Mathf.Max(0.01f, GameClock.Instance.nightDarkness));
    }

    bool Visible()
    {
        if (!GameSettings.Ambient) return false;
        if (BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) return false;
        if (WarView.IsOpen) return false;
        return true;
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        bool on = cam != null && Visible();
        if (!on) { HideAll(); return; }

        float dt = Time.deltaTime;   // со скоростью игры: на паузе всё замирает
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        Vector2 c = cam.transform.position;
        Rect view = new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);
        float night = NightNow();

        // Ветер понемногу меняется
        float wa = Mathf.PerlinNoise(Time.time * 0.01f, 3.3f) * Mathf.PI * 0.6f - 0.2f;
        wind = new Vector2(Mathf.Cos(wa), Mathf.Sin(wa)) * 0.4f;

        Clouds(view, dt, night);
        Birds(view, dt, night);
        Moths(view, dt, night);
        Fireflies(view, dt, night);
        Glints(view, dt, night);
        SwayTrees(view);
    }

    void HideAll()
    {
        foreach (var l in new[] { clouds, birds, moths, flies, glints })
            foreach (var m in l)
            {
                if (m.sr != null && m.sr.enabled) m.sr.enabled = false;
                if (m.shadow != null && m.shadow.enabled) m.shadow.enabled = false;
            }
    }

    SpriteRenderer Make(string name, Sprite s, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = s;
        r.sortingOrder = order;
        return r;
    }

    static Vector2 RandomIn(Rect r, float margin)
    {
        return new Vector2(Random.Range(r.xMin - margin, r.xMax + margin), Random.Range(r.yMin - margin, r.yMax + margin));
    }

    static bool Outside(Rect r, Vector2 p, float margin)
    {
        return p.x < r.xMin - margin || p.x > r.xMax + margin || p.y < r.yMin - margin || p.y > r.yMax + margin;
    }

    // ---------- Облака ----------

    void Clouds(Rect view, float dt, float night)
    {
        Sprite s = GameArt.Get("c/fx_cloud", 256f / 14f);   // облако около 14 клеток
        if (s == null) return;
        float span = Mathf.Max(view.width, view.height);
        int want = Mathf.Clamp(Mathf.RoundToInt(span / 12f), 2, 7);
        while (clouds.Count < want)
        {
            var m = new Mover { sr = Make("CloudShadow", s, CloudOrder) };
            m.pos = RandomIn(view, 10f);
            m.size = Random.Range(0.8f, 1.6f);
            m.phase = Random.value * 10f;
            clouds.Add(m);
        }
        float margin = 14f * 1.6f;
        for (int i = 0; i < clouds.Count; i++)
        {
            var m = clouds[i];
            bool used = i < want;
            m.pos += wind * dt * (0.9f + 0.2f * Mathf.Sin(m.phase));
            if (Outside(view, m.pos, margin))
            {
                // Унесло за край - появляется с наветренной стороны
                m.pos = new Vector2(view.center.x - Mathf.Sign(wind.x) * (view.width * 0.5f + margin * 0.9f), Random.Range(view.yMin - 6f, view.yMax + 6f));
                m.size = Random.Range(0.8f, 1.6f);
            }
            m.sr.enabled = used;
            m.sr.transform.position = new Vector3(m.pos.x, m.pos.y, 0f);
            m.sr.transform.localScale = new Vector3(m.size * 1.3f, m.size, 1f);
            m.sr.color = new Color(0.02f, 0.05f, 0.08f, 0.13f * (1f - night * 0.85f));
        }
    }

    // ---------- Птицы ----------

    void Birds(Rect view, float dt, float night)
    {
        Sprite a = GameArt.Get("c/fx_bird_1", 48f / 0.55f), b = GameArt.Get("c/fx_bird_2", 48f / 0.55f);
        if (a == null || b == null) return;
        birdTimer -= dt;
        if (birdTimer <= 0f && night < 0.3f)
        {
            birdTimer = Random.Range(7f, 16f);
            // Стайка из 1-5 птиц пролетает через экран
            int n = Random.value < 0.35f ? 1 : Random.Range(3, 6);
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 start = view.center - dir * (Mathf.Max(view.width, view.height) * 0.6f + 3f) + new Vector2(-dir.y, dir.x) * Random.Range(-view.height * 0.3f, view.height * 0.3f);
            float speed = Random.Range(3.2f, 4.5f);
            for (int i = 0; i < n; i++)
            {
                Mover m = null;
                foreach (var q in birds) if (!q.sr.enabled) { m = q; break; }
                if (m == null)
                {
                    if (birds.Count >= 16) break;
                    m = new Mover { sr = Make("Bird", a, BirdOrder) };
                    m.shadow = Make("BirdShadow", a, BirdShadowOrder);
                    birds.Add(m);
                }
                // Клин: каждая следующая чуть сзади и сбоку
                Vector2 side = new Vector2(-dir.y, dir.x) * ((i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 0.6f);
                m.pos = start - dir * ((i + 1) / 2) * 0.7f + side;
                m.vel = dir * speed;
                m.t = 0f;
                m.life = (Mathf.Max(view.width, view.height) * 1.3f + 8f) / speed;
                m.phase = Random.value;
                m.sr.enabled = m.shadow.enabled = true;
            }
        }
        foreach (var m in birds)
        {
            if (!m.sr.enabled) { if (m.shadow.enabled) m.shadow.enabled = false; continue; }
            m.t += dt;
            if (m.t > m.life) { m.sr.enabled = m.shadow.enabled = false; continue; }
            // Машет крыльями, иногда парит
            float flap = Mathf.Repeat(m.t * 3.2f + m.phase, 1f);
            bool glide = Mathf.Repeat(m.t * 0.35f + m.phase, 1f) > 0.6f;
            Sprite fr = glide || flap < 0.5f ? a : b;
            m.sr.sprite = fr; m.shadow.sprite = fr;
            m.pos += (m.vel + new Vector2(-m.vel.y, m.vel.x) * Mathf.Sin(m.t * 1.3f + m.phase * 6f) * 0.06f) * dt;
            float ang = Mathf.Atan2(m.vel.y, m.vel.x) * Mathf.Rad2Deg;
            m.sr.transform.SetPositionAndRotation(new Vector3(m.pos.x, m.pos.y, 0f), Quaternion.Euler(0f, 0f, ang));
            m.shadow.transform.SetPositionAndRotation(new Vector3(m.pos.x + 1.1f, m.pos.y - 1.4f, 0f), Quaternion.Euler(0f, 0f, ang));
            m.shadow.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            float fade = Mathf.Clamp01(m.t * 2f) * Mathf.Clamp01((m.life - m.t) * 2f);
            m.sr.color = new Color(1f, 1f, 1f, fade);
            m.shadow.color = new Color(0f, 0f, 0f, 0.18f * fade);
        }
    }

    // ---------- Бабочки ----------

    static readonly Color[] MothColors =
    {
        new Color(1f, 1f, 1f), new Color(1f, 0.86f, 0.32f), new Color(0.62f, 0.78f, 1f), new Color(1f, 0.62f, 0.32f)
    };

    void Moths(Rect view, float dt, float night)
    {
        Sprite a = GameArt.Get("c/fx_moth_1", 24f / 0.22f), b = GameArt.Get("c/fx_moth_2", 24f / 0.22f);
        if (a == null || b == null) return;
        int want = night > 0.4f || view.width > 70f ? 0 : 6;
        while (moths.Count < 6)
        {
            var m = new Mover { sr = Make("Butterfly", a, MothOrder) };
            m.pos = RandomIn(view, 0f);
            m.phase = Random.value * 10f;
            m.sr.color = MothColors[moths.Count % MothColors.Length];
            moths.Add(m);
        }
        for (int i = 0; i < moths.Count; i++)
        {
            var m = moths[i];
            bool used = i < want;
            if (m.sr.enabled != used) m.sr.enabled = used;
            if (!used) continue;
            m.t += dt;
            if (Outside(view, m.pos, 2f)) m.pos = RandomIn(view, 0f);
            // Порхает зигзагом
            float k = m.t * 0.7f + m.phase;
            Vector2 v = new Vector2(Mathf.PerlinNoise(k, m.phase) - 0.5f, Mathf.PerlinNoise(m.phase, k) - 0.5f) * 2.4f;
            m.pos += v * dt;
            bool open = Mathf.Repeat(m.t * 7f + m.phase, 1f) < 0.5f;
            m.sr.sprite = open ? a : b;
            m.sr.transform.position = new Vector3(m.pos.x, m.pos.y + Mathf.Sin(m.t * 5f + m.phase) * 0.06f, 0f);
            m.sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);
        }
    }

    // ---------- Светлячки ----------

    void Fireflies(Rect view, float dt, float night)
    {
        Sprite s = GameArt.Get("c/fx_glow", 64f / 0.35f);
        if (s == null) return;
        int want = night > 0.15f ? Mathf.Clamp(Mathf.RoundToInt(view.width * view.height / 40f), 8, 40) : 0;
        while (flies.Count < want)
        {
            var m = new Mover { sr = Make("Firefly", s, FireflyOrder) };
            m.phase = Random.value * 20f;
            m.pos = SpotForFirefly(view);
            flies.Add(m);
        }
        for (int i = 0; i < flies.Count; i++)
        {
            var m = flies[i];
            bool used = i < want;
            if (m.sr.enabled != used) m.sr.enabled = used;
            if (!used) continue;
            m.t += dt;
            if (Outside(view, m.pos, 1f)) m.pos = SpotForFirefly(view);
            float k = m.t * 0.4f + m.phase;
            m.pos += new Vector2(Mathf.PerlinNoise(k, 1.7f) - 0.5f, Mathf.PerlinNoise(2.9f, k) - 0.5f) * 0.9f * dt;
            m.sr.transform.position = new Vector3(m.pos.x, m.pos.y, 0f);
            float blink = Mathf.Clamp01(Mathf.Sin(m.t * 1.7f + m.phase * 3f) * 1.4f);
            m.sr.color = new Color(0.85f, 1f, 0.45f, blink * night * 0.9f);
        }
    }

    Vector2 SpotForFirefly(Rect view)
    {
        // Чаще у леса и у воды
        for (int i = 0; i < 12; i++)
        {
            Vector2 p = RandomIn(view, 0f);
            Vector2Int cell = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
            if (ResourceZones.IsForest(cell) || Rivers.NearWater(p, 2f)) return p;
        }
        return RandomIn(view, 0f);
    }

    // ---------- Блики на воде ----------

    void Glints(Rect view, float dt, float night)
    {
        Sprite s = GameArt.Get("c/fx_spark", 48f / 0.3f);
        if (s == null || !Rivers.Any) return;
        int want = view.width > 80f ? 0 : 14;
        while (glints.Count < want)
        {
            var m = new Mover { sr = Make("WaterGlint", s, GlintOrder) };
            m.sr.enabled = false;
            m.t = Random.Range(0f, 2f);
            glints.Add(m);
        }
        for (int i = 0; i < glints.Count; i++)
        {
            var m = glints[i];
            if (i >= want) { if (m.sr.enabled) m.sr.enabled = false; continue; }
            m.t -= dt;
            if (m.t <= 0f)
            {
                // Новое место на воде рядом с камерой (если воды не видно - ждём)
                m.sr.enabled = false;
                m.t = Random.Range(0.4f, 1.6f);
                for (int tryN = 0; tryN < 8; tryN++)
                {
                    Vector2 p = RandomIn(view, 0f);
                    if (!Rivers.IsWaterAt(p)) continue;
                    m.pos = p;
                    m.life = Random.Range(0.6f, 1.1f);
                    m.t = m.life;
                    m.sr.enabled = true;
                    break;
                }
                continue;
            }
            if (!m.sr.enabled) continue;
            float k = 1f - m.t / m.life;
            float a = Mathf.Sin(k * Mathf.PI);
            m.sr.transform.position = new Vector3(m.pos.x + k * 0.15f, m.pos.y, 0f);
            float sc = 0.6f + a * 0.6f;
            m.sr.transform.localScale = new Vector3(sc, sc, 1f);
            m.sr.color = new Color(1f, 1f, 1f, a * (0.75f - night * 0.5f));
        }
    }

    // ---------- Деревья на ветру ----------

    void SwayTrees(Rect view)
    {
        if (Time.unscaledTime >= nextTreeScan)
        {
            nextTreeScan = Time.unscaledTime + 8f;
            trees.Clear(); treePhase.Clear();
            if (ResourceZones.Instance != null)
                foreach (SpriteRenderer sr in ResourceZones.Instance.GetComponentsInChildren<SpriteRenderer>(false))
                {
                    if (sr.sprite == null) continue;
                    string n = sr.sprite.name;
                    if (!n.StartsWith("n/tree") && !n.StartsWith("n/pine")) continue;
                    trees.Add(sr.transform);
                    treePhase.Add(sr.transform.position.x * 0.37f + sr.transform.position.y * 0.21f);
                }
        }
        float tm = Time.time;
        float gust = 0.6f + 0.4f * Mathf.PerlinNoise(tm * 0.15f, 0.5f);
        Rect near = new Rect(view.x - 2f, view.y - 2f, view.width + 4f, view.height + 4f);
        for (int i = 0; i < trees.Count; i++)
        {
            Transform t = trees[i];
            if (t == null) continue;
            Vector3 p = t.position;
            if (!near.Contains(new Vector2(p.x, p.y))) continue;
            // Волна ветра идёт по лесу: соседние деревья качаются почти вместе
            float a = Mathf.Sin(tm * 1.3f - treePhase[i]) * 1.6f * gust + Mathf.Sin(tm * 3.1f + treePhase[i] * 2f) * 0.4f;
            t.localRotation = Quaternion.Euler(0f, 0f, a);
        }
    }
}
