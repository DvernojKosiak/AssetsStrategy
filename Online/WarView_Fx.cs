using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Клановая война (klan-voina-14): чтобы война чувствовалась.
///  - Крупные объявления по центру о главном: новый этап, захвачен / потерян участок, враг в деревне, десятки поверженных,
///    деревня пала, победа или поражение. Со звуком.
///  - Враг в своей деревне - края экрана пульсируют красным, трубит тревога (не чаще раза в 45 с).
///  - Повреждённые здания на карте горят и дымят (WarFire), над захваченными источниками - флаги сторон (WarFlag).
/// </summary>
public partial class WarView
{
    struct Moment { public string title, sub; public Color color; public float at; public bool big; }
    readonly List<Moment> moments = new List<Moment>();
    float momentAt = -99f;
    bool momentsReady;
    int enemiesHome, lastKillsMark;
    float alarmAt = -99f, alarmUntil;
    static Texture2D alarmTex, bandTex;

    void Announce(string title, string sub, Color color, bool big = false, string sound = null)
    {
        if (moments.Count >= 4) moments.RemoveAt(1);   // не копим: старое (кроме показываемого) уступает новому
        moments.Add(new Moment { title = title, sub = sub, color = color, big = big });
        if (!string.IsNullOrEmpty(sound)) Sfx.Ui(sound);
    }

    int PlotsOf(string plots, int side)
    {
        if (string.IsNullOrEmpty(plots)) return 0;
        char c = (char)('0' + side);
        int n = 0;
        foreach (char ch in plots) if (ch == c) n++;
        return n;
    }

    /// <summary>Новый кадр карты: что случилось важного с прошлого (fr - прошлый кадр).</summary>
    void Moments(WarFrame f)
    {
        if (st == null || f == null) return;
        WarCityView me = MyCity;

        // Враги в своей деревне
        int inside = 0;
        if (me != null)
        {
            float x0 = me.plotX * WarRules.Plot, y0 = me.plotY * WarRules.Plot;
            foreach (var u in units.Values)
                if (u.side != st.mySide && u.hp > 0 && u.goal.x >= x0 && u.goal.x < x0 + WarRules.CitySize && u.goal.y >= y0 && u.goal.y < y0 + WarRules.CitySize) inside++;
        }

        if (!momentsReady || fr == null)
        {
            momentsReady = true;
            enemiesHome = inside;
            lastKillsMark = f.kills / 10;
            return;
        }

        if (f.stage != fr.stage)
        {
            WarStage s = (WarStage)f.stage;
            if (s == WarStage.Finished)
            {
                if (f.winner == st.mySide) Announce("ПОБЕДА!", "Клан взял верх в войне", new Color(1f, 0.85f, 0.3f), true, "victory");
                else if (f.winner != 0) Announce("Поражение", "Враг оказался сильнее. Рейтинг клана меняется", new Color(1f, 0.35f, 0.3f), true, "defeat");
                else Announce("Ничья", "Силы равны", new Color(0.85f, 0.85f, 0.9f), true, "notify_good");
            }
            else if (s == WarStage.Neutral) Announce("Нейтральные земли открыты", "Захватывайте источники: рудники, лесопилки, деревни", new Color(1f, 0.82f, 0.4f), true, "raid_march");
            else if (s == WarStage.Open) Announce("Вся карта открыта!", "Можно идти в земли врага и жечь его деревни", new Color(1f, 0.45f, 0.3f), true, "horn_alarm");
            else Announce("Новый этап войны", WarRules.StageName(s), new Color(1f, 0.82f, 0.4f), false, "notify_good");
        }

        if (f.stage < (int)WarStage.Finished)
        {
            int mine = PlotsOf(f.plots, st.mySide), was = PlotsOf(fr.plots, st.mySide);
            if (mine > was) Announce(mine - was == 1 ? "Участок захвачен" : $"Захвачено участков: {mine - was}", $"У клана {mine} участков", new Color(0.45f, 0.85f, 1f), false, "upgrade_complete");
            else if (mine < was) Announce("Враг занял наш участок", "Отбейте его: постойте там без врагов минуту", new Color(1f, 0.4f, 0.3f), false, "notify_bad");

            if (inside > 0 && enemiesHome == 0 && Time.unscaledTime - alarmAt > 45f)
            {
                alarmAt = Time.unscaledTime;
                Announce("Враг в деревне!", $"Врагов у вас: {inside}. Бойцы казарм выходят навстречу", new Color(1f, 0.3f, 0.25f), true, "horn_alarm");
            }
            if (f.kills / 10 > lastKillsMark && f.kills >= 10)
                Announce($"Повержено врагов: {f.kills / 10 * 10}", "Так держать!", new Color(1f, 0.85f, 0.3f), false, "notify_good");
            if (f.fallen && !fr.fallen) Announce("Деревня пала", "Цитадель разрушена. Помогайте клану бойцами, что остались на карте", new Color(1f, 0.3f, 0.25f), true, "defeat");
        }
        lastKillsMark = f.kills / 10;
        enemiesHome = inside;
        if (inside > 0) alarmUntil = Time.unscaledTime + 1.5f;
    }

    // ---------- Огонь на зданиях ----------

