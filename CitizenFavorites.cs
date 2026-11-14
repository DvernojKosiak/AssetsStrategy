using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обновление 12: избранные жители. В карточке жителя - звёздочка «★ В избранное».
/// Над избранными на карте - золотая звёздочка (видно, где он сейчас), в меню города - вкладка «Избранные»:
/// кто, чем занят, здоровье, кнопка «Следить» (камера едет за ним, карточка открыта).
/// Об избранном приходят вести: тяжело ранен, погиб, стал солдатом.
/// Сохраняется с городом: бойцы - вместе с армией (имя тоже сохраняется), простые жители - по имени
/// (после входа имя и звёздочка достаются одному из жителей: самих жителей игра не хранит поштучно).
/// </summary>
public class CitizenFavorites : MonoBehaviour
{
    public const int Max = 12;
    static readonly List<CitizenAgent> favs = new List<CitizenAgent>();
    static readonly List<string> pendingCiv = new List<string>();
    static float pendingUntil;
    static CitizenFavorites host;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { favs.Clear(); pendingCiv.Clear(); host = null; hurtAt.Clear(); lastRole.Clear(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { Ensure(); }

    static void Ensure()
    {
        if (host != null) return;
        var go = new GameObject("CitizenFavorites");
        DontDestroyOnLoad(go);
        host = go.AddComponent<CitizenFavorites>();
    }

    void Start() { GameMenu.AddPage("Избранные", PageHeight, DrawPage); }

    public static bool Is(CitizenAgent a) { return a != null && favs.Contains(a); }
    public static int Count { get { Clean(); return favs.Count; } }

    public static void Toggle(CitizenAgent a)
    {
        if (a == null || a.IsDead) return;
        Clean();
        if (favs.Remove(a)) { WarManager.Notify($"{a.CitizenName} больше не в избранном", true); return; }
        if (favs.Count >= Max) { WarManager.Notify($"Избранных не больше {Max}: уберите звёздочку у кого-нибудь", false); return; }
        favs.Add(a);
        lastRole[a] = a.IsSoldier;
        WarManager.Notify($"★ {a.CitizenName} — в избранном. Найти его: «Город» → «Избранные»", true);
    }

    static void Clean() { favs.RemoveAll(a => a == null); }

    /// <summary>Вызывает CitizenAgent при гибели.</summary>
    public static void NoteDeath(CitizenAgent a)
    {
        if (!favs.Remove(a)) return;
        WarManager.Notify($"★ Погиб ваш избранный житель {a.CitizenName}", false);
        Sfx.Ui("ui_error");
    }

    // ---------- Вести и метки ----------

    static readonly Dictionary<CitizenAgent, float> hurtAt = new Dictionary<CitizenAgent, float>();
    static readonly Dictionary<CitizenAgent, bool> lastRole = new Dictionary<CitizenAgent, bool>();
    float tick;

    void Update()
    {
        tick -= Time.unscaledDeltaTime;
        if (tick > 0f) return;
        tick = 0.5f;
        Clean();
        AssignPending();
        foreach (var a in favs)
        {
            if (a.IsDead) continue;
            if (a.HpFraction < 0.35f && (!hurtAt.TryGetValue(a, out float t) || Time.time - t > 60f))
            {
                hurtAt[a] = Time.time;
                WarManager.Notify($"★ {a.CitizenName} тяжело ранен ({Mathf.RoundToInt(a.HpFraction * 100f)}%)", false);
            }
            bool sol = a.IsSoldier;
            if (lastRole.TryGetValue(a, out bool was) && was != sol && sol) WarManager.Notify($"★ {a.CitizenName} стал солдатом: {a.DisplayName}", true);
            lastRole[a] = sol;
        }
    }

    static readonly HashSet<BuildingInstance> starredHouses = new HashSet<BuildingInstance>();

    static bool CityView => !WarView.IsOpen && !WarCityMode.Active && !RaidManager.ViewingBattle;

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || favs.Count == 0 || !CityView) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        UiKit.Apply();
        GUI.depth = 20;   // обновление 14: звёздочки - позади всех окон, как подложка казны
        if (starStyle == null) starStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        starredHouses.Clear();
        foreach (var a in favs)
        {
            if (a == null || a.IsDead) continue;
            Vector3 world = a.transform.position + Vector3.up * 0.62f;
            if (a.IsInsideBuilding)
            {
                // final-13-fix-2: раньше в здании звёздочка просто пропадала - теперь она над зданием, где он сидит
                BuildingInstance h = a.InsideBuilding;
                if (h == null || !starredHouses.Add(h)) continue;   // в походе - не на карте; в одном доме несколько - одна звезда
                SpriteRenderer sr = h.GetComponentInChildren<SpriteRenderer>();
                world = sr != null ? new Vector3(sr.bounds.center.x, sr.bounds.max.y + 0.15f, 0f) : (Vector3)h.Center + Vector3.up;
            }
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0f) continue;
            Vector2 g = UiKit.ToGui(sp);
            if (g.x < -20f || g.y < -20f || g.x > UiKit.W + 20f || g.y > UiKit.H + 20f) continue;
            Rect rc = new Rect(g.x - 12f, g.y - 12f, 24f, 24f);
            if (BuildingPlacer.OtherWindowOver(rc) || BuildingPlacer.IsPointerOverPanel(rc.center)) continue;   // под окном - не видно (раньше светилась поверх окна «В бой!»)
            starStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rc.x + 1f, rc.y + 1f, rc.width, rc.height), "★", starStyle);
            starStyle.normal.textColor = new Color(1f, 0.84f, 0.25f);
            GUI.Label(rc, "★", starStyle);
        }
    }

    static GUIStyle starStyle;

    // ---------- Вкладка в меню города ----------

    const float RowH = 64f;

    static float PageHeight(float w)
    {
        Clean();
        return favs.Count == 0 ? 66f : favs.Count * (RowH + 4f) + 4f;
    }

    static void DrawPage(Rect r)
    {
        Clean();
        if (favs.Count == 0)
        {
            LocGUI.Label(new Rect(r.x, r.y, r.width, 62f),
                "Пока никого. Нажмите на жителя на карте и в его карточке — «☆ В избранное». Над избранными видна золотая звёздочка.",
                UiKit.Small);
            return;
        }
        float y = r.y;
        CitizenAgent unstar = null;
        foreach (var a in favs)
        {
            Rect row = new Rect(r.x, y, r.width, RowH);
            GUI.Box(row, GUIContent.none);
            string role = a.IsSoldier ? a.DisplayName : (a.IsMilitary ? "новобранец" : "житель");
            LocGUI.Label(new Rect(row.x + 6f, row.y + 2f, row.width - 40f, 20f),
                $"{UiKit.Col("#FFD640", "★")} <b>{a.CitizenName}</b> {UiKit.Col(UiKit.MutedHex, "· " + role)}", UiKit.Body);
            if (LocGUI.Button(new Rect(row.xMax - 30f, row.y + 3f, 26f, 20f), "✕", UiKit.ButtonStyle(UiKit.Kind.Ghost))) unstar = a;
            UiKit.Tip(new Rect(row.xMax - 30f, row.y + 3f, 26f, 20f), "Убрать из избранного");
            string act = a.IsInsideBuilding ? (a.IsAway ? "в походе" : "в здании") : StripLong(a.ActivityText);
            LocGUI.Label(new Rect(row.x + 6f, row.y + 22f, row.width - 12f, 18f), act, UiKit.Small);
            float bw = 84f;
            UiKit.Bar(new Rect(row.x + 6f, row.y + 42f, row.width - bw - 18f, 16f), a.HpFraction,
                Color.Lerp(UiKit.Bad, UiKit.Good, a.HpFraction), $"{Mathf.RoundToInt(a.HpFraction * 100f)}%");
            if (UiKit.Button(new Rect(row.xMax - bw - 6f, row.y + 40f, bw, 20f), "Следить", UiKit.Kind.Primary, "Камера едет за жителем, карточка открыта"))
                BuildingPlacer.FollowCitizen(a);
            y += RowH + 4f;
        }
        if (unstar != null) favs.Remove(unstar);
    }

    static string StripLong(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int nl = s.IndexOf('\n');
        return nl > 0 ? s.Substring(0, nl) : s;
    }

    // ---------- Сохранение ----------

    static string Safe(string n) { return (n ?? "").Replace(',', ' ').Replace(';', ' ').Replace('=', ' ').Trim(); }

    /// <summary>CitySave.CaptureCity: простые жители в избранном (бойцы - в CityArmy).</summary>
    public static void SaveState(SaveBag bag)
    {
        if (WarCityMode.Active) return;
        Clean();
        var civ = new List<string[]>();
        foreach (var a in favs) if (!a.IsDead && !a.IsSoldier) civ.Add(new[] { Safe(a.CitizenName) });
        foreach (var n in pendingCiv) if (civ.Count < Max) civ.Add(new[] { Safe(n) });   // ещё не досталось никому
        bag.SetList("fav.civ", civ);
    }

    public static void LoadState(SaveBag bag)
    {
        if (WarCityMode.Active) return;
        Ensure();
        favs.Clear();
        pendingCiv.Clear();
        foreach (var a in bag.List("fav.civ")) if (a.Length > 0 && a[0].Length > 0) pendingCiv.Add(a[0]);
        pendingUntil = Time.time + 90f;
    }

    /// <summary>CityArmy.Capture: имя бойца и звёздочка, по порядку записи армии.</summary>
    public static string[] PackSoldier(CitizenAgent a) { return new[] { Safe(a.CitizenName), SaveBag.B(Is(a)) }; }

    /// <summary>CityArmy.Restore: вернуть бойцу имя и звёздочку.</summary>
    public static void RestoreSoldier(CitizenAgent a, string[] rec)
    {
        if (a == null || rec == null || rec.Length == 0) return;
        if (rec[0].Length > 0) a.RestoreName(rec[0]);
        if (SaveBag.AtB(rec, 1) && !favs.Contains(a) && favs.Count < Max) { favs.Add(a); lastRole[a] = a.IsSoldier; }
    }

    // Жителей после загрузки ещё нет или их мало - отдаём имена по мере появления
    static void AssignPending()
    {
        if (pendingCiv.Count == 0) return;
        if (Time.time > pendingUntil) { pendingCiv.Clear(); return; }
        foreach (var a in CitizenAgent.All)
        {
            if (pendingCiv.Count == 0) break;
            if (a == null || a.IsDead || a.IsMilitary || favs.Contains(a) || favs.Count >= Max) continue;
            a.RestoreName(pendingCiv[0]);
            pendingCiv.RemoveAt(0);
            favs.Add(a);
            lastRole[a] = false;
        }
    }
}
