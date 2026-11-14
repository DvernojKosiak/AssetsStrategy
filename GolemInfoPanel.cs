using UnityEngine;

/// <summary>
/// Финал: карточка голема Алтаря жизни дома. Голема выбирают нажатием, как бойца, но приказов он не слушает:
/// в карточке здоровье, что он делает, его умения (взрыв-лечение с Ур.2 алтаря, подъём павших с Ур.3) и кнопки камеры.
/// </summary>
public static class GolemInfoPanel
{
    public static bool Follow;

    public static Rect Draw(LifeGolem g, float x, float y, float w)
    {
        LifeAltar a = g != null ? g.Altar : null;
        string abil = LifeAltar.GolemAbilitiesText();
        string stats = a != null ? a.StatsLine : "";
        float abilH = UiKit.TextH(abil, w - 20f, UiKit.Small) + 4f;
        float h = UiKit.TitleH + 6f + UiKit.RowH + 24f + UiKit.RowH + UiKit.RowH + abilH + 6f + UiKit.BtnH + 8f;
        Rect panel = new Rect(x, y, w, h);
        UiKit.Window(panel, UiKit.Col(UiKit.GoodHex, LifeAltar.GolemName + " Алтаря жизни"));
        float cy = y + UiKit.TitleH + 6f;

        LocGUI.Label(new Rect(x + 10f, cy, w - 20f, UiKit.RowH), "Страж алтаря · приказов не слушает", UiKit.MutedStyle);
        cy += UiKit.RowH;

        float frac = g != null && g.MaxHp > 0f ? g.Hp / g.MaxHp : 0f;
        UiKit.Bar(new Rect(x + 10f, cy, w - 20f, 18f), frac, Color.Lerp(UiKit.Bad, UiKit.Good, frac),
            $"Здоровье {Mathf.CeilToInt(g != null ? g.Hp : 0f)}/{Mathf.CeilToInt(g != null ? g.MaxHp : 0f)}");
        cy += 24f;

        LocGUI.Label(new Rect(x + 10f, cy, w - 20f, UiKit.RowH), UiKit.Col(UiKit.MutedHex, "Сейчас: ") + (g != null ? g.ActivityText : "—"));
        cy += UiKit.RowH;
        LocGUI.Label(new Rect(x + 10f, cy, w - 20f, UiKit.RowH), UiKit.Col(UiKit.MutedHex, "Сила: ") + stats, UiKit.Small);
        cy += UiKit.RowH;
        LocGUI.Label(new Rect(x + 10f, cy, w - 20f, abilH), abil, UiKit.Small);
        cy += abilH + 6f;

        float bw = (w - 26f) / 2f;
        if (UiKit.Button(new Rect(x + 10f, cy, bw, UiKit.BtnH), "Показать", UiKit.Kind.Normal, "Навести камеру на голема") && CameraController.Instance != null && g != null)
            CameraController.Instance.FocusOn(g.Position);
        if (UiKit.Button(new Rect(x + 16f + bw, cy, bw, UiKit.BtnH), Follow ? "Следить ✓" : "Следить",
                Follow ? UiKit.Kind.Primary : UiKit.Kind.Normal, "Камера едет за големом"))
            Follow = !Follow;
        return new Rect(x, y, w + 5f, h);
    }

    // Зелёная стрелка над выбранным големом
    public static void DrawMarker(LifeGolem g)
    {
        if (g == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(g.Position + Vector2.up * 0.9f);
        if (sp.z < 0f) return;
        GUIStyle st = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        Rect rc = new Rect(UiKit.ToGui(sp).x - 15f, UiKit.ToGui(sp).y - 15f, 30f, 30f);
        st.normal.textColor = Color.black;
        LocGUI.Label(new Rect(rc.x + 1f, rc.y + 1f, rc.width, rc.height), "▼", st);
        st.normal.textColor = new Color(0.55f, 1f, 0.6f);
        LocGUI.Label(rc, "▼", st);
    }
}
