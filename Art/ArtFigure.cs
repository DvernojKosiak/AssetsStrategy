using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Нарисованный человечек на месте цветного квадратика. Висит на туловище (SpriteRenderer, который красит игра):
/// детали и тень повторяют за туловищем видимость и прозрачность.
/// Анимация: на ходу ноги шагают (кадры деталей) и человечек покачивается, стоя - чуть дышит, при ударе - выпад вперёд.
/// </summary>
public class ArtFigure : MonoBehaviour
{
    SpriteRenderer body, detail, shadow, outline;
    public string Kind { get; private set; }

    /// <summary>Конь под всадником (его ноги тоже шагают).</summary>
    public SpriteRenderer Horse;

    Sprite[] frames = new Sprite[3];   // 0 - стоит, 1 и 2 - шаг
    Sprite ride;                       // сидит в седле
    Sprite sil, silRide;               // обводка: стоя / верхом вместе с конём
    int outlineOrder;
    Vector3 baseLocal;
    Vector3 lastPos;
    bool started;
    float moving, walkT, lungeT, seed;
    int lastStep = -1;

    bool mounted;
    /// <summary>Верхом: тень уходит под коня и становится шире.</summary>
    public bool Mounted
    {
        get { return mounted; }
        set
        {
            if (mounted == value && shadow != null) return;
            mounted = value;
            if (shadow == null) return;
            shadow.transform.localScale = mounted ? new Vector3(1.4f, 1f, 1f) : new Vector3(0.8f, 0.8f, 1f);
        }
    }

    public void Setup(SpriteRenderer b, SpriteRenderer o)
    {
        body = b;
        baseLocal = body.transform.localPosition;
        seed = Random.value * 10f;
        if (detail == null) detail = MakeChild("Detail", body.sortingOrder + 1);
        if (shadow == null)
        {
            shadow = MakeChild("Shadow", body.sortingOrder - 2);
            shadow.sprite = GameArt.Shadow();
            mounted = !mounted;
            Mounted = !mounted;
        }
        if (o != null && outline == null)
        {
            outline = o;
            outlineOrder = o.sortingOrder;
            outline.transform.localPosition = body.transform.localPosition;
            outline.transform.localScale = body.transform.localScale;
        }
    }

    SpriteRenderer MakeChild(string n, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(body.transform, false);
        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sortingOrder = order;
        return r;
    }

    public void SetKind(string kind)
    {
        Sprite cloth = GameArt.Cloth(kind);
        if (cloth == null || body == null) return;
        Kind = kind;
        body.sprite = cloth;
        frames[0] = GameArt.Detail(kind);
        frames[1] = GameArt.DetailStep(kind, 1) ?? frames[0];
        frames[2] = GameArt.DetailStep(kind, 2) ?? frames[0];
        ride = GameArt.DetailRide(kind);
        detail.sprite = frames[0];
        sil = GameArt.Outline(kind);
        silRide = GameArt.OutlineRide(kind);
        if (outline != null) outline.sprite = sil;
        Sync(detail, body.enabled, body.color.a);
    }

    /// <summary>Удар: короткий выпад в сторону, куда смотрит.</summary>
    public void Lunge() { lungeT = LungeTime; }
    const float LungeTime = 0.2f;

    void LateUpdate()
    {
        if (body == null) return;
        bool on = body.enabled;
        float a = body.color.a;
        Sync(detail, on, a);
        Sync(shadow, on, a * 0.8f);
        if (outline != null && outline.transform.localScale != body.transform.localScale)
            outline.transform.localScale = body.transform.localScale;
        Animate();
    }

