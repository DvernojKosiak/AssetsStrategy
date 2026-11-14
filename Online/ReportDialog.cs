using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// «Пожаловаться на игрока»: причина, по желанию - сразу заблокировать, отправить модератору (Api.ChatReport).
/// Жалобы видны на странице модерации сервера (/admin): там игроку закрывают чат, сбрасывают имя или стирают сообщения.
///   ReportDialog.Open(accountId, имя, номер сообщения или 0, текст для игрока => ...);
/// </summary>
public class ReportDialog : MonoBehaviour
{
    static ReportDialog inst;

    static readonly string[] Reasons =
    {
        "Оскорбления или травля",
        "Спам или реклама",
        "Мошенничество, выманивает пароль",
        "Непристойное или жестокое",
        "Неприемлемое имя",
        "Другое",
    };

    bool open, sending, alsoBlock;
    string targetId, targetName;
    int messageN, reason = -1;
    Action<string> done;
    int openedFrame;

    public static bool IsOpen => inst != null && inst.open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; }

    public static void Open(string accountId, string name, int messageN, Action<string> done)
    {
        if (string.IsNullOrEmpty(accountId)) return;
        if (inst == null)
        {
            var go = new GameObject("ReportDialog");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<ReportDialog>();
        }
        inst.targetId = accountId;
        inst.targetName = name ?? "";
        inst.messageN = messageN;
        inst.done = done;
        inst.reason = -1;
        inst.alsoBlock = false;
        inst.sending = false;
        inst.open = true;
        inst.openedFrame = Time.frameCount;
    }

    void Update()
    {
        if (!open || Time.frameCount == openedFrame) return;
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) open = false;
    }

    void Send()
    {
        if (sending || reason < 0 || !AccountManager.LoggedIn) return;
        sending = true;
        string id = targetId, name = targetName;
        bool block = alsoBlock;
        Action<string> after = done;
        AccountManager.Backend.Call(Api.ChatReport, AccountManager.Token, new ApiRequest { memberId = id, text = Reasons[reason], since = messageN }, res =>
        {
            sending = false;
            if (!res.ok) { if (after != null) after(UiKit.Col(UiKit.BadHex, "Жалоба не отправлена: " + res.error)); return; }
            open = false;
            WarManager.Notify("Жалоба отправлена. Спасибо! Модератор её рассмотрит", true);
            if (after != null) after(UiKit.Col(UiKit.GoodHex, "Жалоба отправлена"));
            if (block && !(AccountManager.Profile != null && AccountManager.Profile.Blocks(id))) ClanWindow.SetBlocked(id, name, true, after);
        });
    }

    void OnGUI()
    {
        if (!open) return;
        UiKit.Apply();
        GUI.depth = -150;   // поверх окна клана, под вопросом «Точно?»

        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(UiKit.FullRect, Texture2D.whiteTexture);
        GUI.color = old;

        float w = Mathf.Min(420f, UiKit.W - 24f);
        const float row = 30f;
        float h = UiKit.WindowH(40f + Reasons.Length * row + 6f + 26f + 8f + UiKit.BtnH);
        h = Mathf.Min(h, UiKit.H - 20f);
        Rect r = new Rect((UiKit.W - w) * 0.5f, (UiKit.H - h) * 0.5f, w, h);
        Rect c = UiKit.Window(r, $"Пожаловаться на {Loc.Keep(targetName)}", out bool close);
        if (close) { open = false; return; }

        float y = c.y;
        LocGUI.Label(new Rect(c.x, y, c.width, 38f), UiKit.Col(UiKit.MutedHex, "Модератор проверит сообщения игрока. Отправитель жалобы игроку не показывается."), UiKit.Small);
        y += 40f;
        for (int i = 0; i < Reasons.Length; i++)
        {
            bool on = reason == i;
            if (UiKit.Button(new Rect(c.x, y, c.width, row - 4f), (on ? "● " : "○ ") + Reasons[i], on ? UiKit.Kind.Primary : UiKit.Kind.Normal, null)) reason = i;
            y += row;
        }
        y += 6f;
        alsoBlock = LocGUI.Toggle(new Rect(c.x, y, c.width, 22f), alsoBlock, " Заблокировать: не видеть его сообщения");
        y += 26f + 8f;
        float bw = (c.width - 8f) * 0.5f;
        if (UiKit.Button(new Rect(c.x, y, bw, UiKit.BtnH), sending ? "Отправляем…" : "Отправить", reason >= 0 && !sending, UiKit.Kind.Primary, reason < 0 ? "Выберите причину" : null)) Send();
        if (UiKit.Button(new Rect(c.x + bw + 8f, y, bw, UiKit.BtnH), "Отмена", UiKit.Kind.Ghost, "Esc")) open = false;

        // Клики мимо окна не доходят до окон под затемнением
        Event e = Event.current;
        if (e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) { open = false; e.Use(); return; }
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel) e.Use();
    }
}
