using UnityEngine;

/// <summary>
/// Клановая война (klan-voina-14): огонь и дым на повреждённых зданиях карты войны.
/// Чем меньше здоровья, тем сильнее: до 70% - дымок, до 45% - пламя, до 20% - пожар с искрами; разрушенное - тлеет.
/// Своя деревня тоже горит (огонь над настоящим зданием). Вне экрана огонь не считается.
/// </summary>
public class WarFire : MonoBehaviour
{
    const int Flames = 4, Puffs = 6, Embers = 5;

    float w = 2f, h = 2f;
    int level;                      // 0 ничего, 1 дым, 2 огонь, 3 пожар, 4 тлеет (разрушено)
    SpriteRenderer[] flame, puff, ember;
    Vector2[] flameAt;
    float[] puffT, emberT;
    Vector2[] puffFrom, emberFrom;
    float seed, visTimer;
    bool visible = true;

    public static WarFire On(GameObject host, float w, float h)
    {
        var go = new GameObject("Fire");
        go.transform.SetParent(host.transform, false);
        var f = go.AddComponent<WarFire>();
        f.w = Mathf.Max(1f, w);
        f.h = Mathf.Max(1f, h);
        f.Build();
        return f;
    }

    SpriteRenderer Make(string n, Sprite s, int order)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = s != null ? s : CombatFx.Circle();
        r.sortingOrder = order;
        r.enabled = false;
        return r;
    }

    void Build()
    {
        seed = Random.Range(0f, 100f);
        flame = new SpriteRenderer[Flames];
        flameAt = new Vector2[Flames];
        for (int i = 0; i < Flames; i++)
        {
            flame[i] = Make("Flame", GameArt.FxGlow(), 52 + (i & 1));   // поверх ночного затемнения (50): огонь светится в темноте
            flameAt[i] = new Vector2(Random.Range(-0.36f, 0.36f) * w, Random.Range(-0.1f, 0.3f) * h);
        }
        puff = new SpriteRenderer[Puffs];
        puffT = new float[Puffs];
        puffFrom = new Vector2[Puffs];
        for (int i = 0; i < Puffs; i++) { puff[i] = Make("Smoke", GameArt.FxPuff(), 24); puffT[i] = Random.Range(0f, 1f); NewPuff(i); }
        ember = new SpriteRenderer[Embers];
        emberT = new float[Embers];
        emberFrom = new Vector2[Embers];
        for (int i = 0; i < Embers; i++) { ember[i] = Make("Ember", GameArt.FxSpark(), 54); emberT[i] = Random.Range(0f, 1f); NewEmber(i); }
    }

    void NewPuff(int i) { puffFrom[i] = new Vector2(Random.Range(-0.3f, 0.3f) * w, Random.Range(0f, 0.3f) * h); }
    void NewEmber(int i) { emberFrom[i] = new Vector2(Random.Range(-0.35f, 0.35f) * w, Random.Range(-0.05f, 0.25f) * h); }

    /// <summary>Здоровье здания, % (0 - разрушено).</summary>
    public void SetHp(int pct)
    {
        int l = pct <= 0 ? 4 : pct < 20 ? 3 : pct < 45 ? 2 : pct < 70 ? 1 : 0;
        if (l == level) return;
        level = l;
        Apply();
    }

    void Apply()
    {
        bool fire = level == 2 || level == 3;
        int flames = level == 3 ? Flames : level == 2 ? 2 : 0;
        for (int i = 0; i < Flames; i++) flame[i].enabled = visible && fire && i < flames;
        int puffs = level == 0 ? 0 : level == 1 ? 3 : level == 4 ? 3 : Puffs;
        for (int i = 0; i < Puffs; i++) puff[i].enabled = visible && i < puffs;
        int embers = level == 3 ? Embers : level == 2 ? 2 : 0;
        for (int i = 0; i < Embers; i++) ember[i].enabled = visible && i < embers;
    }

    void Update()
    {
        if (level == 0) return;
        visTimer -= Time.unscaledDeltaTime;
        if (visTimer <= 0f)
        {
            visTimer = 0.4f;
            Camera cam = Camera.main;
            bool v = true;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(transform.position);
                float m = 0.15f + Mathf.Max(w, h) / Mathf.Max(1f, cam.orthographicSize * 2f);
                v = vp.x > -m && vp.x < 1f + m && vp.y > -m && vp.y < 1f + m && cam.orthographicSize < 60f;
            }
            if (v != visible) { visible = v; Apply(); }
        }
        if (!visible) return;

        float t = Time.time + seed, dt = Time.deltaTime;
        bool fire = level == 2 || level == 3;
        float size = Mathf.Clamp(Mathf.Min(w, h) * 0.55f, 0.6f, 2.2f) * (level == 3 ? 1.25f : 1f);
        for (int i = 0; i < Flames; i++)
        {
            if (!flame[i].enabled) continue;
            float n = Mathf.PerlinNoise(t * 3.1f + i * 7.3f, i * 1.7f);
            float s = size * (0.75f + 0.5f * n);
            flame[i].transform.localPosition = new Vector3(flameAt[i].x, flameAt[i].y + n * 0.18f * h, 0f);
            flame[i].transform.localScale = new Vector3(s * 0.8f, s * (1.05f + 0.35f * n), 1f);
            flame[i].color = Color.Lerp(new Color(1f, 0.32f, 0.06f, 0.75f), new Color(1f, 0.82f, 0.3f, 0.85f), n);
        }
        float rise = h * 0.9f + 1.2f;
        for (int i = 0; i < Puffs; i++)
        {
            if (!puff[i].enabled) continue;
            puffT[i] += dt / (fire ? 2.2f : 3.2f);
            if (puffT[i] >= 1f) { puffT[i] -= 1f; NewPuff(i); }
            float k = puffT[i];
            Vector2 p = puffFrom[i] + new Vector2(Mathf.Sin(k * 3f + i) * 0.25f + k * 0.6f, k * rise);
            puff[i].transform.localPosition = new Vector3(p.x, p.y, 0f);
            float s = Mathf.Lerp(0.5f, 1.5f, k) * Mathf.Clamp(Mathf.Min(w, h) * 0.45f, 0.6f, 1.8f);
            puff[i].transform.localScale = new Vector3(s, s, 1f);
            float g = level == 1 ? 0.55f : level == 4 ? 0.3f : 0.16f;
            puff[i].color = new Color(g, g, g * 1.05f, Mathf.Sin(k * Mathf.PI) * (level == 1 ? 0.35f : 0.55f));
        }
        for (int i = 0; i < Embers; i++)
        {
            if (!ember[i].enabled) continue;
            emberT[i] += dt / 1.3f;
            if (emberT[i] >= 1f) { emberT[i] -= 1f; NewEmber(i); }
            float k = emberT[i];
            Vector2 p = emberFrom[i] + new Vector2(Mathf.Sin(k * 9f + i * 2f) * 0.2f, k * (h * 0.7f + 1f));
            ember[i].transform.localPosition = new Vector3(p.x, p.y, 0f);
            float s = 0.16f * (1f - k * 0.6f);
            ember[i].transform.localScale = new Vector3(s, s, 1f);
            ember[i].color = new Color(1f, 0.6f + 0.3f * Mathf.Sin(t * 20f + i), 0.2f, 1f - k);
        }
    }
}

