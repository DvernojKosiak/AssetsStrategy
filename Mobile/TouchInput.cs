using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Сенсорное управление для телефона. Вся игра читает мышь и клавиатуру (Mouse.current, Keyboard.current),
/// поэтому здесь заводятся две виртуальные «железки» - мышь TouchMouse и клавиатура TouchKeys,
/// а жесты пальцами превращаются в их нажатия. Игровые скрипты ничего не знают о касаниях
/// и одинаково работают на компьютере и на телефоне; новые функции с мышью тоже заработают сами.
///
/// Жесты:
///  - нажать - щелчок левой кнопкой (по кнопкам окон жмёт сам интерфейс, щелчок в карту не уходит);
///  - провести одним пальцем - двигать карту (как средняя кнопка);
///  - два пальца - двигать карту, развести/свести - плавно приблизить/отдалить (точка между пальцами стоит на месте);
///  - удерживать в городе - меню «Перенести / Снести» (клавиши M и X), на карте войны - приказ (правая кнопка);
///  - пока в руках здание, дозор, ловушка или переправа: палец двигает призрак, «✓ Поставить»
///    или двойное нажатие - поставить;
///  - провести по окну - листать список (окно едет ровно за пальцем);
///  - подержать палец на кнопке - подсказка к ней (кнопка при этом не нажмётся);
///  - кнопка «Назад» телефона - Esc (закрыть окно, отменить стройку).
/// Кнопки на экране вместо клавиш рисует TouchControls.
/// </summary>
[DefaultExecutionOrder(-30000)]
public class TouchInput : MonoBehaviour
{
    public static TouchInput Instance { get; private set; }

    /// <summary>Сенсорное управление сейчас включено.</summary>
    public static bool Active => Instance != null && Instance.active;
    /// <summary>Сейчас на экране есть палец.</summary>
    public static bool FingerDown => Instance != null && Instance.active && Instance.g != G.Idle;
    /// <summary>Палец листает окно - OnGUI превращает его движение в прокрутку колесом.</summary>
    public static bool GuiScrolling => Instance != null && Instance.active && Instance.g == G.UiScroll;

    /// <summary>Номер касания (растёт с каждым новым пальцем) и где оно началось (пиксели экрана, y снизу).</summary>
    public static int GestureId => Instance != null ? Instance.gestureId : 0;
    public static Vector2 StartPx => Instance != null ? Instance.startPos : Vector2.zero;

    /// <summary>Насколько сдвинулся палец, листающий окно, в этом кадре (точки окон, вниз - плюс).</summary>
    public static float ScrollDragGui => Instance != null && Instance.active && Instance.g == G.UiScroll && Instance.dragFrame == Time.frameCount ? Instance.dragGui : 0f;

    /// <summary>Два пальца: во сколько раз приблизить карту в этом кадре (1 - не трогать) и вокруг какой точки (пиксели экрана).</summary>
    public static float PinchFactor => Instance != null && Instance.active && Instance.pinchFrame == Time.frameCount ? Instance.pinchFactor : 1f;
    public static Vector2 PinchCenter => Instance != null ? Instance.pinchMid : Vector2.zero;

    /// <summary>Палец стоит на кнопке или окне дольше полсекунды - показываем подсказку (и кнопка не нажмётся).</summary>
    public static bool HoldingOnUi => Instance != null && Instance.active && Instance.g == G.Pending && Instance.overUi
                                      && Time.unscaledTime - Instance.startTime >= LongPressSec;

    // ---------- Пороги (в миллиметрах экрана) ----------
    const float SlopMm = 2.5f;          // сдвиг пальца меньше этого - ещё нажатие, а не перетаскивание
    const float LongPressSec = 0.5f;    // удержание - меню здания / приказ
    const float DoubleTapSec = 0.45f;   // второе нажатие за это время - двойное
    const float DoubleTapMm = 7f;

    /// <summary>Двойное нажатие по карте (марш отряда, final-9): второе нажатие не позже этого, секунд.
    /// На телефоне палец медленнее мыши - даём больше времени.</summary>
    // final-13: марш включался сам, когда игрок быстро давал два приказа рядом (бойцы «не слушались»: шли напролом
    // и не дрались). Окно стало короче и уже: 0,38 с и 6 мм.
    public static float DoubleSeconds => Active || Application.isMobilePlatform ? 0.38f : 0.35f;
    /// <summary>... и не дальше этого от первого, пикселей экрана. Раньше было 30 пикселей - на телефоне это 2 мм,
    /// пальцем во второй раз так точно не попасть, и марш почти не включался.</summary>
    public static float DoublePixels => Active || Application.isMobilePlatform ? Mathf.Max(30f, 6f * TouchSettings.PxPerMm) : 30f;

