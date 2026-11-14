using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Короткое обучение при первом запуске: карточка в правом нижнем углу, 10 шагов.
/// Где нужно действие (сдвинуть карту, нажать на Цитадель, поставить дом…), шаг засчитывается сам, как только игрок это сделал.
/// Нужная кнопка или здание подсвечивается мигающей рамкой и стрелкой.
/// Пройденный шаг запоминается (PlayerPrefs), обучение можно пропустить и пройти заново: ⚙ Настройки → Общие.
/// Создаётся само, добавлять в сцену не нужно.
/// </summary>
public class Tutorial : MonoBehaviour
{
    const string KStep = "tutorial_v1_step", KDone = "tutorial_v1_done";
    const float CardW = 380f;

    static Tutorial instance;
    int step;
    bool done;
    Rect cardRect;
    bool cardShown;

    // Что было в начале шага (чтобы заметить, что игрок сделал действие)
    Vector3 camPos0;
    float camSize0;
    int houses0;
    float stepTime;

    enum Target { None, Camera, Hall, TopBar, BuildButton, Production, Clock, CityButtons, Settings }

    class Step
    {
        public string title, text, waitText;
        public string phoneText, phoneWait;   // на телефоне - про пальцы, а не про мышь и клавиши
        public string Text => UiKit.Phone && phoneText != null ? phoneText : text;
        public string Wait => UiKit.Phone && phoneWait != null ? phoneWait : waitText;
        public Target target;
        public bool action;   // шаг засчитывается сам, когда игрок сделал действие
    }

    static readonly Step[] Steps =
    {
        new Step { title = "Добро пожаловать!", target = Target.None,
            text = "Это ваш город. Здесь вы строите дома и мастерские, кормите жителей, растите армию и защищаетесь от набегов.\n\nКороткое обучение займёт пару минут. Его можно пропустить и пройти позже: ⚙ → Общие." },
        new Step { title = "Камера", target = Target.Camera, action = true,
            text = "Двигайте карту: клавиши WASD или стрелки, либо зажмите правую кнопку мыши и тяните.\nКолесо мыши приближает и отдаляет.",
            waitText = "Сдвиньте или приблизьте карту",
            phoneText = "Ведите пальцем по карте — она двигается.\nДва пальца: развести — приблизить, свести — отдалить.",
            phoneWait = "Сдвиньте или приблизьте карту" },
        new Step { title = "Цитадель", target = Target.Hall, action = true,
            text = "Большое здание в центре — Цитадель, сердце города. В ней склад: доски, плиты, сталь и всё, что вы купили или добыли. От её уровня зависят лимиты казны.\n\nНажмите на Цитадель левой кнопкой.",
            waitText = "Нажмите на Цитадель",
            phoneText = "Большое здание в центре — Цитадель, сердце города. В ней склад: доски, плиты, сталь и всё, что вы купили или добыли. От её уровня зависят лимиты казны.\n\nНажмите на Цитадель." },
        new Step { title = "Казна", target = Target.TopBar,
            text = "Слева сверху — казна: золото, дерево, камень и еда, под ними жители. Наведите мышь на строку, чтобы увидеть остальные запасы.\n\nДерево даёт Лесопилка, камень и немного золота — Шахта, еду — Ферма. Золото ещё приносят набеги и рынок. Подсказка у каждого ресурса говорит, где его взять.",
            phoneText = "Слева сверху — казна: золото, дерево, камень и еда, под ними жители. Подержите палец на строке — появится подсказка с остальными запасами.\n\nДерево даёт Лесопилка, камень и немного золота — Шахта, еду — Ферма. Золото ещё приносят набеги и рынок. Подсказка у каждого ресурса говорит, где его взять." },
        new Step { title = "Ресурсы", target = Target.Production, action = true,
            text = "Сначала — ресурсы. Жители сами работают на Лесопилке (дерево), в Шахте (камень и немного золота) и на Ферме (еда), а готовое носят в Цитадель. Нажмите на любое из этих зданий: в окне видно, сколько рабочих на месте, кнопки − и + меняют их число.\n\nЕсли еды мало, жители голодают и работают хуже — следите за Фермой.",
            waitText = "Нажмите на Лесопилку, Шахту или Ферму" },
        new Step { title = "Первая постройка", target = Target.BuildButton, action = true,
            text = "Когда хватает дерева и золота — стройте. Нажмите «Постройки», выберите Жилой дом и поставьте его на свободную траву левой кнопкой. Правая кнопка или Esc — отмена.\n\nВ домах живут жители: чем больше домов, тем больше рабочих рук. Цена здания видна в списке построек; чего не хватает — подсвечено.",
            waitText = "Поставьте Жилой дом",
            phoneText = "Когда хватает дерева и золота — стройте. Нажмите «Постройки» и выберите Жилой дом. Ведите пальцем — дом едет следом, потом нажмите «Поставить».\n\nВ домах живут жители: чем больше домов, тем больше рабочих рук." },
        new Step { title = "День и ночь", target = Target.Clock,
            text = "Наверху справа — часы и мана (✦). День сменяется ночью: ночью на производстве остаётся только дежурная смена, остальные спят дома. Выходных нет: работают каждый день." },
        new Step { title = "Город и Путь", target = Target.CityButtons,
            text = "Справа сверху: «Город» — сводка по городу и сбор готовых ресурсов, «Путь» — выбор направления развития, когда Цитадель подрастёт. «Клан» — общий чат и войны вместе с другими игроками." },
        new Step { title = "Защита", target = Target.None,
            text = "Иногда на город нападают. Постройте Казарму и обучите бойцов, ставьте стены и башни. Когда бойцы готовы, кнопка «В бой!» справа снизу отправит их в набег за добычей." },
        new Step { title = "Готово!", target = Target.Settings,
            text = "Шестерёнка ⚙ справа сверху — настройки, язык и журнал всех сообщений. Там же можно пройти это обучение заново.\n\nУдачи, правитель!" },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("Tutorial");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Tutorial>();
    }

