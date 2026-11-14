using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Живые мелочи на рисунке здания: дым из труб, крылья мельницы, пила лесопилки.
/// Места берутся из Resources/Art/anim.txt (их записывает рисовалка вместе с картинками).
/// При смене картинки (улучшение) всё собирается заново. У руин ничего не движется.
/// Ещё:
///  - новое здание появляется со вспышкой, кольцом пыли и клубами по краям;
///  - при улучшении (картинка сменилась на следующий уровень) - вспышка и золотые искры;
///  - когда стройка или восстановление руин закончились (рисунок снова целиком яркий) - вспышка и пыль;
///  - ночью в жилых зданиях загорается тёплый свет в окнах (чуть мерцает);
///  - флажки на башнях, казармах, Цитадели и др. развеваются, у магических построек пульсирует свечение
///    (места - Resources/Art/anim_vizual.txt).
/// </summary>
public class ArtBuildingFx : MonoBehaviour
{
    SpriteRenderer host;
    Sprite builtFor;
    readonly List<Transform> spinners = new List<Transform>();
    readonly List<float> speeds = new List<float>();
    readonly List<SpriteRenderer> spinR = new List<SpriteRenderer>();
    readonly List<Vector3> smokeAt = new List<Vector3>();   // x, y, размер
    readonly List<float> smokeTimer = new List<float>();
    readonly List<Puff> puffs = new List<Puff>();
    readonly List<GameObject> made = new List<GameObject>();

    class Puff { public SpriteRenderer sr; public Vector3 from; public float t, life, size; }

    // Разовые эффекты (появление, улучшение): частицы в координатах здания
    class Bit { public SpriteRenderer sr; public Vector3 pos, vel; public float t, life, size0, size1, spin; public Color col; public bool rise; }
    readonly List<Bit> bits = new List<Bit>();

    // Флажки: кадры меняются по кругу, у каждого свой сдвиг по времени
    readonly List<SpriteRenderer> flags = new List<SpriteRenderer>();
    readonly List<Sprite[]> flagFrames = new List<Sprite[]>();
    readonly List<float> flagPhase = new List<float>();
    // Свечение магических построек
    readonly List<SpriteRenderer> pulses = new List<SpriteRenderer>();
    readonly List<Color> pulseColor = new List<Color>();

    SpriteRenderer windowGlow;
    float glowSeed;
    int lookState = -1;   // -1 - ещё не видели, 0 - стройка или руины (тёмный / полупрозрачный), 1 - целое здание

    public static void Attach(SpriteRenderer sr) { Attach(sr, false); }

    /// <summary>fresh - здание только что построено (показать появление).</summary>
    public static void Attach(SpriteRenderer sr, bool fresh)
    {
        if (sr == null || sr.sprite == null) return;
        ArtBuildingFx fx = sr.GetComponent<ArtBuildingFx>();
        if (fx == null) fx = sr.gameObject.AddComponent<ArtBuildingFx>();
        fx.host = sr;
        fx.Build();
        if (fresh) fx.Celebrate(false);
    }

