using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обновление 12: полный повтор набега. Раньше повтор был схемой (красные и синие квадратики на плане).
/// Теперь запись разыгрывается «вживую»: здания - своими картинками (у вашей деревни - ваши здания вашего уровня),
/// бойцы - нарисованными человечками своего вида (верхом - на коне), с полосками здоровья. Летят стрелы, ядра и огонь,
/// рушатся здания (вспышка, дым), падают погибшие. Карту можно двигать (тянуть) и приближать, есть «Следить за боем».
/// Сцена стоит далеко от города (Origin) и снимается своей камерой прямо в окно повтора.
/// Записи старых версий (без вида бойцов) тоже показываются: здания - по виду, бойцы - цветными человечками.
/// </summary>
public class ReplayStage
{
    static readonly Vector3 Origin = new Vector3(0f, 30000f, 0f);
    static readonly Color AttackRing = new Color(1f, 0.3f, 0.25f, 0.55f);
    static readonly Color DefendRing = new Color(0.35f, 0.65f, 1f, 0.55f);

    readonly BattleReplay rep;
    readonly Dictionary<int, ReplayName> names;
    GameObject root;
    Camera cam;
    readonly List<GameObject> buildings = new List<GameObject>();
    readonly List<SpriteRenderer> buildingLooks = new List<SpriteRenderer>();
    readonly List<Color> buildingBase = new List<Color>();
    readonly List<int> lastHp = new List<int>();

    class Ghost
    {
        public GameObject go;
        public SpriteRenderer body;
        public ArtFigure fig;
        public bool attacker;
        public float lastX;
        public int hp;
        public bool seen;
        public float clashAt;
    }
    readonly Dictionary<int, Ghost> ghosts = new Dictionary<int, Ghost>();

    public Rect Bounds { get; private set; }     // клетки, в которых идёт бой
    public bool Follow = true;
    float zoom = -1f;
    Vector2 camPos;
    int shotFrame = -1;                          // кадр, выстрелы которого уже показаны
    float prevTime = -1f;

    public ReplayStage(BattleReplay r, Dictionary<int, ReplayName> n)
    {
        rep = r;
        names = n;
        Build();
    }

    Vector3 W(float x, float y) { return Origin + new Vector3(x, y, 0f); }

    void Build()
    {
        root = new GameObject("ReplayStage");
        root.transform.position = Vector3.zero;

        // Границы боя: здания и все точки бойцов
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var b in rep.buildings)
        {
            if (b == null) continue;
            minX = Mathf.Min(minX, b.x); minY = Mathf.Min(minY, b.y);
            maxX = Mathf.Max(maxX, b.x + b.w); maxY = Mathf.Max(maxY, b.y + b.h);
        }
        foreach (var f in rep.frames)
            foreach (var u in f.u)
            {
                minX = Mathf.Min(minX, u.x / 10f); minY = Mathf.Min(minY, u.y / 10f);
                maxX = Mathf.Max(maxX, u.x / 10f); maxY = Mathf.Max(maxY, u.y / 10f);
            }
        if (minX > maxX) { float h = Mathf.Max(8, rep.size) * 0.5f; minX = minY = -h; maxX = maxY = h; }
        Bounds = Rect.MinMaxRect(minX - 3f, minY - 3f, maxX + 3f, maxY + 3f);

        // Земля: трава и лёгкая сетка клеток
        var ground = Quad("Ground", new Vector2(Bounds.center.x, Bounds.center.y), new Vector2(Bounds.width + 30f, Bounds.height + 30f), new Color(0.30f, 0.42f, 0.22f), -200);
        ground.transform.SetParent(root.transform, true);
        var field = Quad("Field", new Vector2(Bounds.center.x, Bounds.center.y), new Vector2(Bounds.width, Bounds.height), new Color(0.35f, 0.48f, 0.26f), -199);
        field.transform.SetParent(root.transform, true);

