using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Иконки бойцов для меню (рынок, казарма): тот же человечек, что ходит по карте, - обводка, одежда цвета вида войск
/// и детали; конница - верхом на коне.
///  - Draw(r, unit) - нарисовать иконку вида войск в прямоугольнике;
///  - Id(unit) / Draw(r, id) - строковый id «unit:Имя», чтобы иконки бойцов шли через ItemIcons.Of/Lead/Draw как обычные вещи.
/// Картинок нет (GameArt выключен) - ничего не рисуется.
/// </summary>
public static class UnitIcons
{
    public const string Prefix = "unit:";

    // Виды войск, для которых уже выдали id: наёмник с рынка может быть из казармы, которой в городе нет
    static readonly Dictionary<string, UnitType> known = new Dictionary<string, UnitType>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { known.Clear(); }

    public static string Id(UnitType u)
    {
        if (u == null || string.IsNullOrEmpty(u.unitName)) return null;
        known[u.unitName] = u;
        return Prefix + u.unitName;
    }

    public static bool IsId(string id) { return id != null && id.StartsWith(Prefix); }

    public static UnitType FromId(string id)
    {
        if (!IsId(id)) return null;
        string name = id.Substring(Prefix.Length);
        if (known.TryGetValue(name, out UnitType u) && u != null) return u;
        u = BarracksController.FindType(name);
        if (u != null) known[name] = u;
        return u;
    }

    public static bool Has(UnitType u) { return u != null && GameArt.Cloth(GameArt.FigureKind(u)) != null; }
    public static bool Has(string id) { return Has(FromId(id)); }

    public static void Draw(Rect r, string id, int tier = -1) { Draw(r, FromId(id), tier); }

    /// <summary>Нарисовать бойца. tier: 1 - ★ усилен похлёбкой (золотая звёздочка в углу), иначе без метки.</summary>
    public static void Draw(Rect r, UnitType u, int tier = -1)
    {
        if (u == null || Event.current == null || Event.current.type != EventType.Repaint) return;
        string kind = GameArt.FigureKind(u);
        Sprite cloth = GameArt.Cloth(kind);
        if (cloth == null) return;
        Sprite outline = GameArt.Outline(kind);
        Sprite horse = u.requiresHorse ? GameArt.Horse() : null;
        Sprite detail = (horse != null ? GameArt.DetailRide(kind) : null) ?? GameArt.Detail(kind);

        // Человечек - квадрат 128, конь 170×128 той же плотности. Верхом: конь чуть крупнее (×1.05) и ниже на 0.36 роста,
        // как на карте (GameArt.PlaceHorse). Вписываем всю картинку в r, сохраняя пропорции.
        float figW = 1f, figH = 1f, horseW = 0f, horseH = 0f, horseDy = 0f;
        float boxW = figW, boxH = figH, figX = 0f, figY = 0f;
        if (horse != null)
        {
            horseH = 1.05f;
            horseW = horseH * horse.rect.width / Mathf.Max(1f, horse.rect.height);
            horseDy = 0.36f;
            boxW = Mathf.Max(figW, horseW);
            boxH = horseDy + horseH * 0.5f + figH * 0.5f;   // от макушки всадника до копыт
            figX = (boxW - figW) * 0.5f;
        }
        float k = Mathf.Min(r.width / boxW, r.height / boxH);
        float ox = r.x + (r.width - boxW * k) * 0.5f, oy = r.y + (r.height - boxH * k) * 0.5f;
        Rect fig = new Rect(ox + figX * k, oy + figY * k, figW * k, figH * k);

        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, old.a);
        if (horse != null)
        {
            float cy = figY + figH * 0.5f + horseDy;
            Rect hr = new Rect(ox + (boxW - horseW) * 0.5f * k, oy + (cy - horseH * 0.5f) * k, horseW * k, horseH * k);
            Tex(hr, horse);
        }
        if (outline != null) Tex(fig, outline);
        Color c = u.color;
        GUI.color = new Color(c.r, c.g, c.b, old.a);
        Tex(fig, cloth);
        GUI.color = new Color(1f, 1f, 1f, old.a);
        if (detail != null) Tex(fig, detail);
        if (u.IsRam) RamLog(fig, old.a);

        if (tier >= 1)
        {
            float s = Mathf.Max(8f, r.width * 0.32f);
            var st = new GUIStyle(UiKit.Small) { fontSize = Mathf.RoundToInt(s), alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            st.normal.textColor = new Color(1f, 0.8f, 0.25f);
            GUI.Label(new Rect(r.xMax - s, r.y - 1f, s, s), "★", st);
        }
        GUI.color = old;
    }

    // Таран: бревно с железной головой поперёк человечка
    static void RamLog(Rect fig, float alpha)
    {
        Sprite art = GameArt.Ram();
        if (art != null)
        {
            // Нарисованный таран: в длину почти во всю иконку, на уровне пояса
            float w = fig.width * 1.15f, ah = w * art.rect.height / Mathf.Max(1f, art.rect.width);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Tex(new Rect(fig.center.x - w * 0.5f, fig.y + fig.height * 0.52f - ah * 0.5f, w, ah), art);
            return;
        }
        float h = fig.height * 0.16f;
        Rect log = new Rect(fig.x - fig.width * 0.05f, fig.y + fig.height * 0.5f, fig.width * 0.9f, h);
        GUI.color = new Color(0.45f, 0.3f, 0.15f, alpha);
        GUI.DrawTexture(log, Texture2D.whiteTexture);
        GUI.color = new Color(0.25f, 0.25f, 0.28f, alpha);
        GUI.DrawTexture(new Rect(log.xMax, log.y - h * 0.2f, fig.width * 0.18f, h * 1.4f), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, alpha);
    }

    static void Tex(Rect r, Sprite s)
    {
        Texture2D t = s.texture;
        Rect tr = s.textureRect;
        GUI.DrawTextureWithTexCoords(r, t, new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height), true);
    }

    /// <summary>Иконка слева от строки (как ItemIcons.Lead): рисует и возвращает, насколько сдвинуть текст.</summary>
    public static float Lead(Rect row, UnitType u, int tier = -1)
    {
        if (!Has(u)) return 0f;
        float s = Mathf.Min(row.height, 30f);
        Draw(new Rect(row.x, row.y + (row.height - s) * 0.5f, s, s), u, tier);
        return s + 4f;
    }
}