    void Awake()
    {
        done = PlayerPrefs.GetInt(KDone, 0) == 1;
        step = Mathf.Clamp(PlayerPrefs.GetInt(KStep, 0), 0, Steps.Length - 1);
        stepTime = -1f;
    }

    /// <summary>Пройти обучение заново (кнопка в настройках).</summary>
    public static void Restart()
    {
        if (instance == null) Boot();
        instance.done = false;
        instance.Go(0);
        PlayerPrefs.SetInt(KDone, 0);
        PlayerPrefs.Save();
    }

    /// <summary>Обучение сейчас на экране.</summary>
    public static bool Active => instance != null && instance.cardShown;

    /// <summary>Мышь над карточкой обучения (карта под ней не кликается).</summary>
    public static bool IsPointerOver(Vector2 guiPoint) { return Active && instance.cardRect.Contains(guiPoint); }

    bool CanShow()
    {
        if (done) return false;
        if (BuildingPlacer.Instance == null || !BuildingPlacer.Instance.WorldReady) return false;
        if (AccountManager.Blocking || RaidManager.ViewingBattle || WarView.IsOpen) return false;
        return true;
    }

    void Go(int s)
    {
        step = Mathf.Clamp(s, 0, Steps.Length - 1);
        PlayerPrefs.SetInt(KStep, step);
        PlayerPrefs.Save();
        stepTime = -1f;   // начальное состояние снимем в следующем кадре
    }

    void Finish()
    {
        done = true;
        cardShown = false;
        PlayerPrefs.SetInt(KDone, 1);
        PlayerPrefs.DeleteKey(KStep);
        PlayerPrefs.Save();
    }

    void Update()
    {
        if (!CanShow()) { cardShown = false; return; }
        Camera cam = Camera.main;
        if (stepTime < 0f)
        {
            // Запоминаем, как было в начале шага
            stepTime = 0f;
            if (cam != null) { camPos0 = cam.transform.position; camSize0 = cam.orthographicSize; }
            houses0 = Count(PopulationManager.HouseName);
        }
        stepTime += Time.unscaledDeltaTime;
        if (stepTime < 0.6f || !Steps[step].action) return;   // не перескакивать шаг сразу

        if (Achieved(Steps[step].target, cam)) Go(step + 1);
    }

    bool Achieved(Target t, Camera cam)
    {
        BuildingInstance sel = BuildingPlacer.Selected;
        switch (t)
        {
            case Target.Camera:
                if (cam == null) return true;
                return (cam.transform.position - camPos0).magnitude > 2.5f || Mathf.Abs(cam.orthographicSize - camSize0) > 1f;
            case Target.Hall:
                return sel != null && IsHall(sel);
            case Target.BuildButton:
                return Count(PopulationManager.HouseName) > houses0;
            case Target.Production:
                return sel != null && sel.type != null && IsProduction(sel.type.name);
        }
        return false;
    }

