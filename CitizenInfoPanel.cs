using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Английские имена жителей: имя + фамилия из списков. Повторы возможны, но редки.
/// </summary>
public static class CitizenNames
{
    static readonly string[] First =
    {
        "Adam", "Alice", "Arthur", "Bella", "Ben", "Clara", "Daniel", "Edith", "Edward", "Eleanor",
        "Emma", "Felix", "Florence", "George", "Grace", "Harold", "Harriet", "Henry", "Isaac", "Ivy",
        "Jack", "James", "Jane", "John", "Kate", "Leo", "Lily", "Lucy", "Mabel", "Martin",
        "Mary", "Matilda", "Nora", "Oliver", "Oscar", "Peter", "Rose", "Ruth", "Samuel", "Sarah",
        "Simon", "Sophie", "Thomas", "Victor", "Walter", "William", "Agnes", "Hugh", "Roger", "Joan"
    };

    static readonly string[] Last =
    {
        "Archer", "Baker", "Bell", "Brewer", "Brooks", "Butler", "Carter", "Clark", "Cooper", "Fisher",
        "Fletcher", "Ford", "Gardner", "Green", "Hall", "Hill", "Hunter", "King", "Knight", "Lane",
        "Mason", "Miller", "Moore", "Parker", "Potter", "Reed", "Shepherd", "Smith", "Stone", "Taylor",
        "Thatcher", "Turner", "Walker", "Ward", "Webb", "Weaver", "Wells", "White", "Wood", "Wright"
    };

    static readonly HashSet<string> used = new HashSet<string>();

    public static string Next()
    {
        // Несколько попыток найти ещё не занятое сочетание
        string n = "";
        for (int i = 0; i < 8; i++)
        {
            n = First[Random.Range(0, First.Length)] + " " + Last[Random.Range(0, Last.Length)];
            if (used.Add(n)) return n;
        }
        return n;
    }

    /// <summary>Имя из сохранения (обновление 12): больше никому не выдаётся.</summary>
    public static void Claim(string n) { if (!string.IsNullOrEmpty(n)) used.Add(n); }
}

/// <summary>
/// Карточка жителя: открывается кликом по жителю на карте (BuildingPlacer).
/// Только просмотр: имя, кто он, где работает и живёт, здоровье, чем занят, что несёт.
/// Кнопка "Следить" - камера едет за жителем.
/// </summary>
public static class CitizenInfoPanel
{
    public static bool Follow;   // камера следует за выбранным жителем

    // ---------- Прямое управление бойцом (final-8) ----------
    // Выбрали бойца - кнопка «Послать отряд» (на войне включена сразу): следующее нажатие по карте
    // отправляет весь его отряд туда (по врагу - туда, где враг: отряд нападёт). Нажатие по зданию или жителю - просто выбор.
    public static bool OrderArmed;

    public static bool Orderable(CitizenAgent a)
    {
        return a != null && !a.IsDead && a.IsSoldier && !a.IsOnExpedition && !a.IsDeparting
            && a.Squad != null && !a.Squad.OnExpedition && a.ExpBarracks != null;
    }

    static int SquadSize(CitizenAgent a)
    {
        int n = 0;
        if (a.Squad != null) foreach (var m in a.Squad.Members) if (m != null && !m.IsDead) n++;
        return n;
    }

    static string OrderHint(CitizenAgent a) =>
        $"Нажмите на карту — отряд ({SquadSize(a)} бойц.) пойдёт туда и ударит по врагам на пути. Двойное нажатие — марш без боя.";