/// <summary>klan-voina-14: флаг стороны над захваченным источником (синий - наши, красный - враг). Колышется.</summary>
public class WarFlag : MonoBehaviour
{
    Sprite[] blue, red;
    SpriteRenderer sr, pole;
    int owner = -1, mySide;
    float t;

    public static WarFlag On(GameObject host, Vector2 local, float size)
    {
        var go = new GameObject("Flag");
        go.transform.SetParent(host.transform, false);
        go.transform.localPosition = new Vector3(local.x, local.y, 0f);
        var f = go.AddComponent<WarFlag>();
        f.blue = Frames("b");
        f.red = Frames("r");
        var p = new GameObject("Pole");
        p.transform.SetParent(go.transform, false);
        f.pole = p.AddComponent<SpriteRenderer>();
        f.pole.sprite = CombatFx.Square();
        f.pole.color = new Color(0.32f, 0.22f, 0.12f);
        f.pole.sortingOrder = 4;
        p.transform.localPosition = new Vector3(0f, -size * 0.35f, 0f);
        p.transform.localScale = new Vector3(0.07f * size, size * 0.95f, 1f);
        var fl = new GameObject("Cloth");
        fl.transform.SetParent(go.transform, false);
        fl.transform.localPosition = new Vector3(0.03f * size, size * 0.02f, 0f);
        fl.transform.localScale = new Vector3(size * 0.9f, size * 0.9f, 1f);
        f.sr = fl.AddComponent<SpriteRenderer>();
        f.sr.sortingOrder = 5;
        f.Set(0, 1);
        return f;
    }

    static Sprite[] Frames(string col)
    {
        var fr = new Sprite[4];
        for (int i = 0; i < 4; i++) fr[i] = GameArt.FxFlag(col, i + 1);
        return fr[0] != null ? fr : null;
    }

    /// <summary>Чей источник: 0 - ничей (флага нет).</summary>
    public void Set(int who, int mine)
    {
        if (who == owner && mine == mySide) return;
        owner = who;
        mySide = mine;
        bool show = who != 0;
        sr.enabled = pole.enabled = show;
        if (!show) return;
        Sprite[] fr = who == mine ? blue : red;
        if (fr != null) { sr.sprite = fr[0]; sr.color = Color.white; }
        else
        {
            // Нет картинки флага - цветной флажок
            sr.sprite = CombatFx.Square();
            sr.color = who == mine ? new Color(0.25f, 0.5f, 1f) : new Color(0.95f, 0.22f, 0.18f);
            sr.transform.localScale = new Vector3(0.5f, 0.3f, 1f);
        }
        if (who != mine) CombatFx.Ring(transform.position, 1.2f, new Color(1f, 0.3f, 0.25f, 0.8f));
        else CombatFx.Ring(transform.position, 1.2f, new Color(0.4f, 0.7f, 1f, 0.8f));
    }

    void Update()
    {
        if (owner <= 0 || sr == null || !sr.enabled) return;
        Sprite[] fr = owner == mySide ? blue : red;
        if (fr == null) return;
        t += Time.deltaTime * 6f;
        Sprite s = fr[(int)t % 4];
        if (s != null && sr.sprite != s) sr.sprite = s;
    }
}
