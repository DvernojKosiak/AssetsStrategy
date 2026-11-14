using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Общий вопрос «Точно?» поверх всех окон: выгнать из клана, уволить бойцов, выйти из аккаунта и т.п.
///   ConfirmDialog.Ask("Выгнать игрока?", "Пояснение", "Выгнать", () => { ... });
/// Кнопка «да» / Enter - сделать, «Отмена» / Esc / щелчок мимо окна - закрыть.
/// Если в настройках выключено «Переспрашивать перед важными действиями», действие выполняется сразу.
/// Пока вопрос открыт, карта не реагирует на мышь (см. BuildingPlacer.Update).
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    public static ConfirmDialog Instance { get; private set; }

    string caption, body, yes;
    UiKit.Kind yesKind;
    Action onYes;
    int openedFrame;
    bool open;

    public static bool IsOpen => Instance != null && Instance.open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    static ConfirmDialog Ensure()
    {
        if (Instance == null) new GameObject("ConfirmDialog").AddComponent<ConfirmDialog>();
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // Тревога: закрыть вопрос, чтобы было видно бой
    void OnEnable() { WarManager.OnAlarm += CollapseForAlarm; }
    void OnDisable() { WarManager.OnAlarm -= CollapseForAlarm; }
    bool CollapseForAlarm()
    {
        if (!open) return false;
        Close();
        return true;
    }

    /// <summary>Спросить «Точно?». yesKind - цвет кнопки «да» (по умолчанию красная).</summary>
    public static void Ask(string caption, string body, string yes, Action onYes, UiKit.Kind yesKind = UiKit.Kind.Danger)
    {
        if (!GameSettings.Confirm) { if (onYes != null) onYes(); return; }
        ConfirmDialog d = Ensure();
        d.caption = caption;
        d.body = body ?? "";
        d.yes = string.IsNullOrEmpty(yes) ? "Да" : yes;
        d.yesKind = yesKind;
        d.onYes = onYes;
        d.open = true;
        d.openedFrame = Time.frameCount;
    }

    public static void Close()
    {
        if (Instance == null) return;
        Instance.open = false;
        Instance.onYes = null;
    }

    void Confirm()
    {
        Action a = onYes;
        Close();
        if (a != null) a();
    }

    void Update()
    {
        if (!open) return;
        if (Time.frameCount == openedFrame) return;   // тот же Enter не должен сразу подтвердить
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame) Close();
        else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Confirm();
    }

    void OnGUI()
    {
        if (!open) return;
        UiKit.Apply();
        GUI.depth = -200;   // поверх окон зданий, клана и настроек

        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(UiKit.FullRect, Texture2D.whiteTexture);
        GUI.color = old;

        float w = 400f;
        float textH = string.IsNullOrEmpty(body) ? 0f : UiKit.TextH(body, w - UiKit.Pad * 2f) + 8f;
        float h = UiKit.WindowH(textH + UiKit.BtnH + 4f);
        Rect r = new Rect((UiKit.W - w) * 0.5f, (UiKit.H - h) * 0.5f, w, h);

        Rect c = UiKit.Window(r, caption, out bool close);
        if (close) { Close(); return; }

        if (textH > 0f) LocGUI.Label(new Rect(c.x, c.y, c.width, textH), body, UiKit.Body);

        float by = c.y + textH;
        float bw = (c.width - 8f) * 0.5f;
        if (UiKit.Button(new Rect(c.x, by, bw, UiKit.BtnH), yes, yesKind, "Enter")) { Confirm(); return; }
        if (UiKit.Button(new Rect(c.x + bw + 8f, by, bw, UiKit.BtnH), "Отмена", UiKit.Kind.Ghost, "Esc")) { Close(); return; }

        // Щелчок мимо окна - отмена; клики не доходят до окон под затемнением
        Event e = Event.current;
        if (e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) { Close(); e.Use(); return; }
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel) e.Use();
    }
}