    void Build()
    {
        foreach (var go in made) if (go != null) Destroy(go);
        made.Clear(); spinners.Clear(); speeds.Clear(); spinR.Clear(); smokeAt.Clear(); smokeTimer.Clear(); puffs.Clear();
        flags.Clear(); flagFrames.Clear(); flagPhase.Clear(); pulses.Clear(); pulseColor.Clear();
        windowGlow = null;
        builtFor = host.sprite;
        if (builtFor == null) return;

        foreach (var s in GameArt.AnimSpots(builtFor.name))
        {
            Vector3 lp = new Vector3(s.x / 128f, s.y / 128f, 0f);
            if (s.kind == "smoke")
            {
                smokeAt.Add(new Vector3(lp.x, lp.y, s.size));
                smokeTimer.Add(Random.Range(0f, 0.8f));
            }
            else if (s.kind == "flag_r" || s.kind == "flag_y")
            {
                string col = s.kind.Substring(5);
                var fr = new Sprite[4];
                for (int f = 0; f < 4; f++) fr[f] = GameArt.FxFlag(col, f + 1);
                if (fr[0] == null) continue;
                GameObject go = new GameObject("Flag");
                go.transform.SetParent(host.transform, false);
                go.transform.localPosition = lp;
                // Нарисованный флажок s.size точек в длину и ~10 в высоту: наш чуть больше и закрывает его
                float kx = s.size * 1.3f / 128f, ky = 14f / 128f / (20f / 64f);
                go.transform.localScale = new Vector3(kx, ky, 1f);
                SpriteRenderer r = go.AddComponent<SpriteRenderer>();
                r.sprite = fr[0];
                r.sortingOrder = host.sortingOrder + 2;
                made.Add(go);
                flags.Add(r); flagFrames.Add(fr); flagPhase.Add(Random.value * 4f);
            }
            else if (s.kind == "pulse_m" || s.kind == "pulse_g")
            {
                Sprite g = GameArt.FxGlow();
                if (g == null) continue;
                GameObject go = new GameObject("Pulse");
                go.transform.SetParent(host.transform, false);
                go.transform.localPosition = lp;
                float k = s.size / 128f;
                go.transform.localScale = new Vector3(k, k, 1f);
                SpriteRenderer r = go.AddComponent<SpriteRenderer>();
                r.sprite = g;
                r.sortingOrder = host.sortingOrder + 3;
                made.Add(go);
                pulses.Add(r);
                pulseColor.Add(s.kind == "pulse_m" ? new Color(0.75f, 0.45f, 1f) : new Color(0.5f, 1f, 0.6f));
            }
            else if (s.kind == "mill" || s.kind == "saw")
            {
                bool mill = s.kind == "mill";
                Sprite sp = mill ? GameArt.FxMill() : GameArt.FxSaw();
                if (sp == null) continue;
                GameObject go = new GameObject(mill ? "Mill" : "Saw");
                go.transform.SetParent(host.transform, false);
                go.transform.localPosition = lp;
                float k = mill ? s.size * 1.4f / 76f : s.size / 30f;
                go.transform.localScale = new Vector3(k, k, 1f);
                SpriteRenderer r = go.AddComponent<SpriteRenderer>();
                r.sprite = sp;
                r.sortingOrder = host.sortingOrder + 1;
                made.Add(go);
                spinners.Add(go.transform);
                spinR.Add(r);
                speeds.Add(mill ? -40f : -320f);
            }
        }
        BuildWindowGlow();
    }

    void LateUpdate()
    {
        if (host == null) return;
        if (host.sprite != builtFor)
        {
            bool up = IsUpgrade(builtFor, host.sprite);
            Build();
            if (up) Celebrate(true);
        }

        // руины тёмные - ничего не крутится и не дымит
        bool alive = host.enabled && host.gameObject.activeInHierarchy && host.color.r > 0.6f;
        float dt = Time.deltaTime;
        for (int i = 0; i < spinners.Count; i++)
        {
            if (spinR[i].enabled != host.enabled) spinR[i].enabled = host.enabled;
            if (alive) spinners[i].Rotate(0f, 0f, speeds[i] * dt);
        }

        for (int i = 0; i < smokeAt.Count; i++)
        {
            if (!alive) break;
            smokeTimer[i] -= dt;
            if (smokeTimer[i] > 0f) continue;
            smokeTimer[i] = Random.Range(0.55f, 0.9f);
            SpawnPuff(smokeAt[i]);
        }

        foreach (var p in puffs)
        {
            if (!p.sr.enabled) continue;
            p.t += dt;
            float k = p.t / p.life;
            if (k >= 1f) { p.sr.enabled = false; continue; }
            p.sr.transform.localPosition = p.from + new Vector3(0.18f * k + Mathf.Sin(p.t * 3f) * 0.03f, 0.55f * k, 0f);
            float sc = Mathf.Lerp(0.12f, 0.42f, k) * p.size;
            p.sr.transform.localScale = new Vector3(sc, sc, 1f);
            Color c = p.sr.color;
            c.a = Mathf.Lerp(0.75f, 0f, k) * (k < 0.15f ? k / 0.15f : 1f);
            p.sr.color = c;
        }

        // Флажки развеваются (8 кадров в секунду), магия мерцает; у руин всё замирает
        for (int i = 0; i < flags.Count; i++)
        {
            // У руин и стройки флажка нет (рисунок тёмный, а флажок был бы ярким)
            bool on = host.enabled && alive;
            if (flags[i].enabled != on) flags[i].enabled = on;
            if (!on) continue;
            int f = (int)((Time.time + flagPhase[i]) * 8f) & 3;
            if (flags[i].sprite != flagFrames[i][f]) flags[i].sprite = flagFrames[i][f];
        }
        for (int i = 0; i < pulses.Count; i++)
        {
            bool on = host.enabled && alive;
            if (pulses[i].enabled != on) pulses[i].enabled = on;
            if (!on) continue;
            Color c = pulseColor[i];
            c.a = 0.18f + 0.14f * Mathf.Sin(Time.time * 2.4f + i);
            pulses[i].color = c;
        }

        WatchFinished();
        UpdateWindowGlow(alive);
        UpdateBits(dt);
    }