    enum G { Idle, Pending, Pan, Cursor, Paint, Box, UiScroll, UiOther, Pinch, Done }

    bool active, touchSeen;
    Mouse mouse;
    Keyboard keys;

    // Жест
    G g = G.Idle;
    int fingerId = -1, downFrame;
    Vector2 startPos, lastPos;
    int gestureId;
    float startTime;
    bool overUi;
    float pinchDist, pinchFactor = 1f;
    int pinchFrame = -1, dragFrame = -1;
    Vector2 pinchMid;
    float dragGui;
    Vector2 savedCursor;
    bool cursorSaved;

    // Призрак (здание в руках и т.п.)
    bool wasGhost;
    int keepCursorUntil = -1;   // «Перенести» из меню: призрак появится там, где здание, а не в центре
    Vector2 lastGhostTap = new Vector2(-9999f, -9999f);
    float lastGhostTapTime = -10f;

    // Что отправить виртуальным устройствам
    Vector2 pos;
    bool leftHeld, rightHeld, middleHeld;
    bool leftPulse, rightPulse;
    float scrollPulse;
    bool backWasDown;
    readonly List<Key> keyPulse = new List<Key>();
    readonly List<Key> keysNow = new List<Key>();

    // Что отправили в прошлый раз
    Vector2 sentPos;
    int sentButtons = -1;
    float sentScroll;
    readonly List<Key> sentKeys = new List<Key>();

    // Касание началось и закончилось в одном кадре - решаем через пару кадров, когда интерфейс его увидит
    Vector2 quickTapPos;
    int quickTapFrame = -1;