    static bool IsHall(BuildingInstance b) { return b.type == null || b.type.name == "Ратуша"; }

    static bool IsProduction(string n)
    {
        return n == BuildingInstance.SawmillName || n == BuildingInstance.MineName || n == BuildingInstance.FarmName;
    }

    static int Count(string typeName)
    {
        int n = 0;
        foreach (var b in BuildingInstance.All)
            if (b != null && b.type != null && b.type.name == typeName) n++;
        return n;
    }

    // ---------- Рисование ----------

    void OnGUI()
    {
        if (!CanShow()) { cardShown = false; return; }
        GUI.depth = -2000;   // поверх окон игры
        UiKit.Apply();

        Step s = Steps[step];
        DrawHighlight(s.target);
        DrawCard(s);
    }

    void DrawCard(Step s)
    {
        float textW = CardW - UiKit.Pad * 2f;
        string body = s.Text;
        float textH = UiKit.TextH(body, textW);
        float waitH = s.action ? 24f : 0f;
        float h = UiKit.WindowH(textH + 8f + waitH + 14f + UiKit.BtnH);
        cardRect = new Rect(UiKit.W - CardW - 12f, UiKit.H - h - 14f, CardW, h);
        cardShown = true;

        // Мягкое появление карточки на каждом шаге
        float k = Mathf.Clamp01(Mathf.Max(0f, stepTime) / 0.25f);
        Rect r = cardRect;
        r.y += (1f - k) * 18f;
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.3f, 1f, k));

        string caption = $"{s.title}  {UiKit.Col(UiKit.MutedHex, $"· {step + 1}/{Steps.Length}")}";
        Rect c = UiKit.Window(r, caption, out bool close);
        if (close) { GUI.color = old; Finish(); return; }

        float y = c.y;
        LocGUI.Label(new Rect(c.x, y, textW, textH), body, UiKit.Body);
        y += textH + 8f;

        if (s.action)
        {
            // Чего ждём: мигающая точка и подсказка
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            Color oc = GUI.color;
            GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, (0.4f + 0.6f * blink) * oc.a);
            GUI.DrawTexture(new Rect(c.x + 2f, y + 7f, 8f, 8f), Texture2D.whiteTexture);
            GUI.color = oc;
            LocGUI.Label(new Rect(c.x + 16f, y, textW - 16f, 22f), UiKit.Col(UiKit.AccentHex, s.Wait), UiKit.Body);
            y += waitH;
        }

        // Точки прогресса
        float dotsW = Steps.Length * 12f;
        for (int i = 0; i < Steps.Length; i++)
        {
            Color oc = GUI.color;
            GUI.color = i < step ? new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, oc.a)
                      : i == step ? new Color(1f, 1f, 1f, oc.a) : new Color(1f, 1f, 1f, 0.25f * oc.a);
            GUI.DrawTexture(new Rect(c.x + i * 12f, y + 3f, 7f, 7f), Texture2D.whiteTexture);
            GUI.color = oc;
        }
        y += 14f;

        float bw = 130f;
        Rect next = new Rect(c.xMax - bw, y, bw, UiKit.BtnH);
        Rect back = new Rect(c.x, y, 90f, UiKit.BtnH);
        if (step > 0 && UiKit.Button(back, "Назад", UiKit.Kind.Ghost)) Go(step - 1);
        bool last = step == Steps.Length - 1;
        if (s.action)
        {
            if (UiKit.Button(next, "Пропустить шаг", UiKit.Kind.Normal)) Go(step + 1);
        }
        else if (UiKit.Button(next, step == 0 ? "Начать" : last ? "Играть!" : "Далее", UiKit.Kind.Primary))
        {
            if (last) Finish(); else Go(step + 1);
        }
        if (step == 0)
        {
            Rect skip = new Rect(next.x - 140f, y, 134f, UiKit.BtnH);
            if (UiKit.Button(skip, "Пропустить всё", UiKit.Kind.Ghost)) Finish();
        }
        GUI.color = old;
        UiKit.Tip(new Rect(r.xMax - 30f, r.y, 30f, UiKit.TitleH), "Закончить обучение. Пройти заново можно в ⚙ → Общие.");
    }

    // ---------- Подсветка ----------

    void DrawHighlight(Target t)
    {
        switch (t)
        {
            case Target.TopBar: Frame(new Rect(2f, 2f, ResourceManager.HudRight > 0f ? ResourceManager.HudRight : 470f, ResourceManager.HudH), true); break;
            case Target.BuildButton:
                if (!BuildingPlacer.PlacingNow) Frame(new Rect(10f, 45f, 196f, 40f), true);
                break;
            case Target.Clock:
                if (GameClock.Instance != null && !GameClock.Instance.IsHidden) Frame(GameClock.Instance.PanelRect, false);
                break;
            case Target.CityButtons: Frame(new Rect(DevelopmentPath.ButtonLeft - 2f, UiKit.TopBtnY - 2f, UiKit.W - 46f - DevelopmentPath.ButtonLeft, UiKit.TopBtnH + 4f), true); break;
            case Target.Settings: Frame(AccountManager.SystemButtonRect, true); break;
            case Target.Hall:
                foreach (var b in BuildingInstance.All)
                    if (b != null && IsHall(b)) { WorldArrow(b); break; }
                break;
            case Target.Production:
                {
                    // Стрелка над ближайшим к центру экрана производством
                    Camera cam = Camera.main;
                    BuildingInstance best = null;
                    float bestD = float.MaxValue;
                    if (cam != null)
                        foreach (var b in BuildingInstance.All)
                        {
                            if (b == null || b.type == null || !IsProduction(b.type.name)) continue;
                            float d = ((Vector2)(b.transform.position - cam.transform.position)).sqrMagnitude;
                            if (d < bestD) { bestD = d; best = b; }
                        }
                    if (best != null) WorldArrow(best);
                }
                break;
        }
    }

    /// <summary>Мигающая золотая рамка вокруг кнопки; arrowBelow - стрелка под ней (иначе над ней).</summary>
    static void Frame(Rect r, bool arrowBelow)
    {
        float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        float grow = 2f + p * 3f;
        Rect o = new Rect(r.x - grow, r.y - grow, r.width + grow * 2f, r.height + grow * 2f);
        Color old = GUI.color;
        GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.55f + 0.45f * p);
        float t = 2f;
        GUI.DrawTexture(new Rect(o.x, o.y, o.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(o.x, o.yMax - t, o.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(o.x, o.y, t, o.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(o.xMax - t, o.y, t, o.height), Texture2D.whiteTexture);
        GUI.color = old;
        Arrow(new Vector2(o.center.x, arrowBelow ? o.yMax + 4f + p * 6f : o.y - 4f - p * 6f), !arrowBelow);
    }

    static GUIStyle arrowStyle;

    /// <summary>Стрелка ▲ (или ▼, если down) остриём в точку tip.</summary>
    static void Arrow(Vector2 tip, bool down)
    {
        if (arrowStyle == null)
        {
            arrowStyle = new GUIStyle(UiKit.Body) { fontSize = 26, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true };
        }
        Rect r = down ? new Rect(tip.x - 20f, tip.y - 32f, 40f, 32f) : new Rect(tip.x - 20f, tip.y, 40f, 32f);
        string a = down ? "▼" : "▲";
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f * old.a);
        GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), a, arrowStyle);
        GUI.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, old.a);
        GUI.Label(r, a, arrowStyle);
        GUI.color = old;
    }

    /// <summary>Прыгающая стрелка над зданием на карте.</summary>
    static void WorldArrow(BuildingInstance b)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        SpriteRenderer sr = b.GetComponentInChildren<SpriteRenderer>();
        Vector3 top = sr != null ? new Vector3(sr.bounds.center.x, sr.bounds.max.y, 0f) : b.transform.position;
        Vector3 sp = cam.WorldToScreenPoint(top);
        if (sp.z < 0f) return;
        float bounce = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.5f)) * 10f;
        Vector2 gp = new Vector2(UiKit.ToGui(sp).x, UiKit.ToGui(sp).y - 4f - bounce);
        // За краем экрана - стрелка у края, указывает в сторону здания
        gp.x = Mathf.Clamp(gp.x, 24f, UiKit.W - 24f);
        gp.y = Mathf.Clamp(gp.y, 40f, UiKit.H - 10f);
        Arrow(gp, true);
    }
}