    public static Rect Draw(CitizenAgent a, float x, float y, float w)
    {
        string carry = a.CarryText;
        string combat = a.CombatStatsText;
        string equip = a.EquipmentText;
        string cond = a.ConditionText;
        string stealth = a.StealthText;   // плащ из листвы / дозор
        if (!a.IsMilitary && a.Workplace != null)
            cond += "  " + UiKit.Col(UiKit.MutedHex, $"работает на {Mathf.RoundToInt(a.WorkFactor * 100f)}%");
        bool hasMore = combat != "" || equip != "";
        bool more = hasMore && UiKit.IsOpen("citizen-more");

        float h = UiKit.TitleH + 6f
                + UiKit.RowH                       // роль
                + 24f                              // здоровье (полоска)
                + KvH(w, a.ActivityText)
                + KvH(w, a.HomeText)
                + KvH(w, cond)
                + KvH(w, carry)
                + (stealth != "" ? KvH(w, stealth) : 0f)
                + (hasMore ? UiKit.FoldH + 2f : 0f)
                + (more && combat != "" ? KvH(w, combat) : 0f)
                + (more && equip != "" ? KvH(w, equip) : 0f)
                + 6f + UiKit.BtnH + 8f             // кнопки
                + (Orderable(a) ? UiKit.BtnH + 6f + (OrderArmed ? UiKit.TextH(OrderHint(a), w - 20f, UiKit.Small) + 4f : 0f) : 0f);
        Rect panel = new Rect(x, y, w, h);
        bool fav = CitizenFavorites.Is(a);
        UiKit.Window(panel, (fav ? "★ " : "") + a.CitizenName);
        float cy = y + UiKit.TitleH + 6f;

        // Обновление 12: звёздочка - в избранное (вкладка «Избранные» в меню города, метка над жителем)
        Rect star = new Rect(x + w - 128f, y + 4f, 92f, UiKit.TitleH - 8f);
        if (UiKit.Button(star, fav ? "★ Избранный" : "☆ В избранное", fav ? UiKit.Kind.Primary : UiKit.Kind.Ghost,
                fav ? "Убрать из избранного" : "Следить за жителем: звёздочка над ним, вкладка «Избранные» в меню города, вести о нём"))
            CitizenFavorites.Toggle(a);

        LocGUI.Label(new Rect(x + 10f, cy, w - 20f, UiKit.RowH), a.RoleText, UiKit.MutedStyle);
        cy += UiKit.RowH;

        // Здоровье
        float frac = a.HpFraction;
        UiKit.Bar(new Rect(x + 10f, cy, w - 20f, 18f), frac, Color.Lerp(UiKit.Bad, UiKit.Good, frac),
            $"Здоровье {Mathf.CeilToInt(a.Hp)}/{Mathf.CeilToInt(a.MaxHp)}");
        cy += 24f;

        Kv(x, ref cy, w, "Сейчас", a.ActivityText);
        Kv(x, ref cy, w, "Живёт", a.HomeText);
        Kv(x, ref cy, w, "Состояние", cond);
        Kv(x, ref cy, w, "Несёт", carry);
        if (stealth != "") Kv(x, ref cy, w, "Скрытность", stealth);

        if (hasMore)
        {
            UiKit.Fold(new Rect(x + 10f, cy, w - 20f, UiKit.FoldH), "citizen-more", "Бой и снаряжение");
            cy += UiKit.FoldH + 2f;
            if (more && combat != "") Kv(x, ref cy, w, "В бою", combat);
            if (more && equip != "") Kv(x, ref cy, w, "Снаряж.", equip);
        }

        // Кнопки (только камера - менять в жителе ничего нельзя)
        cy += 6f;
        float bw = (w - 26f) / 2f;
        if (UiKit.Button(new Rect(x + 10f, cy, bw, UiKit.BtnH), "Показать", UiKit.Kind.Normal, "Навести камеру на жителя") && CameraController.Instance != null)
            CameraController.Instance.FocusOn(a.transform.position);
        if (UiKit.Button(new Rect(x + 16f + bw, cy, bw, UiKit.BtnH), Follow ? "Следить ✓" : "Следить",
                Follow ? UiKit.Kind.Primary : UiKit.Kind.Normal, "Камера едет за жителем"))
            Follow = !Follow;

        if (Orderable(a))
        {
            cy += UiKit.BtnH + 6f;
            if (UiKit.Button(new Rect(x + 10f, cy, bw, UiKit.BtnH), OrderArmed ? "➜ Куда? ✓" : "➜ Послать отряд",
                    OrderArmed ? UiKit.Kind.Primary : UiKit.Kind.Normal, "Следующее нажатие по карте — приказ отряду этого бойца"))
                OrderArmed = !OrderArmed;
            if (UiKit.Button(new Rect(x + 16f + bw, cy, bw, UiKit.BtnH), "Домой", UiKit.Kind.Normal, "Отряд возвращается охранять свою казарму"))
            {
                a.Squad.GoHome(SoldierOrder.Guard);
                OrderArmed = false;
            }
            if (OrderArmed)
            {
                cy += UiKit.BtnH + 2f;
                float th = UiKit.TextH(OrderHint(a), w - 20f, UiKit.Small);
                LocGUI.Label(new Rect(x + 10f, cy, w - 20f, th), UiKit.Col(UiKit.InfoHex, OrderHint(a)), UiKit.Small);
            }
        }

        return new Rect(x, y, w + 5f, h);
    }

    // Строка «ключ — значение»: значение переносится, высота по тексту
    static float KvH(float w, string value)
    {
        return Mathf.Max(UiKit.RowH, UiKit.TextH(value ?? "", w - 20f - KeyW)) + 2f;
    }

    static void Kv(float x, ref float cy, float w, string label, string value)
    {
        float h = KvH(w, value);
        LocGUI.Label(new Rect(x + 10f, cy, KeyW, UiKit.RowH), label, UiKit.MutedStyle);
        LocGUI.Label(new Rect(x + 10f + KeyW, cy, w - 20f - KeyW, h), value);
        cy += h;
    }

    const float KeyW = 78f;

    static int CountLines(string s)
    {
        int n = 0;
        foreach (char c in s) if (c == '\n') n++;
        return n;
    }

    // Жёлтая стрелка над выбранным жителем
    public static void DrawMarker(CitizenAgent a)
    {
        if (a == null || a.IsInsideBuilding) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(a.transform.position + Vector3.up * 0.7f);
        if (sp.z < 0f) return;

        GUIStyle st = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        Rect rc = new Rect(UiKit.ToGui(sp).x - 15f, UiKit.ToGui(sp).y - 15f, 30f, 30f);
        st.normal.textColor = Color.black;
        LocGUI.Label(new Rect(rc.x + 1f, rc.y + 1f, rc.width, rc.height), "▼", st);
        st.normal.textColor = new Color(0.4f, 1f, 1f);
        LocGUI.Label(rc, "▼", st);
    }
}