    // Касания на некоторых телефонах приходят в родном разрешении, а игра рисуется в уменьшенном
    bool nativeTouchCoords;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; crossingField = scoutMapField = null; crossingLooked = scoutMapLooked = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("TouchInput");
        DontDestroyOnLoad(go);
        go.AddComponent<TouchInput>();
        go.AddComponent<TouchControls>();
        go.AddComponent<TouchGuiProbe>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SetActive(false);
    }

    void SetActive(bool on)
    {
        if (on == active) return;
        active = on;
        if (on)
        {
            mouse = InputSystem.AddDevice<Mouse>("TouchMouse");
            keys = InputSystem.AddDevice<Keyboard>("TouchKeys");
            pos = sentPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);   // пиксели экрана
            sentButtons = -1;
            g = G.Idle;
        }
        else
        {
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keys != null && keys.added) InputSystem.RemoveDevice(keys);
            mouse = null;
            keys = null;
            g = G.Idle;
            leftHeld = rightHeld = middleHeld = false;
        }
    }

    // ---------- Что сейчас происходит в игре ----------

    static FieldInfo crossingField, scoutMapField;
    static bool crossingLooked, scoutMapLooked;

    /// <summary>Открыта карта разведчика в набеге: её двигают пальцем, листать её нельзя.</summary>
    public static bool ScoutMapOpen
    {
        get
        {
            if (!scoutMapLooked)
            {
                scoutMapLooked = true;
                scoutMapField = typeof(RaidManager).GetField("mapShown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            var m = RaidManager.Instance;
            return m != null && scoutMapField != null && scoutMapField.GetValue(m) != null;
        }
    }

    /// <summary>Ставят переправу (поле закрытое - читаем без правки чужого файла).</summary>
    public static bool CrossingPlacing
    {
        get
        {
            if (!crossingLooked)
            {
                crossingLooked = true;
                crossingField = typeof(CrossingManager).GetField("placing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            var m = CrossingManager.Instance;
            return m != null && crossingField != null && crossingField.FieldType == typeof(bool) && (bool)crossingField.GetValue(m);
        }
    }

    /// <summary>В руках что-то с призраком: здание (новое или переносимое), дозор, ловушка, переправа, разведчик.</summary>
    public static bool Ghost => BuildingPlacer.PlacingNow || FieldPostManager.Placing || TrapManager.Placing || CamoScouts.Placing || CrossingPlacing;

    public static bool WarMap => WarView.IsOpen || WarView.Embedded;

    // ---------- Команды от кнопок на экране ----------

    /// <summary>Нажать клавишу (один кадр). Можно несколько сразу: Ctrl + A.</summary>
    public void PressKeys(params Key[] k) { keyPulse.AddRange(k); }

    /// <summary>Щелчок левой кнопкой там, где сейчас курсор (призрак).</summary>
    public void ClickAtCursor() { leftPulse = true; }

    /// <summary>Поставить курсор в точку экрана (координаты Input System, y снизу) и нажать клавишу.</summary>
    public void KeyAt(Vector2 screen, Key k, bool keepCursorForGhost)
    {
        pos = screen;
        keyPulse.Add(k);
        if (keepCursorForGhost) keepCursorUntil = Time.frameCount + 10;
    }

    /// <summary>Поставить курсор (призрак здания) в точку экрана, не нажимая ничего: перенос из панели здания.</summary>
    public void PlaceCursor(Vector2 screen)
    {
        pos = new Vector2(Mathf.Clamp(screen.x, 0f, Screen.width), Mathf.Clamp(screen.y, 0f, Screen.height));
        keepCursorUntil = Time.frameCount + 10;
    }

    /// <summary>Щелчок левой кнопкой в точке экрана.</summary>
    public void ClickAt(Vector2 screen) { pos = screen; leftPulse = true; }

    /// <summary>Где сейчас виртуальный курсор (координаты Input System).</summary>
    public Vector2 CursorPos => pos;

    // ---------- Кадр ----------

    void Update()
    {
        if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        var touches = ETouch.activeTouches;
        if (touches.Count > 0) touchSeen = true;

        bool want = TouchSettings.Wanted(touchSeen);
        if (want != active) SetActive(want);
        if (!active || mouse == null) return;

        // Здание взяли в руки - курсор в центр экрана, чтобы призрак было видно (а не под окном построек)
        bool ghost = Ghost;
        if (ghost && !wasGhost && Time.frameCount > keepCursorUntil) pos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        wasGhost = ghost;

        Gesture(touches, ghost);
        BackButton();

        // Shift: «ставить ещё» на стройке, «добавить отряд» в набеге
        keysNow.Clear();
        if (TouchControls.Keep && (ghost || RaidManager.ViewingBattle)) keysNow.Add(Key.LeftShift);
        foreach (var k in keyPulse) if (!keysNow.Contains(k)) keysNow.Add(k);
        keyPulse.Clear();

        Send();
    }

    Vector2 Fix(Vector2 p)
    {
        if (!nativeTouchCoords && (p.x > Screen.width + 2f || p.y > Screen.height + 2f) && MobilePlatform.RenderScale < 0.999f)
            nativeTouchCoords = true;
        return nativeTouchCoords ? p * MobilePlatform.RenderScale : p;
    }

    /// <summary>Точка экрана → точка окон (с учётом масштаба интерфейса и выреза камеры).</summary>
    static Vector2 Gui(Vector2 screen) { return UiKit.ToGui(screen); }

    // Кнопка «Назад» телефона. Обычно Input System сам отдаёт её как Esc клавиатуры телефона;
    // если такой клавиатуры нет (бывает на части прошивок) - берём из старого ввода и жмём Esc сами.
    void BackButton()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        bool down = false;
        try { down = Input.GetKey(KeyCode.Escape); } catch (System.Exception) { }
        if (down && !backWasDown && !NativeKeyboard()) keyPulse.Add(Key.Escape);
        backWasDown = down;
#endif
    }

    bool NativeKeyboard()
    {
        foreach (var d in InputSystem.devices)
            if (d is Keyboard && d != keys) return true;
        return false;
    }

    void Gesture(UnityEngine.InputSystem.Utilities.ReadOnlyArray<ETouch> touches, bool ghost)
    {
        float mm = TouchSettings.PxPerMm;

        // Пальцы на экране (те, что только что подняли, тоже в списке - с фазой Ended)
        int down = 0;
        ETouch a = default, b = default, mine = default;
        bool haveMine = false;
        foreach (var t in touches)
        {
            bool live = t.phase != UnityEngine.InputSystem.TouchPhase.Ended && t.phase != UnityEngine.InputSystem.TouchPhase.Canceled;
            if (t.touchId == fingerId) { mine = t; haveMine = true; }
            if (!live) continue;
            if (down == 0) a = t; else if (down == 1) b = t;
            down++;
        }

        // ---- Два пальца: двигать и приближать карту ----
        if (down >= 2)
        {
            Vector2 pa = Fix(a.screenPosition), pb = Fix(b.screenPosition);
            Vector2 mid = (pa + pb) * 0.5f;
            float dist = Mathf.Max(1f, (pa - pb).magnitude);
            if (g != G.Pinch)
            {
                if (g == G.Paint || g == G.Box) leftHeld = false;
                if (ghost && !cursorSaved) { savedCursor = pos; cursorSaved = true; }
                TouchControls.CloseMenu();
                g = G.Pinch;
                pinchDist = dist;
                pos = mid;
                middleHeld = true;   // два пальца ведут карту, как средняя кнопка мыши
                return;
            }
            pos = mid;
            // Приближение - плавно, ровно настолько, насколько развели пальцы
            float ratio = dist / pinchDist;
            pinchDist = dist;
            if (Mathf.Abs(ratio - 1f) > 0.0005f)
            {
                pinchFactor = Mathf.Clamp(ratio, 0.7f, 1.4f);
                pinchMid = mid;
                pinchFrame = Time.frameCount;
            }
            return;
        }

        if (g == G.Pinch)
        {
            // Один палец убрали - жест закончен, остальное ждём, пока поднимут все
            middleHeld = false;
            if (cursorSaved) { pos = savedCursor; cursorSaved = false; }
            g = down == 0 ? G.Idle : G.Done;
            if (down > 0) fingerId = a.touchId;
            return;
        }

        if (g == G.Done)
        {
            if (down == 0) g = G.Idle;
            return;
        }

        // ---- Один палец ----
        if (quickTapFrame >= 0 && Time.frameCount - quickTapFrame >= 2)
        {
            overUi = false;
            quickTapFrame = -1;
            Tap(quickTapPos, ghost);
        }

        if (g == G.Idle)
        {
            if (down == 0)
            {
                foreach (var t in touches)
                    if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended && t.touchId != fingerId && quickTapFrame < 0)
                    {
                        quickTapPos = Fix(t.screenPosition);
                        quickTapFrame = Time.frameCount;
                        if (!ghost) pos = quickTapPos;
                        fingerId = t.touchId;
                        break;
                    }
                return;
            }
            fingerId = a.touchId;
            startPos = lastPos = Fix(a.screenPosition);
            gestureId++;
            startTime = Time.unscaledTime;
            downFrame = Time.frameCount;
            overUi = false;
            if (TouchControls.TouchBegan(Gui(startPos))) { g = G.Done; return; }   // палец мимо открытого меню - меню закрылось
            g = G.Pending;
            if (!ghost) pos = startPos;   // наведение (подсветка того, что под пальцем)
            return;
        }

        if (!haveMine) { EndTouch(lastPos, ghost); return; }

        Vector2 p = Fix(mine.screenPosition);
        bool ended = mine.phase == UnityEngine.InputSystem.TouchPhase.Ended || mine.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
        Vector2 step = p - lastPos;
        lastPos = p;

        // Под пальцем окно или кнопка? Интерфейс узнаёт о касании в OnGUI, поэтому смотрим пару кадров
        if (g == G.Pending && Time.frameCount - downFrame <= 3 && TouchControls.UiAt(Gui(startPos), ghost)) overUi = true;
        if (g == G.Pending && !overUi && UiKit.Phone && TouchControls.UiAt(Gui(startPos), ghost) && Time.frameCount - downFrame <= 6) overUi = true;

        if (g == G.Pending && !ended)
        {
            Vector2 moved = p - startPos;
            if (moved.magnitude > SlopMm * mm)
            {
                if (overUi)
                {
                    g = Mathf.Abs(moved.y) >= Mathf.Abs(moved.x) * 0.5f ? G.UiScroll : G.UiOther;
                    if (UiKit.OnScrollbar(Gui(startPos))) g = G.UiOther;   // палец на ползунке - его и тянет
                    // Первый сдвиг (порог) тоже листает - иначе окно «прыгает» с места
                    if (g == G.UiScroll) { dragGui = -moved.y / UiKit.Scale; dragFrame = Time.frameCount; }
                }
                else if (ghost)
                {
                    g = TouchControls.Paint && BuildingPlacer.PlacingNow ? G.Paint : G.Cursor;
                    if (g == G.Paint) leftHeld = true;
                }
                else if (WarMap && TouchControls.BoxSelect)
                {
                    g = G.Box;
                    pos = startPos;   // рамка - от точки, где коснулись
                    leftHeld = true;
                    return;
                }
                else
                {
                    g = G.Pan;
                    pos = startPos;
                    middleHeld = true;
                    return;
                }
            }
            else if (!overUi && Time.unscaledTime - startTime >= LongPressSec)
            {
                LongPress(startPos, ghost);
                g = G.Done;
                return;
            }
        }

        switch (g)
        {
            case G.Pan:
            case G.Box:
                pos = p;
                break;
            case G.Cursor:
            case G.Paint:
                pos += step;   // призрак едет за пальцем, но не под ним - так его видно
                pos.x = Mathf.Clamp(pos.x, 0f, Screen.width);    // пиксели экрана
                pos.y = Mathf.Clamp(pos.y, 0f, Screen.height);
                break;
            case G.UiScroll:
                // Окна со списками листает OnGUI (TouchControls), окно построек - само (ScrollDragGui)
                CameraController.BlockZoom();
                if (dragFrame != Time.frameCount) { dragGui = 0f; dragFrame = Time.frameCount; }
                dragGui -= step.y / UiKit.Scale;   // экран: y вверх, окна: y вниз. Палец вверх - содержимое едет вверх, список листается к концу
                break;
        }

        if (ended) EndTouch(p, ghost);
    }

    void EndTouch(Vector2 p, bool ghost)
    {
        switch (g)
        {
            case G.Pending: Tap(p, ghost); break;
            case G.Pan: middleHeld = false; break;
            case G.Box:
            case G.Paint: leftHeld = false; break;
        }
        g = G.Idle;
        fingerId = -1;
    }

    void Tap(Vector2 p, bool ghost)
    {
        if (overUi || TouchControls.UiAt(Gui(p), ghost)) return;   // кнопку нажал сам интерфейс

        if (ghost)
        {
            float mm = TouchSettings.PxPerMm;
            bool twice = Time.unscaledTime - lastGhostTapTime <= DoubleTapSec && (p - lastGhostTap).magnitude <= DoubleTapMm * mm;
            pos = p;
            if (twice) { leftPulse = true; lastGhostTapTime = -10f; }
            else { lastGhostTap = p; lastGhostTapTime = Time.unscaledTime; }
            return;
        }

        pos = p;
        leftPulse = true;
    }

    void LongPress(Vector2 p, bool ghost)
    {
        if (ghost) return;
        if (WarMap) { pos = p; rightPulse = true; return; }   // приказ выбранным бойцам
        if (RaidManager.ViewingBattle) { pos = p; leftPulse = true; return; }
        int acts = BuildingPlacer.ActionsAtScreen(p);
        if (acts == 0) return;   // трава или Цитадель - переносить и сносить нечего
        TouchControls.OpenMenu(p, acts);
    }

    // ---------- Отправка в виртуальные устройства ----------

    void Send()
    {
        bool l = leftHeld || leftPulse, r = rightHeld || rightPulse, m = middleHeld;
        int buttons = (l ? 1 : 0) | (r ? 2 : 0) | (m ? 4 : 0);
        float scroll = scrollPulse;
        leftPulse = rightPulse = false;
        scrollPulse = 0f;

        bool keysChanged = keysNow.Count != sentKeys.Count;
        if (!keysChanged) foreach (var k in keysNow) if (!sentKeys.Contains(k)) { keysChanged = true; break; }
        if (keysChanged && keys != null)
        {
            InputSystem.QueueStateEvent(keys, new KeyboardState(keysNow.ToArray()));
            sentKeys.Clear();
            sentKeys.AddRange(keysNow);
        }

        if (pos == sentPos && buttons == sentButtons && scroll == 0f && sentScroll == 0f) return;
        var st = new MouseState { position = pos, delta = pos - sentPos, scroll = new Vector2(0f, scroll) }
            .WithButton(MouseButton.Left, l)
            .WithButton(MouseButton.Right, r)
            .WithButton(MouseButton.Middle, m);
        InputSystem.QueueStateEvent(mouse, st);
        sentPos = pos;
        sentButtons = buttons;
        sentScroll = scroll;
    }
}
