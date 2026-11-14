using UnityEngine;

/// <summary>
/// klan-voina-14: экран загрузки при переходе на клановую войну - горящая ночная деревня, красное зарево,
/// летящие искры, огненная полоска и советы про войну. Рисует его StartupScreen (тот же экран загрузки),
/// когда игра входит в военную деревню (WarCityMode.Active). Домой - обычный экран.
/// Картинка: Resources/Startup/loading_war.jpg (нет её - обычный фон, но с огнём поверх).
/// </summary>
public static class WarLoading
{
    /// <summary>Сейчас грузится военная деревня.</summary>
    public static bool On => WarCityMode.Active;

    static Texture2D bg, ember, glow, fill, heat;
    static bool loaded;

    const int Sparks = 70;
    static readonly Vector4[] spark = new Vector4[Sparks];   // x, y (0..1 от экрана), скорость, размер
    static float lastT = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { lastT = -1f; }

    static void Load()
    {
        if (loaded && ember != null) return;
        loaded = true;
        bg = Resources.Load<Texture2D>("Startup/loading_war");

        ember = new Texture2D(16, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(7.5f, 7.5f)) / 7.5f;
                float a = Mathf.Clamp01(1f - d);
                ember.SetPixel(x, y, new Color(1f, 0.75f + 0.25f * a, 0.4f + 0.5f * a * a, a * a));
            }
        ember.Apply();

        // Зарево снизу: красное, кверху тает
        glow = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int i = 0; i < 64; i++)
        {
            float v = i / 63f;   // 0 - низ
            glow.SetPixel(0, i, new Color(1f, 0.28f + 0.2f * (1f - v), 0.05f, Mathf.Pow(1f - v, 2.2f) * 0.75f));
        }
        glow.Apply();

        // Полоска: раскалённая - тёмно-красная снизу, жёлтая сверху
        fill = new Texture2D(1, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int i = 0; i < 16; i++)
        {
            float v = i / 15f;
            fill.SetPixel(0, i, Color.Lerp(new Color(0.55f, 0.06f, 0.02f), new Color(1f, 0.72f, 0.25f), v));
        }
        fill.Apply();

        // Дрожащий жар по краям
        heat = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                heat.SetPixel(x, y, new Color(0.6f, 0.03f, 0.01f, Mathf.Clamp01((d - 0.4f) / 0.6f) * 0.85f));
            }
        heat.Apply();

        for (int i = 0; i < Sparks; i++) spark[i] = NewSpark(true);
    }

    static Vector4 NewSpark(bool anywhere)
    {
        return new Vector4(Random.value, anywhere ? Random.value : 1.02f + Random.value * 0.1f, Random.Range(0.05f, 0.16f), Random.Range(1.5f, 3.6f));
    }

    /// <summary>Фон экрана загрузки на войне (вместо деревни днём).</summary>
    public static Texture2D Background(Texture2D normal) { Load(); return bg != null ? bg : normal; }

    /// <summary>Полоска загрузки - огненная.</summary>
    public static Texture2D BarFill(Texture2D normal) { Load(); return fill != null ? fill : normal; }

    /// <summary>Огонь поверх фона: дрожащее зарево снизу, красные края, летящие вверх искры. W, H - весь экран.</summary>
    public static void DrawFire(float W, float H, float alpha)
    {
        Load();
        if (Event.current.type != EventType.Repaint || alpha <= 0f) return;
        float t = Time.unscaledTime;
        float dt = lastT < 0f ? 0f : Mathf.Min(0.1f, t - lastT);
        lastT = t;
        Color old = GUI.color;

        float flick = 0.75f + 0.25f * Mathf.PerlinNoise(t * 2.3f, 0.4f);
        GUI.color = new Color(1f, 1f, 1f, alpha * flick);
        GUI.DrawTexture(new Rect(0f, H * 0.45f, W, H * 0.55f), glow, ScaleMode.StretchToFill, true);
        GUI.color = new Color(1f, 1f, 1f, alpha * (0.55f + 0.3f * Mathf.PerlinNoise(t * 1.3f, 3.1f)));
        GUI.DrawTexture(new Rect(0f, 0f, W, H), heat, ScaleMode.StretchToFill, true);

        for (int i = 0; i < Sparks; i++)
        {
            Vector4 s = spark[i];
            s.y -= s.z * dt;
            s.x += Mathf.Sin(t * 1.7f + i * 0.9f) * 0.012f * dt + 0.01f * dt;
            if (s.y < -0.05f || s.x > 1.05f) s = NewSpark(false);
            spark[i] = s;
            float life = Mathf.Clamp01(s.y * 1.3f);
            float tw = 0.6f + 0.4f * Mathf.Sin(t * 9f + i * 2.1f);
            GUI.color = new Color(1f, 1f, 1f, alpha * life * tw);
            float sz = s.w * (UiKit.Phone ? 1.2f : 1f);
            GUI.DrawTexture(new Rect(s.x * W - sz / 2f, s.y * H - sz / 2f, sz, sz), ember, ScaleMode.StretchToFill, true);
        }
        GUI.color = old;
    }

    public const string Subtitle = "Клановая война  ·  деревня готовится к бою";
    public const string Status = "Трубят сбор…";

    public static readonly string[] Tips =
    {
        "Бойцы казарм сами выходят на карту и остаются бойцами своей казармы",
        "Подведите бойца к его казарме — он зайдёт в неё, и там ему сменят оружие и броню",
        "«Позвать к казарме» в окне казармы собирает её бойцов с карты",
        "Захваченные рудники, лесопилки и деревни приносят доход прямо в военную деревню",
        "Стойте на участке минуту без врагов — и он станет вашим",
        "Со 2-го дня можно дарить ресурсы соседям по клану: «Клану» → «Помочь своим»",
        "Враги снесли здание на карте — в деревне оно тоже сломано. Почините его досками и камнем",
        "На 5-й день открывается вся карта: собирайте отряды заранее",
        "Счёт войны: занятые участки и по 2 очка за каждую уцелевшую деревню",
    };
}