        // Здания
        foreach (var b in rep.buildings)
        {
            GameObject go = new GameObject("RB_" + (b != null ? b.title : ""));
            go.transform.SetParent(root.transform, false);
            go.transform.position = W(b.x + b.w * 0.5f, b.y + b.h * 0.5f);
            GameObject body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            var sr = body.AddComponent<SpriteRenderer>();
            RaidBuildingKind kind = (RaidBuildingKind)Mathf.Clamp(b.kind, 0, 64);
            Sprite art = !string.IsNullOrEmpty(b.type) ? GameArt.Building(b.type, Mathf.Max(1, b.lv)) : null;
            if (art == null) art = GameArt.Raid(kind);
            Color baseCol = Color.white;
            if (art != null)
            {
                sr.sprite = art;
                Vector2 a = art.bounds.size;
                body.transform.localScale = new Vector3(b.w / Mathf.Max(0.01f, a.x), b.h / Mathf.Max(0.01f, a.y), 1f);
            }
            else
            {
                sr.sprite = CombatFx.Square();
                baseCol = kind == RaidBuildingKind.Wall || kind == RaidBuildingKind.Gate ? new Color(0.55f, 0.5f, 0.45f) : new Color(0.72f, 0.6f, 0.42f);
                body.transform.localScale = new Vector3(b.w - 0.08f, b.h - 0.08f, 1f);
            }
            sr.color = baseCol;
            sr.sortingOrder = kind == RaidBuildingKind.Farm ? -50 : -(int)(b.y * 2f);
            buildings.Add(go);
            buildingLooks.Add(sr);
            buildingBase.Add(baseCol);
            lastHp.Add(100);
        }

