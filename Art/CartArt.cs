using UnityEngine;

/// <summary>Нарисованная повозка с лошадью (смотрит вправо; разворачивает её сама повозка масштабом).</summary>
public static class CartArt
{
    public static SpriteRenderer Add(Transform gfx, Color tint)
    {
        SpriteRenderer sr = Make(gfx, "Cart", GameArt.Cart(), 12);
        sr.transform.localPosition = new Vector3(GameArt.CartShift, 0.1f, 0f);
        // Тент слегка подкрашен (у купцов - в цвет гильдии), но сильно не красим
        sr.color = Color.Lerp(Color.white, tint, 0.25f);
        return sr;
    }

    /// <summary>Ещё одна лошадь впереди упряжки (i = 1, 2, ...).</summary>
    public static void AddHorse(Transform gfx, int i)
    {
        SpriteRenderer sr = Make(gfx, "Horse" + i, GameArt.Horse(), 12);
        sr.transform.localPosition = new Vector3(0.62f + 0.42f * i, 0.07f, 0f);
        sr.transform.localScale = new Vector3(0.42f, 0.42f, 1f);
    }

    static SpriteRenderer Make(Transform parent, string n, Sprite s, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(parent, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sortingOrder = order;
        return sr;
    }
}