    void BurnBuilding(BldView v)
    {
        if (v == null || v.go == null) return;
        bool wall = v.d.type == WarRules.WallName || v.d.type == WarRules.GateName;
        if (v.fire == null)
        {
            if (v.hp >= 70) return;
            if (wall && v.hp > 0) return;   // стены не горят - только рушатся
            v.fire = WarFire.On(v.go, v.d.w, v.d.h);
        }
        v.fire.SetHp(wall && v.hp > 0 ? 100 : v.hp);
    }

    // ---------- Экран ----------

    static Texture2D AlarmTex()
    {
        if (alarmTex != null) return alarmTex;
        alarmTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = Mathf.Abs(x - 31.5f) / 31.5f, dy = Mathf.Abs(y - 31.5f) / 31.5f;
                float d = Mathf.Max(dx, dy) * 0.6f + Mathf.Sqrt(dx * dx + dy * dy) * 0.4f;
                float a = Mathf.Clamp01((d - 0.62f) / 0.38f);
                alarmTex.SetPixel(x, y, new Color(0.85f, 0.05f, 0.02f, a * a));
            }
        alarmTex.Apply();
        return alarmTex;
    }

    static Texture2D BandTex()
    {
        if (bandTex != null) return bandTex;
        bandTex = new Texture2D(64, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 64; x++)
            {
                float a = Mathf.Pow(Mathf.Sin(x / 63f * Mathf.PI), 0.6f) * Mathf.Sin((y + 0.5f) / 8f * Mathf.PI);
                bandTex.SetPixel(x, y, new Color(0.03f, 0.02f, 0.02f, 0.82f * a));
            }
        bandTex.Apply();
        return bandTex;
    }

    /// <summary>Враг в деревне: края экрана пульсируют красным.</summary>
    void DrawWarFx()
    {
        if (e.type != EventType.Repaint) return;
        if (Time.unscaledTime > alarmUntil || fr == null || fr.stage >= (int)WarStage.Finished) return;
        float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4.2f);
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.35f + 0.35f * pulse);
        GUI.DrawTexture(UiKit.FullRect, AlarmTex(), ScaleMode.StretchToFill, true);
        GUI.color = old;
    }

    GUIStyle annTitle, annSub;

    /// <summary>Объявление по центру сверху: всплывает, держится и гаснет.</summary>
    void DrawAnnounce()
    {
        if (moments.Count == 0) return;
        float now = Time.unscaledTime;
        Moment m = moments[0];
        if (m.at <= 0f) { m.at = now; moments[0] = m; }
        float life = m.big ? 3.4f : 2.6f, t = now - m.at;
        if (t > life) { moments.RemoveAt(0); return; }
        if (e.type != EventType.Repaint) return;

        float inK = Mathf.Clamp01(t / 0.28f), outK = Mathf.Clamp01((life - t) / 0.45f);
        float a = Mathf.Min(inK, outK);
        float pop = 1f + (1f - inK) * 0.35f;
        if (annTitle == null)
        {
            annTitle = new GUIStyle(UiKit.Title) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontStyle = FontStyle.Bold, richText = false };
            annSub = new GUIStyle(UiKit.Body) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = false };
        }
        int ts = Mathf.RoundToInt((m.big ? (UiKit.Phone ? 30f : 36f) : (UiKit.Phone ? 22f : 26f)) * pop);
        annTitle.fontSize = ts;
        annSub.fontSize = UiKit.Phone ? 13 : 14;

        // По центру свободного места: левее плашки войны (справа), ниже сообщений деревни
        float cx = embedded ? Mathf.Max(160f, (ChipRect.x - 8f) / 2f) : UiKit.W / 2f;
        float y = Mathf.Round(UiKit.H * 0.24f);
        float bw = Mathf.Min(cx * 2f - 16f, m.big ? 620f : 520f);
        float bh = ts + 34f;
        Rect band = new Rect(cx - bw / 2f, y - 6f, bw, bh);
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(band, BandTex(), ScaleMode.StretchToFill, true);
        // тонкие полосы цвета события сверху и снизу
        GUI.color = new Color(m.color.r, m.color.g, m.color.b, 0.8f * a);
        float lw = bw * 0.55f * inK;
        GUI.DrawTexture(new Rect(cx - lw / 2f, band.y, lw, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - lw / 2f, band.yMax - 2f, lw, 2f), Texture2D.whiteTexture);

        string title = Loc.T(m.title), sub = Loc.T(m.sub ?? "");
        Rect tr = new Rect(band.x, y, band.width, ts + 6f);
        annTitle.normal.textColor = new Color(0f, 0f, 0f, 0.75f * a);
        GUI.color = Color.white;
        GUI.Label(new Rect(tr.x + 2f, tr.y + 2f, tr.width, tr.height), title, annTitle);
        annTitle.normal.textColor = new Color(m.color.r, m.color.g, m.color.b, a);
        GUI.Label(tr, title, annTitle);
        if (sub.Length > 0)
        {
            annSub.normal.textColor = new Color(0.95f, 0.92f, 0.86f, a);
            GUI.Label(new Rect(band.x, tr.yMax - 2f, band.width, 20f), sub, annSub);
        }
        GUI.color = old;
    }
}