        // Камера: снимает только сцену (она далеко от города)
        Camera main = Camera.main;
        GameObject cg = new GameObject("ReplayCamera");
        cg.transform.SetParent(root.transform, false);
        cam = cg.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.2f, 0.13f);
        cam.depth = (main != null ? main.depth : 0f) + 20f;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
        camPos = Bounds.center;
        zoom = Mathf.Clamp(Mathf.Max(Bounds.height, Bounds.width * 0.6f) * 0.5f, 6f, 40f);
    }

    static GameObject Quad(string n, Vector2 at, Vector2 size, Color c, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.position = Origin + new Vector3(at.x, at.y, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CombatFx.Square();
        sr.color = c;
        sr.sortingOrder = order;
        return go;
    }

    public void Destroy()
    {
        if (root != null) Object.Destroy(root);
        root = null;
        ghosts.Clear();
    }

    /// <summary>Куда смотрит камера (прямоугольник экрана в пикселях, y снизу).</summary>
    public void SetViewport(Rect guiRect)
    {
        if (cam == null) return;
        Vector2 a = UiKit.ToScreen(new Vector2(guiRect.xMin, guiRect.yMax)), b = UiKit.ToScreen(new Vector2(guiRect.xMax, guiRect.yMin));
        float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
        cam.rect = new Rect(a.x / sw, a.y / sh, Mathf.Max(0.01f, (b.x - a.x) / sw), Mathf.Max(0.01f, (b.y - a.y) / sh));
        cam.enabled = true;
    }

    public void Hide() { if (cam != null) cam.enabled = false; }

    public void Pan(Vector2 guiDelta, Rect view)
    {
        if (cam == null) return;
        float unitsPerPoint = cam.orthographicSize * 2f / Mathf.Max(1f, view.height);
        camPos -= new Vector2(guiDelta.x, -guiDelta.y) * unitsPerPoint;
        camPos.x = Mathf.Clamp(camPos.x, Bounds.xMin - 5f, Bounds.xMax + 5f);
        camPos.y = Mathf.Clamp(camPos.y, Bounds.yMin - 5f, Bounds.yMax + 5f);
        Follow = false;
    }

    public void Zoom(float factor) { zoom = Mathf.Clamp(zoom * factor, 3f, 45f); }

    /// <summary>Точка сцены (клетки) → точка окон (для полосок и подписей).</summary>
    public bool ToGui(Vector2 cell, out Vector2 gp)
    {
        gp = default;
        if (cam == null) return false;
        Vector3 sp = cam.WorldToScreenPoint(W(cell.x, cell.y));
        if (sp.z < 0f) return false;
        gp = UiKit.ToGui(new Vector2(sp.x, sp.y));
        return true;
    }

    // ---------- Кадр ----------

    public struct Info { public int attackers, defenders, ruined; }

    /// <summary>Поставить сцену на время t (кадр fi и доля k до следующего). Возвращает счёт.</summary>
    public Info Show(int fi, float k, float t, List<string> events)
    {
        var info = new Info();
        if (root == null) return info;
        ReplayFrame f = rep.frames[fi];
        ReplayFrame g = fi + 1 < rep.frames.Count ? rep.frames[fi + 1] : f;
        bool forward = prevTime >= 0f && t >= prevTime && t - prevTime < 3f * Mathf.Max(1, rep.step);
        prevTime = t;

        // Здания: руины - серые, с дымом; рушится на глазах - вспышка
        for (int i = 0; i < buildings.Count; i++)
        {
            int hp = i < f.b.Count ? f.b[i] : -2;   // -2: здания ещё нет (заслон поставили позже)
            bool exists = hp != -2;
            if (buildings[i].activeSelf != exists) buildings[i].SetActive(exists);
            if (!exists) continue;
            if (hp < 0) info.ruined++;
            var sr = buildingLooks[i];
            Color want = hp < 0 ? new Color(0.38f, 0.36f, 0.34f, 0.95f) : Color.Lerp(new Color(1f, 0.75f, 0.7f), buildingBase[i], 0.35f + 0.65f * hp / 100f);
            if (sr.color != want) sr.color = want;
            if (forward && hp < 0 && lastHp[i] >= 0)
            {
                var b = rep.buildings[i];
                Vector2 c = W(b.x + b.w * 0.5f, b.y + b.h * 0.5f);
                CombatFx.Blast(c, Mathf.Max(b.w, b.h) * 0.6f);
                CombatFx.Flash(c);
                if (events != null) events.Add($"{Clock(t)} — разрушено: {Clean(b.title)}");
            }
            if (hp < 0 && Random.value < Time.unscaledDeltaTime * 1.2f)
            {
                var b = rep.buildings[i];
                CombatFx.Spark(W(b.x + Random.value * b.w, b.y + Random.value * b.h), new Color(0.35f, 0.33f, 0.32f, 0.8f), 0.5f, 1.4f);
            }
            lastHp[i] = hp;
        }

        // Бойцы
        var next = new Dictionary<int, ReplayUnit>();
        foreach (var u in g.u) next[u.i] = u;
        foreach (var gh in ghosts.Values) gh.seen = false;
        foreach (var u in f.u)
        {
            float x = u.x / 10f, y = u.y / 10f;
            if (next.TryGetValue(u.i, out ReplayUnit n) && (Mathf.Abs(n.x - u.x) + Mathf.Abs(n.y - u.y)) < 120) { x = Mathf.Lerp(x, n.x / 10f, k); y = Mathf.Lerp(y, n.y / 10f, k); }
            Ghost gh = GhostOf(u.i);
            gh.seen = true;
            gh.hp = u.h;
            if (!gh.go.activeSelf) gh.go.SetActive(true);
            Vector3 p = W(x, y);
            float dx = p.x - gh.go.transform.position.x;
            gh.go.transform.position = p;
            if (Mathf.Abs(dx) > 0.004f)
            {
                Vector3 s = gh.body.transform.localScale;
                float sx = Mathf.Abs(s.x) * (dx < 0f ? -1f : 1f);
                if (s.x != sx) gh.body.transform.localScale = new Vector3(sx, s.y, s.z);
            }
            gh.body.sortingOrder = 13 - Mathf.RoundToInt(y * 2f) % 200;
            if (gh.attacker) info.attackers++; else info.defenders++;
        }
        // Пропали из кадра: погибли (или ушли) - след на земле
        foreach (var kv in ghosts)
        {
            Ghost gh = kv.Value;
            if (gh.seen || !gh.go.activeSelf) continue;
            gh.go.SetActive(false);
            bool stillNext = next.ContainsKey(kv.Key);
            if (forward && !stillNext && gh.hp < 60) CombatFx.Death(gh.go.transform.position);
        }

        // Ближний бой: соседние враги делают выпады, летят искры
        if (forward) Clashes(t);

        // Выстрелы кадра: раскидываем по времени кадра
        if (forward && rep.real)
        {
            for (int fr = Mathf.Max(shotFrame + 1, fi - 1); fr <= fi; fr++)
            {
                var shots = rep.frames[fr].s;
                if (shots != null) foreach (var s in shots) Shot(s);
                shotFrame = fr;
            }
        }
        else shotFrame = fi;

        // Камера
        if (cam != null)
        {
            if (Follow) FollowAction(f);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize <= 0.1f ? zoom : cam.orthographicSize, zoom, 0.2f);
            cam.transform.position = new Vector3(Origin.x + camPos.x, Origin.y + camPos.y, -20f);
        }
        return info;
    }

    void FollowAction(ReplayFrame f)
    {
        // Центр нападающих (их меньше и они двигаются); никого - центр боя
        Vector2 sum = Vector2.zero; int n = 0;
        foreach (var u in f.u)
            if (names.TryGetValue(u.i, out ReplayName nm) && nm.a) { sum += new Vector2(u.x / 10f, u.y / 10f); n++; }
        Vector2 want = n > 0 ? sum / n : Bounds.center;
        camPos = Vector2.Lerp(camPos, want, Mathf.Clamp01(Time.unscaledDeltaTime * 1.5f));
    }

    void Clashes(float t)
    {
        var att = new List<Ghost>();
        var def = new List<Ghost>();
        foreach (var gh in ghosts.Values) if (gh.seen) (gh.attacker ? att : def).Add(gh);
        if (att.Count * def.Count > 6000) return;   // огромный бой: без искр
        foreach (var a in att)
        {
            if (Time.unscaledTime < a.clashAt) continue;
            foreach (var d in def)
            {
                if ((a.go.transform.position - d.go.transform.position).sqrMagnitude > 1.1f) continue;
                a.clashAt = Time.unscaledTime + Random.Range(0.7f, 1.3f);
                if (a.fig != null) a.fig.Lunge();
                if (d.fig != null && Random.value < 0.6f) d.fig.Lunge();
                CombatFx.Spark((a.go.transform.position + d.go.transform.position) * 0.5f, new Color(1f, 0.9f, 0.6f), 0.16f, 0.2f);
                break;
            }
        }
    }

    void Shot(ReplayShot s)
    {
        Vector2 a = W(s.x0 / 10f, s.y0 / 10f), b = W(s.x1 / 10f, s.y1 / 10f);
        bool was = FieldPost.VisualShots;
        FieldPost.VisualShots = true;   // только картинка: заслоны города не толкаем
        try
        {
            switch (s.k)
            {
                case 1: CombatFx.Shell(a, b, 0.6f, 1.2f, () => CombatFx.Blast(b, 1.2f)); break;
                case 2: CombatFx.Bolt(a, b); break;
                case 3: CombatFx.Fireball(a, b, 1f, null); break;
                default: CombatFx.Arrow(a, b); break;
            }
        }
        finally { FieldPost.VisualShots = was; }
    }

    Ghost GhostOf(int id)
    {
        if (ghosts.TryGetValue(id, out Ghost gh)) return gh;
        names.TryGetValue(id, out ReplayName nm);
        gh = new Ghost { attacker = nm != null && nm.a };
        gh.go = new GameObject("RU_" + id);
        gh.go.transform.SetParent(root.transform, false);
        float scale = 0.55f;
        // Кружок под ногами: красный - нападающие, синий - ваши
        var ring = Part(gh.go, "Side", new Vector2(0f, -scale * 0.42f), new Vector2(scale * 0.95f, scale * 0.38f), gh.attacker ? AttackRing : DefendRing, 10);
        ring.sprite = CombatFx.Circle();
        var ol = Part(gh.go, "Outline", Vector2.zero, new Vector2(scale + 0.12f, scale + 0.12f), gh.attacker ? new Color(0.35f, 0.05f, 0.05f) : new Color(0.05f, 0.1f, 0.3f), 12);
        Color tint = nm != null && nm.c != 0 ? new Color(((nm.c >> 16) & 255) / 255f, ((nm.c >> 8) & 255) / 255f, (nm.c & 255) / 255f)
                                             : (gh.attacker ? new Color(0.95f, 0.35f, 0.3f) : new Color(0.4f, 0.65f, 1f));
        gh.body = Part(gh.go, "Body", Vector2.zero, new Vector2(scale, scale), tint, 13);
        string kind = nm != null && !string.IsNullOrEmpty(nm.k) ? nm.k : GuessKind(nm);
        gh.fig = GameArt.Dress(gh.body, kind, ol);
        if (gh.fig == null && kind != "sword") gh.fig = GameArt.Dress(gh.body, "sword", ol);
        if (nm != null && nm.hr)
        {
            var h = Part(gh.go, "Horse", new Vector2(0f, -scale * 0.45f), new Vector2(scale * 1.5f, scale * 0.42f), new Color(0.45f, 0.29f, 0.14f), 11);
            if (gh.fig != null) { GameArt.PlaceHorse(h, scale); gh.fig.Mounted = true; gh.fig.Horse = h; }
        }
        ghosts[id] = gh;
        return gh;
    }

    static SpriteRenderer Part(GameObject parent, string n, Vector2 pos, Vector2 size, Color c, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = CombatFx.Square();
        r.color = c;
        r.sortingOrder = order;
        return r;
    }

    /// <summary>Полоски здоровья бойцов и зданий, рисуются в OnGUI поверх окна сцены.</summary>
    public void DrawBars(Rect view, int fi)
    {
        if (cam == null || Event.current.type != EventType.Repaint) return;
        float ppu = view.height / Mathf.Max(0.1f, cam.orthographicSize * 2f);
        Color old = GUI.color;
        ReplayFrame f = rep.frames[fi];
        for (int i = 0; i < rep.buildings.Count && i < f.b.Count; i++)
        {
            int hp = f.b[i];
            if (hp < 0 || hp >= 100) continue;
            var b = rep.buildings[i];
            if (!ToGui(new Vector2(b.x + b.w * 0.5f, b.y + b.h), out Vector2 p) || !view.Contains(p)) continue;
            float w = Mathf.Clamp(b.w * ppu * 0.8f, 16f, 70f);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(p.x - w * 0.5f - 1f, p.y - 5f, w + 2f, 5f), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(UiKit.Bad, UiKit.Good, hp / 100f);
            GUI.DrawTexture(new Rect(p.x - w * 0.5f, p.y - 4f, w * hp / 100f, 3f), Texture2D.whiteTexture);
        }
        if (ppu >= 14f)
            foreach (var kv in ghosts)
            {
                Ghost gh = kv.Value;
                if (!gh.seen || gh.hp >= 100) continue;
                Vector3 sp = cam.WorldToScreenPoint(gh.go.transform.position + Vector3.up * 0.45f);
                Vector2 p = UiKit.ToGui(new Vector2(sp.x, sp.y));
                if (!view.Contains(p)) continue;
                float w = Mathf.Clamp(ppu * 0.6f, 10f, 26f);
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.DrawTexture(new Rect(p.x - w * 0.5f - 1f, p.y - 1f, w + 2f, 4f), Texture2D.whiteTexture);
                GUI.color = gh.attacker ? new Color(1f, 0.4f, 0.3f) : new Color(0.45f, 0.75f, 1f);
                GUI.DrawTexture(new Rect(p.x - w * 0.5f, p.y, w * gh.hp / 100f, 2f), Texture2D.whiteTexture);
            }
        GUI.color = old;
    }

    // Старые записи (без рисунка бойца): угадываем по имени - как GameArt.FigureKind
    static string GuessKind(ReplayName nm)
    {
        string n = nm != null ? nm.n ?? "" : "";
        if (n.Contains("Голем")) return GameArt.Golem;
        if (n.Contains("Рыцарь")) return "knight";
        if (n.Contains("аг")) return "mage";
        if (n.Contains("учник") || (nm != null && nm.r)) return "archer";
        return "sword";
    }

    static string Clean(string t) { return string.IsNullOrEmpty(t) ? "здание" : t; }
    static string Clock(float s) { int i = Mathf.FloorToInt(s); return $"{i / 60}:{i % 60:00}"; }
}