    // Стройка и руины рисуются тёмными или полупрозрачными. Стал рисунок целиком ярким - здание готово
    void WatchFinished()
    {
        if (!host.enabled) return;
        Color c = host.color;
        int now = c.a > 0.995f && c.r > 0.95f && c.g > 0.95f && c.b > 0.95f ? 1 : 0;
        if (lookState == 0 && now == 1 && BuildingPlacer.Instance != null && BuildingPlacer.Instance.WorldReady) Celebrate(false);
        lookState = now;
    }

    void SpawnPuff(Vector3 at)
    {
        Puff p = null;
        foreach (var q in puffs) if (!q.sr.enabled) { p = q; break; }
        if (p == null)
        {
            if (puffs.Count >= 6 * Mathf.Max(1, smokeAt.Count)) return;
            Sprite sp = GameArt.FxPuff();
            if (sp == null) return;
            GameObject go = new GameObject("Smoke");
            go.transform.SetParent(host.transform, false);
            p = new Puff { sr = go.AddComponent<SpriteRenderer>() };
            p.sr.sprite = sp;
            p.sr.sortingOrder = host.sortingOrder + 6;
            made.Add(go);
            puffs.Add(p);
        }
        p.from = new Vector3(at.x, at.y, 0f);
        p.size = at.z;
        p.t = 0f;
        p.life = Random.Range(1.8f, 2.6f);
        p.sr.color = new Color(1f, 1f, 1f, 0f);
        p.sr.enabled = true;
    }

    // ---------- Улучшение: картинка того же здания, но следующего уровня ----------

    // «b/house_2» → house и 2; «b/wall_2_j5» → wall и 2 (концовка _j5 - соседи стены, не уровень)
    static bool ParseArt(string name, out string id, out int level)
    {
        id = null; level = 0;
        if (string.IsNullOrEmpty(name) || !name.StartsWith("b/")) return false;
        string[] parts = name.Substring(2).Split('_');
        for (int i = parts.Length - 1; i >= 1; i--)
            if (parts[i].Length == 1 && char.IsDigit(parts[i][0]))
            {
                level = parts[i][0] - '0';
                id = string.Join("_", parts, 0, i);
                return true;
            }
        return false;
    }

    static bool IsUpgrade(Sprite from, Sprite to)
    {
        if (from == null || to == null) return false;
        string a, b;
        int la, lb;
        if (!ParseArt(from.name, out a, out la) || !ParseArt(to.name, out b, out lb)) return false;
        return a == b && lb > la;
    }

    // ---------- Появление и улучшение ----------

    /// <summary>Вспышка, кольцо пыли, клубы по краям; при улучшении ещё и золотые искры.</summary>
    public void Celebrate(bool upgrade)
    {
        if (host == null || host.sprite == null || !GameSettings.Ambient) return;
        Vector3 size = host.sprite.bounds.size;
        Vector3 center = host.sprite.bounds.center;
        float w = Mathf.Max(size.x, size.y);
        int order = host.sortingOrder;

        // Вспышка: светлая копия рисунка тает и чуть раздувается
        Bit flash = NewBit(host.sprite, order + 7);
        flash.pos = Vector3.zero; flash.life = 0.45f; flash.size0 = 1f; flash.size1 = 1.07f;
        flash.col = upgrade ? new Color(1f, 0.92f, 0.6f, 0.7f) : new Color(1f, 1f, 1f, 0.6f);

        // Кольцо пыли у основания
        Sprite ring = GameArt.FxRing();
        if (ring != null)
        {
            Bit r = NewBit(ring, order - 1);
            r.pos = center; r.life = 0.7f; r.size0 = w * 0.6f; r.size1 = w * 1.5f;
            r.col = upgrade ? new Color(1f, 0.9f, 0.55f, 0.8f) : new Color(0.92f, 0.85f, 0.68f, 0.85f);
        }

        // Клубы пыли разлетаются от краёв
        Sprite puff = GameArt.FxPuff();
        if (puff != null && !upgrade)
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Bit b = NewBit(puff, order + 2);
                b.pos = center + new Vector3(dir.x * size.x * 0.45f, dir.y * size.y * 0.45f, 0f);
                b.vel = dir * Random.Range(0.5f, 0.9f);
                b.life = Random.Range(0.6f, 0.9f);
                b.size0 = 0.25f * w / 2f; b.size1 = 0.6f * w / 2f;
                b.col = new Color(0.86f, 0.8f, 0.66f, 0.8f);
            }