    void Animate()
    {
        float dt = Time.deltaTime;
        Transform p = body.transform.parent;
        Vector3 pos = p != null ? p.position : body.transform.position;
        if (!started) { lastPos = pos; started = true; }
        float speed = dt > 0f ? (pos - lastPos).magnitude / dt : 0f;
        lastPos = pos;
        if (speed > 25f) speed = 0f;   // скачок (зашёл в здание) - не шаг

        moving = Mathf.MoveTowards(moving, speed > 0.12f ? 1f : 0f, dt * 8f);
        int frame = 0;
        if (moving > 0.5f)
        {
            walkT += dt * Mathf.Clamp(2.2f + speed * 1.2f, 2.5f, 6f);
            int step = (int)(walkT * 4f) % 4;          // шаг, ноги вместе, другой шаг, ноги вместе
            frame = step == 0 ? 1 : (step == 2 ? 2 : 0);
            // Бегом и верхом из-под ног летит пыль
            if (step != lastStep && (step == 0 || step == 2) && body.enabled && shadow != null)
            {
                bool rider = mounted && Horse != null && Horse.enabled;
                if (speed > 1.7f && (rider || Random.value < 0.35f))
                    FootDust.Spawn(shadow.transform.position, Mathf.Abs(body.transform.lossyScale.y) * (rider ? 0.9f : 0.55f), body.sortingOrder - 3);
            }
            lastStep = step;
        }
        // Верхом ноги не шагают: всадник сидит, шагает конь
        Sprite want = mounted && ride != null ? ride : frames[frame];
        if (detail.sprite != want && want != null) detail.sprite = want;
        RideOutline();
        if (Horse != null && Horse.enabled)
        {
            Sprite hs = GameArt.HorseStep(frame);
            if (hs != null && Horse.sprite != hs) Horse.sprite = hs;
        }

        float sc = Mathf.Abs(body.transform.localScale.y);
        float bob = moving * Mathf.Abs(Mathf.Sin(walkT * Mathf.PI * 2f)) * (mounted ? 0.04f : 0.07f) * sc
                    + (1f - moving) * Mathf.Sin(Time.time * 2.2f + seed) * 0.012f * sc;
        float lx = 0f;
        if (lungeT > 0f)
        {
            lungeT -= dt;
            float k = Mathf.Sin(Mathf.Clamp01(1f - lungeT / LungeTime) * Mathf.PI);
            lx = (body.flipX ? -1f : 1f) * k * 0.16f * sc;
        }
        Vector3 lp = baseLocal + new Vector3(lx, bob, 0f);
        if (body.transform.localPosition != lp) body.transform.localPosition = lp;
        if (outline != null) outline.transform.localPosition = lp;
        // тень остаётся на земле
        if (shadow != null)
        {
            float gy = (mounted ? -1f : -0.5f) - (sc > 0.001f ? bob / sc : 0f);
            shadow.transform.localPosition = new Vector3(-lx / Mathf.Max(0.001f, sc), gy, 0f);
        }
    }

    // Верхом обводка стоячего человечка ложилась поверх коня - казалось, что всадник стоит на земле.
    // Теперь у всадника своя обводка (он и конь вместе) и она уходит под коня.
    void RideOutline()
    {
        if (outline == null) return;
        bool riding = mounted && Horse != null && Horse.enabled && silRide != null;
        Sprite want = riding ? silRide : sil;
        if (want != null && outline.sprite != want) outline.sprite = want;
        int order = riding ? Mathf.Min(outlineOrder, Horse.sortingOrder - 1) : outlineOrder;
        if (outline.sortingOrder != order) outline.sortingOrder = order;
        if (shadow != null)
        {
            int so = riding ? Mathf.Min(body.sortingOrder - 2, order - 1) : body.sortingOrder - 2;
            if (shadow.sortingOrder != so) shadow.sortingOrder = so;
        }
    }

    static void Sync(SpriteRenderer r, bool on, float a)
    {
        if (r == null) return;
        if (r.enabled != on) r.enabled = on;
        Color c = r.color;
        if (Mathf.Abs(c.a - a) > 0.001f) { c.a = a; r.color = c; }
    }
}

/// <summary>Пыль из-под ног бегущих и коней: общий запас клубов на всю карту (не больше 40 сразу).</summary>
public class FootDust : MonoBehaviour
{
    static FootDust instance;
    const int Max = 40;

    class P { public SpriteRenderer sr; public Vector3 from; public float t, life, size; }
    readonly List<P> pool = new List<P>();

    public static void Spawn(Vector3 at, float size, int order)
    {
        if (!GameSettings.Ambient) return;
        if (instance == null)
        {
            var go = new GameObject("FootDust");
            instance = go.AddComponent<FootDust>();
        }
        instance.Add(at, size, order);
    }

    void Add(Vector3 at, float size, int order)
    {
        Sprite s = GameArt.FxPuff();
        if (s == null) return;
        P p = null;
        foreach (var q in pool) if (!q.sr.enabled) { p = q; break; }
        if (p == null)
        {
            if (pool.Count >= Max) return;
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            p = new P { sr = go.AddComponent<SpriteRenderer>() };
            p.sr.sprite = s;
            pool.Add(p);
        }
        p.from = at + new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(-0.04f, 0.04f), 0f);
        p.t = 0f;
        p.life = Random.Range(0.45f, 0.7f);
        p.size = size;
        p.sr.sortingOrder = order;
        p.sr.enabled = true;
        Update1(p);
    }

    void Update()
    {
        foreach (var p in pool)
        {
            if (!p.sr.enabled) continue;
            p.t += Time.deltaTime;
            if (p.t >= p.life) { p.sr.enabled = false; continue; }
            Update1(p);
        }
    }

    static void Update1(P p)
    {
        float k = p.t / p.life;
        p.sr.transform.position = p.from + new Vector3(0f, 0.12f * k * p.size, 0f);
        float sc = Mathf.Lerp(0.25f, 0.6f, k) * p.size;
        p.sr.transform.localScale = new Vector3(sc, sc * 0.7f, 1f);
        p.sr.color = new Color(0.82f, 0.75f, 0.6f, 0.45f * (1f - k));
    }
}
