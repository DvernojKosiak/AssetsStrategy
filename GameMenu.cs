using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;

/// <summary>
/// Общее меню города: маленькая кнопка в правом верхнем углу (или клавиша F).
/// По нажатию открывается окно: сверху кнопка "Собрать всё" (курьеры забирают добычу со всех зданий),
/// ниже вкладки (сейчас - "Рационы", её добавляет CityConsumption). Новые вкладки:
///     GameMenu.AddPage("Название", ширина => высота, rect => { ...рисуем внутри rect... });
/// При старте меню закрыто.
/// </summary>
public class GameMenu : MonoBehaviour
{
    public static GameMenu Instance { get; private set; }

    class Page
    {
        public string title;
        public Func<float, float> height;   // по ширине содержимого - его высота
        public Action<Rect> draw;
    }

    readonly List<Page> pages = new List<Page>();
    bool open = false;      // при старте закрыто
    int current = 0;

    const float PanelWidth = 300f;
    const float TabsHeight = 26f;
    const float CollectHeight = 28f;

    public bool IsOpen => open;

    public static GameMenu Ensure()
    {
        if (Instance == null) new GameObject("GameMenu").AddComponent<GameMenu>();
        return Instance;
    }

    /// <summary>Добавить вкладку в меню.</summary>
    public static void AddPage(string title, Func<float, float> height, Action<Rect> draw)
    {
        GameMenu m = Ensure();
        m.pages.RemoveAll(p => p.title == title); // повторная регистрация заменяет старую
        m.pages.Add(new Page { title = title, height = height, draw = draw });
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Тревога (WarManager.RaiseAlarm): свернуть окно, чтобы было видно бой
    void OnEnable() { WarManager.OnAlarm += CollapseForAlarm; }
    void OnDisable() { WarManager.OnAlarm -= CollapseForAlarm; }
    bool CollapseForAlarm()
    {
        if (!open) return false;
        open = false;
        return true;
    }

    void Update()
    {
        if (Keyboard.current != null && !UiKit.Typing && !WarView.IsOpen && Keyboard.current.fKey.wasPressedThisFrame) Toggle();
    }

    public void Toggle()
    {
        open = !open;
        if (open) UiKit.Front(FrontId);   // настройки, клан, путь и т.п. закроются - не лягут поверх меню
    }

    const string FrontId = "city";

    // ---------- Собрать всё ----------

    static bool Collectable(BuildingInstance b)
    {
        return b != null && b.type != null && b.isActiveAndEnabled && !b.IsTownHall && !b.IsRoad
            && !b.IsRuined && !b.courierOnTheWay && b.StoredTotal > 0;
    }

    static void CountCollectable(out int buildings, out int items)
    {
        buildings = 0;
        items = 0;
        foreach (var b in BuildingInstance.All)
        {
            if (!Collectable(b)) continue;
            buildings++;
            items += b.StoredTotal;
        }
    }

    // Курьеры идут ко всем зданиям, где что-то накопилось (как "Собрать со всех", только для всех типов)
    public static void CollectAll()
    {
        var pm = PopulationManager.Instance;
        var list = new List<BuildingInstance>(BuildingInstance.All);
        int sent = 0, noOne = 0;

        foreach (var b in list)
        {
            if (!Collectable(b)) continue;
            if (pm == null || pm.Hall == null) { b.CollectToTreasury(); sent++; }
            else if (pm.DispatchCourier(b)) sent++;
            else noOne++;
        }

        if (sent == 0 && noOne == 0) WarManager.Notify("Собирать пока нечего", false);
        else if (noOne > 0) WarManager.Notify($"Курьеры отправлены: {sent}. Нет свободных жителей для зданий: {noOne}", sent > 0);
        else WarManager.Notify($"Курьеры отправлены к зданиям: {sent}", true);
    }

    // ---------- Размеры ----------

    Rect ButtonRect => new Rect(UiKit.W - (UiKit.Phone ? 154f : 150f), UiKit.TopBtnY, 100f, UiKit.TopBtnH);   // левее шестерёнки настроек

    float ContentWidth => PanelWidth - UiKit.Pad * 2f;

    // Снизу справа - «В бой!» и над ним малые кнопки («Ловушки», «Заслоны»): окно до них не достаёт
    const float PanelTop = 40f, BottomFree = 96f;

    // Высота всего, что под кнопкой «Собрать всё» (режим накопления, зоны, вкладки, страница) при ширине w
    float BodyHeight(float w)
    {
        float h = AccumulationMode.RowHeight + 4f;   // режим накопления
        h += 24f;   // галочка зон ресурсов
        if (ResourceZones.Show) h += ResourceZones.LegendHeight + 2f;
        if (ReplaysShown) h += UiKit.BtnH + 6f;   // повторы набегов (обновление 12)
        if (pages.Count > 1) h += TabsHeight + 6f;
        if (pages.Count > 0)
        {
            current = Mathf.Clamp(current, 0, pages.Count - 1);
            h += pages[current].height(w) + 8f;
        }
        else h += 24f;
        return h;
    }

    // Обновление 13: кнопка повторов видна всегда (раньше - только когда записи уже есть, и её не могли найти)
    static bool ReplaysShown => !WarCityMode.Active && AccountManager.LoggedIn && !AccountManager.IsOffline;

    float MaxPanelH => Mathf.Max(220f, UiKit.H - PanelTop - BottomFree);

    // Всё влезает без прокрутки
    bool Fits => UiKit.TitleH + 6f + CollectHeight + 6f + BodyHeight(ContentWidth) <= MaxPanelH;

    Rect PanelRect
    {
        get
        {
            float h = UiKit.TitleH + 6f + CollectHeight + 6f + BodyHeight(ContentWidth);
            // Не влезает по высоте - окно до низа свободного места, а содержимое прокручивается (раньше уезжало вниз за окно)
            h = Mathf.Min(h, MaxPanelH);
            return new Rect(UiKit.W - PanelWidth - 10f, PanelTop, PanelWidth, h);
        }
    }

    // guiPoint: координаты мыши в системе OnGUI (Y сверху вниз)
    public bool IsPointerOver(Vector2 guiPoint)
    {
        return ButtonRect.Contains(guiPoint) || (open && PanelRect.Contains(guiPoint));
    }

    // ---------- Отрисовка ----------

    void OnGUI()
    {
        if (WarView.IsOpen) return;   // открыта карта клановой войны
        UiKit.Apply();
        if (open && UiKit.Behind(FrontId)) open = false;   // открыли другое большое окно
        if (UiKit.Button(ButtonRect, open ? "Город ▴" : "≡ Город", open ? UiKit.Kind.Primary : UiKit.Kind.Normal,
                "Меню города (F): собрать всё, режим накопления, зоны ресурсов, рационы")) Toggle();

        if (!open) return;

        Rect p = PanelRect;
        string head = pages.Count == 1 ? pages[0].title : "Город";
        Rect c = UiKit.Window(p, head, out bool close);
        if (close) { open = false; return; }

        float x = c.x, w = c.width, y = c.y;

        // Главное действие: «Собрать всё»
        CountCollectable(out int bCount, out int items);
        string collectLabel = bCount > 0 ? $"Собрать всё  ·  {items} ед." : "Собрать всё";
        string collectTip = bCount > 0
            ? $"Курьеры заберут добычу со всех зданий: {bCount} зд., {items} ед."
            : "Собирать пока нечего: склады зданий пусты";
        if (UiKit.Button(new Rect(x, y, w, CollectHeight), collectLabel, bCount > 0, UiKit.Kind.Primary, collectTip)) CollectAll();
        y += CollectHeight + 6f;

        // Ниже - прокрутка, если окно не влезает по высоте
        Rect view = new Rect(x, y, w, p.yMax - y);
        float bw = Fits ? w : w - UiKit.ScrollBarW;
        UiKit.Scroll("gamemenu", view, BodyHeight(bw), r => DrawBody(r));
    }

    void DrawBody(Rect area)
    {
        float x = area.x, w = area.width, y = area.y;

        // Режим накопления
        AccumulationMode.DrawRow(new Rect(x, y, w, AccumulationMode.RowHeight));
        y += AccumulationMode.RowHeight + 4f;

        // Зоны ресурсов
        Rect tr = new Rect(x, y, w, 22f);
        ResourceZones.Show = LocGUI.Toggle(tr, ResourceZones.Show, " Зоны ресурсов на карте");
        UiKit.Tip(tr, "Где шахта, ферма и лесопилка дают больше. Подсвечиваются и сами, когда здание в руке.");
        y += 24f;
        if (ResourceZones.Show)
        {
            ResourceZones.DrawLegend(new Rect(x, y, w, ResourceZones.LegendHeight));
            y += ResourceZones.LegendHeight + 2f;
        }

        // Обновление 12: повторы набегов на ваш город - и отсюда, не только из окна аккаунта
        if (ReplaysShown)
        {
            if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), ReplayViewer.Offers.Count > 0 ? $"▶ Повторы набегов на вас ({ReplayViewer.Offers.Count})" : "▶ Повторы набегов на вас", UiKit.Kind.Normal,
                    "Посмотреть, как шли последние набеги на ваш город (записи хранятся неделю)"))
            { open = false; ReplayViewer.OpenList(); return; }
            y += UiKit.BtnH + 6f;
        }

        if (pages.Count == 0)
        {
            LocGUI.Label(new Rect(x, y, w, 22f), "Пока пусто", UiKit.MutedStyle);
            return;
        }

        // Вкладки (видны, когда страниц больше одной)
        if (pages.Count > 1)
        {
            string[] names = new string[pages.Count];
            for (int i = 0; i < pages.Count; i++) names[i] = pages[i].title;
            current = UiKit.Tabs(new Rect(x, y, w + 2f, TabsHeight), names, Mathf.Clamp(current, 0, pages.Count - 1));
            y += TabsHeight + 6f;
        }

        Page page = pages[Mathf.Clamp(current, 0, pages.Count - 1)];
        page.draw(new Rect(x, y, w, page.height(w)));
    }
}