        // Искры: при улучшении - золотые и вверх, при постройке - немного белых
        Sprite spark = GameArt.FxSpark();
        if (spark != null)
        {
            int n = upgrade ? 18 : 6;
            for (int i = 0; i < n; i++)
            {
                Bit b = NewBit(spark, order + 8);
                b.pos = center + new Vector3(Random.Range(-0.45f, 0.45f) * size.x, Random.Range(-0.4f, 0.3f) * size.y, 0f);
                b.vel = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.6f, 1.3f), 0f);
                b.life = Random.Range(0.8f, 1.4f);
                float s0 = Random.Range(0.18f, 0.3f) * Mathf.Min(1.5f, w / 2f);
                b.size0 = s0; b.size1 = s0 * 0.3f;
                b.spin = Random.Range(-200f, 200f);
                b.rise = true;
                b.col = upgrade ? new Color(1f, Random.Range(0.78f, 0.92f), 0.35f, 1f) : new Color(1f, 1f, 0.92f, 0.9f);
            }
        }
    }

    Bit NewBit(Sprite s, int order)
    {
        Bit b = null;
        foreach (var q in bits) if (!q.sr.enabled) { b = q; break; }
        if (b == null)
        {
            GameObject go = new GameObject("Fx");
            go.transform.SetParent(host.transform, false);
            b = new Bit { sr = go.AddComponent<SpriteRenderer>() };
            bits.Add(b);
        }
        b.sr.sprite = s;
        b.sr.sortingOrder = order;
        b.sr.enabled = true;
        b.t = 0f; b.vel = Vector3.zero; b.spin = 0f; b.rise = false;
        b.sr.transform.localRotation = Quaternion.identity;
        return b;
    }

    void UpdateBits(float dt)
    {
        // Эффект короткий - пусть доигрывает и на паузе
        float udt = Mathf.Max(dt, Time.unscaledDeltaTime);
        foreach (var b in bits)
        {
            if (!b.sr.enabled) continue;
            b.t += udt;
            float k = b.t / Mathf.Max(0.01f, b.life);
            if (k >= 1f) { b.sr.enabled = false; continue; }
            b.pos += b.vel * udt;
            b.vel *= Mathf.Max(0f, 1f - udt * (b.rise ? 0.6f : 2.5f));
            b.sr.transform.localPosition = new Vector3(b.pos.x, b.pos.y, 0f);
            float ease = 1f - (1f - k) * (1f - k);
            float sc = Mathf.Lerp(b.size0, b.size1, ease);
            b.sr.transform.localScale = new Vector3(sc, sc, 1f);
            if (b.spin != 0f) b.sr.transform.Rotate(0f, 0f, b.spin * udt);
            Color c = b.col;
            float fadeIn = b.rise ? Mathf.Clamp01(k * 6f) : 1f;
            c.a *= (1f - k) * fadeIn * (b.rise ? 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(b.t * 14f)) : 1f);
            b.sr.color = c;
        }
    }

    // ---------- Свет в окнах ночью ----------

    void BuildWindowGlow()
    {
        if (!Lit(builtFor)) return;
        Sprite g = GameArt.FxGlow();
        if (g == null) return;
        GameObject go = new GameObject("WindowLight");
        go.transform.SetParent(host.transform, false);
        Bounds bb = builtFor.bounds;
        go.transform.localPosition = new Vector3(bb.center.x, bb.center.y - bb.size.y * 0.08f, 0f);
        go.transform.localScale = new Vector3(bb.size.x * 1.25f, bb.size.y * 1.05f, 1f);
        windowGlow = go.AddComponent<SpriteRenderer>();
        windowGlow.sprite = g;
        windowGlow.sortingOrder = 51;   // поверх ночного затемнения (50): светится в темноте
        windowGlow.color = new Color(1f, 0.72f, 0.36f, 0f);
        windowGlow.enabled = false;
        glowSeed = Random.value * 10f;
        made.Add(go);
    }

    // Свет есть в домах, где живут и работают; у стен, дорог, мостов и пушек - нет
    static bool Lit(Sprite s)
    {
        string id;
        int lv;
        if (s == null || !ParseArt(s.name, out id, out lv)) return false;
        switch (id)
        {
            case "wall": case "gate": case "road": case "cobble": case "bridge": case "barricade":
            case "cannon": case "magic_cannon": case "tower": case "farm": case "life_altar": case "outpost":
                return false;
        }
        return true;
    }

    void UpdateWindowGlow(bool alive)
    {
        if (windowGlow == null) return;
        float night = AmbientLife.Night;
        bool on = alive && night > 0.02f && GameSettings.Ambient;
        if (windowGlow.enabled != on) windowGlow.enabled = on;
        if (!on) return;
        float flicker = 0.88f + 0.12f * Mathf.PerlinNoise(Time.time * 2.2f, glowSeed);
        windowGlow.color = new Color(1f, 0.72f, 0.36f, 0.32f * night * flicker);
    }
}
