using UnityEngine;

/// <summary>
/// То, что нужно игре для Google Play (часть AccountManager):
///  - вход через Google Play Games: на новом телефоне аккаунт находится сам, без пароля; к старому аккаунту
///    Google привязывается после входа по паролю (или кнопкой в ⚙ Настройки → Аккаунт);
///  - ссылки на правила игры и политику конфиденциальности (страницы сервера /terms и /privacy);
///  - вкладка «Аккаунт»: Google Play Games, push-уведомления, заблокированные игроки, удаление аккаунта.
/// Без плагина Google Play Games (символ STRATEGYCA_GPGS, см. PlayGames) кнопок Google нет, остальное работает.
/// </summary>
public partial class AccountManager
{
    // ---------- Вход через Google Play Games ----------

    bool googleTried;           // тихий вход при запуске уже пробовали
    string googleTicket;        // Google узнал игрока, но аккаунта ещё нет: этим билетом заводим новый или привязываем
    string googleName = "";
    string googleError = "";    // для вкладки «Аккаунт»: почему не привязался

    /// <summary>При запуске без сохранённой сессии: Google Play Games уже вошёл сам - ищем его аккаунт на сервере.</summary>
    void TryGoogleSilent()
    {
        if (googleTried || !PlayGames.Available || LoggedIn) return;
        googleTried = true;
        busy = true;
        note = "Вход через Google Play…";
        PlayGames.SignIn(false, ok =>
        {
            if (!ok) { busy = false; note = ""; return; }
            GoogleLogin(false);
        });
    }

    /// <summary>Кнопка «Войти через Google Play Games».</summary>
    void GoogleButton()
    {
        if (busy) return;
        busy = true;
        error = "";
        note = "Вход через Google Play…";
        PlayGames.SignIn(true, ok =>
        {
            if (!ok) { busy = false; note = ""; error = "Не удалось войти в Google Play Games"; return; }
            GoogleLogin(false);
        });
    }

    // Код от Google → сервер: аккаунт есть - входим; нет - запоминаем билет и спрашиваем (новый город или войти в старый)
    void GoogleLogin(bool create)
    {
        busy = true;
        note = "Вход через Google Play…";
        System.Action<ApiRequest> send = req => Backend.Call(Api.GoogleLogin, null, req, res =>
        {
            busy = false;
            note = "";
            if (!res.ok)
            {
                error = res.error ?? "";
                googleTicket = null;
                return;
            }
            if (res.googleNew)
            {
                googleTicket = res.googleTicket;
                googleName = res.googleName ?? "";
                tab = 0;
                return;
            }
            googleTicket = null;
            SignedIn(AccountResult.Ok(res.profile, res.token), true, create);
        });

        if (create && !string.IsNullOrEmpty(googleTicket)) { send(new ApiRequest { googleTicket = googleTicket, create = true }); return; }
        PlayGames.ServerCode(code =>
        {
            if (string.IsNullOrEmpty(code)) { busy = false; note = ""; error = "Google Play Games не дал код входа. Проверьте настройки (см. ЧТО_ИЗМЕНЕНО.md)"; return; }
            send(new ApiRequest { authCode = code, create = create });
        });
    }

    /// <summary>Город открылся: если вошли по паролю, а Google Play Games на телефоне есть - привязываем, чтобы прогресс не потерялся.</summary>
    void AfterHomeCity()
    {
        if (Profile == null || Profile.googleLinked) return;
        if (!string.IsNullOrEmpty(googleTicket)) { LinkGoogle(new ApiRequest { googleTicket = googleTicket }, false); return; }
        if (PlayGames.Available && PlayGames.IsSignedIn)
            PlayGames.ServerCode(code => { if (!string.IsNullOrEmpty(code)) LinkGoogle(new ApiRequest { authCode = code }, false); });
    }

    void LinkGoogle(ApiRequest req, bool loud)
    {
        Backend.Call(Api.GoogleLink, token, req, res =>
        {
            googleTicket = null;
            if (res.ok && res.profile != null && Profile != null)
            {
                Profile.googleLinked = true;
                googleError = "";
                WarManager.Notify("Аккаунт привязан к Google Play Games: на новом телефоне вы войдёте в него сами", true);
            }
            else
            {
                googleError = res.error ?? "";
                if (loud) pageMsg = UiKit.Col(UiKit.BadHex, googleError);
            }
        });
    }

    /// <summary>Кнопка «Привязать» во вкладке «Аккаунт».</summary>
    void LinkGoogleButton()
    {
        PlayGames.SignIn(true, ok =>
        {
            if (!ok) { pageMsg = UiKit.Col(UiKit.BadHex, "Не удалось войти в Google Play Games"); return; }
            PlayGames.ServerCode(code =>
            {
                if (string.IsNullOrEmpty(code)) { pageMsg = UiKit.Col(UiKit.BadHex, "Google Play Games не дал код входа"); return; }
                LinkGoogle(new ApiRequest { authCode = code }, true);
            });
        });
    }

    // ---------- Экран входа ----------

    const float LegalH = 34f;

    float StoreLoginHeight(float cw)
    {
        float h = LegalH;
        if (PlayGames.Available) h += !string.IsNullOrEmpty(googleTicket) ? GoogleOfferH(cw) + 10f : UiKit.BtnH + 6f + 20f;
        return h;
    }

    float GoogleOfferH(float cw) { return 4f + UiKit.TextH(GoogleOfferText, cw - 16f, UiKit.Small) + 8f + UiKit.BtnH + 8f; }

    string GoogleOfferText => $"Google Play: {UiKit.B(Loc.Keep(googleName))} — новый игрок.\nУже играли? Войдите логином и паролем ниже — аккаунт сам привяжется к Google Play.";

    void DrawStoreLoginTop(ref float y, float x, float cw)
    {
        if (!PlayGames.Available) return;
        if (!string.IsNullOrEmpty(googleTicket))
        {
            float h = GoogleOfferH(cw);
            Rect box = new Rect(x, y, cw, h);
            LocGUI.Box(box, GUIContent.none);
            float th = UiKit.TextH(GoogleOfferText, cw - 16f, UiKit.Small);
            LocGUI.Label(new Rect(x + 8f, y + 4f, cw - 16f, th), GoogleOfferText, UiKit.Small);
            if (UiKit.Button(new Rect(x + 8f, y + 4f + th + 8f, cw - 16f, UiKit.BtnH), busy ? "Подождите…" : "Начать новый город", UiKit.Kind.Primary,
                    "Новый аккаунт, привязанный к Google Play Games"))
                GoogleLogin(true);
            y += h + 10f;
            return;
        }
        if (UiKit.Button(new Rect(x, y, cw, UiKit.BtnH), "▶  Войти через Google Play Games", UiKit.Kind.Primary, "Без пароля: прогресс сохранится и на новом телефоне"))
            GoogleButton();
        y += UiKit.BtnH + 6f;
        LocGUI.Label(new Rect(x, y, cw, 18f), UiKit.Col(UiKit.MutedHex, "— или логин и пароль —"), new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter });
        y += 20f;
    }

    /// <summary>«Создавая аккаунт, вы принимаете правила и политику» - со ссылками.</summary>
    void DrawStoreLegal(ref float y, float x, float cw)
    {
        LocGUI.Label(new Rect(x, y, cw, 16f), UiKit.Col(UiKit.MutedHex, "Создавая аккаунт, вы принимаете"), new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter });
        DrawLegalLinks(new Rect(x, y + 16f, cw, 18f), true);
        y += LegalH;
    }

    static void DrawLegalLinks(Rect r, bool accept)
    {
        var link = new GUIStyle(UiKit.Small) { alignment = TextAnchor.MiddleCenter };
        float half = r.width * 0.5f;
        if (LocGUI.Button(new Rect(r.x, r.y, half, r.height), UiKit.Col(UiKit.InfoHex, "Правила игры"), link)) OpenWeb(Api.WebTerms);
        if (LocGUI.Button(new Rect(r.x + half, r.y, half, r.height), UiKit.Col(UiKit.InfoHex, accept ? "Политику конфиденциальности" : "Политика конфиденциальности"), link)) OpenWeb(Api.WebPrivacy);
    }

    /// <summary>Открыть страницу сервера в браузере (политика, правила, удаление аккаунта).</summary>
    public static void OpenWeb(string path)
    {
        string address = Backend is OnlineAccountBackend ob ? ob.Address : Api.DefaultAddress;
        Application.OpenURL(address + path);
    }

    // ---------- Вкладка «Аккаунт» ----------

    string deleteWord = "";
    bool deleting;              // запрос на удаление ушёл: город больше не сохраняем
    float deletedAt = -1f;      // аккаунт удалён: показываем это и закрываем игру

    static string DeleteWordHere => Loc.Current == Loc.Lang.RU ? AccountRules.DeleteWord : AccountRules.DeleteWordEn;

    float StorePageHeight(float w)
    {
        if (!LoggedIn) return 0f;
        int blocked = Profile.blocked != null ? Profile.blocked.Count : 0;
        return UiKit.SectionH + 24f + (googleError != "" && !Profile.googleLinked ? 34f : 0f)    // Google Play Games
            + UiKit.SectionH + 3f * 24f + 4f                                                       // уведомления
            + UiKit.SectionH + (blocked == 0 ? 22f : Mathf.Min(blocked, 6) * 26f + (blocked > 6 ? 20f : 0f))   // заблокированные
            + 26f                                                                                  // правила и политика
            + UiKit.SectionH + 50f + 18f + 28f + 6f + UiKit.BtnH + 10f;                            // удаление аккаунта
    }

    float DrawStorePage(float x, float y, float w)
    {
        // Google Play Games
        y = UiKit.Section(x, y, w, "Google Play Games");
        Rect gr = new Rect(x, y, w, 22f);
        if (Profile.googleLinked) UiKit.Row(gr, "Вход без пароля", UiKit.Col(UiKit.GoodHex, "✓ привязан"));
        else if (PlayGames.Available)
        {
            LocGUI.Label(new Rect(x, y, w - 110f, 22f), UiKit.Col(UiKit.MutedHex, "Не привязан: на новом телефоне нужен пароль"), UiKit.Small);
            if (UiKit.Button(new Rect(x + w - 104f, y - 1f, 104f, 24f), "Привязать", UiKit.Kind.Normal, "Привязать аккаунт к Google Play Games")) LinkGoogleButton();
        }
        else UiKit.Row(gr, "Вход без пароля", UiKit.Col(UiKit.MutedHex, "в версии для Android"));
        y += 24f;
        if (googleError != "" && !Profile.googleLinked)
        {
            LocGUI.Label(new Rect(x, y, w, 32f), UiKit.Col(UiKit.BadHex, googleError), UiKit.Small);
            y += 34f;
        }

        // Уведомления
        y = UiKit.Section(x, y, w, "Уведомления");
        int off = Profile.pushOff;
        off = PushToggle(x, ref y, w, off, PushKinds.Attack, "На вас напали, итог набега");
        off = PushToggle(x, ref y, w, off, PushKinds.Build, "Стройка или улучшение готовы");
        off = PushToggle(x, ref y, w, off, PushKinds.Shield, "Щит скоро кончится");
        if (off != Profile.pushOff)
        {
            Profile.pushOff = off;
            PushNotifications.SaveSettings();
        }
        y += 4f;

        // Заблокированные игроки
        y = UiKit.Section(x, y, w, "Заблокированные игроки");
        var list = Profile.blocked;
        if (list == null || list.Count == 0)
        {
            LocGUI.Label(new Rect(x, y, w, 20f), UiKit.Col(UiKit.MutedHex, "Никого. Заблокировать игрока можно в чате клана: нажмите на его сообщение"), UiKit.Small);
            y += 22f;
        }
        else
        {
            for (int i = 0; i < list.Count && i < 6; i++)
            {
                BlockedPlayer b = list[i];
                if (b == null) continue;
                LocGUI.Label(new Rect(x, y, w - 130f, 24f), Loc.Keep(b.name ?? "?"), UiKit.Body);
                string id = b.id;
                if (UiKit.Button(new Rect(x + w - 124f, y, 124f, 24f), "Разблокировать", UiKit.Kind.Normal, "Снова видеть сообщения этого игрока"))
                    ClanWindow.SetBlocked(id, b.name, false, m => pageMsg = m);
                y += 26f;
            }
            if (list.Count > 6) { LocGUI.Label(new Rect(x, y, w, 18f), UiKit.Col(UiKit.MutedHex, $"и ещё {list.Count - 6}"), UiKit.Small); y += 20f; }
        }

        DrawLegalLinks(new Rect(x, y + 2f, w, 20f), false);
        y += 26f;

        // Удаление аккаунта
        y = UiKit.Section(x, y, w, "Удаление аккаунта");
        LocGUI.Label(new Rect(x, y, w, 48f), UiKit.Col(UiKit.MutedHex, "Навсегда удаляются город, ресурсы, марки (и купленные), место в клане, ваши сообщения и рейтинг. Восстановить аккаунт нельзя."), UiKit.Small);
        y += 50f;
        string word = DeleteWordHere;
        deleteWord = Field(ref y, x, w, $"Чтобы удалить, введите {Loc.Keep(word)}", deleteWord, false);
        bool ready = AccountRules.IsDeleteWord(deleteWord) && !deleting;
        if (UiKit.Button(new Rect(x, y, w, UiKit.BtnH), deleting ? "Удаляем…" : "Удалить аккаунт навсегда", ready, UiKit.Kind.Danger, ready ? null : $"Введите {word} в поле выше"))
            ConfirmDialog.Ask("Удалить аккаунт навсегда?", "Город, марки, клан и рейтинг пропадут, вернуть их будет нельзя. Игра закроется.", "Удалить", DeleteAccountNow);
        y += UiKit.BtnH + 10f;
        return y;
    }

    static int PushToggle(float x, ref float y, float w, int off, int bit, string label)
    {
        bool on = (off & bit) == 0;
        bool now = LocGUI.Toggle(new Rect(x, y, w, 22f), on, " " + label);
        y += 24f;
        return now ? off & ~bit : off | bit;
    }

    void DeleteAccountNow()
    {
        if (deleting || !LoggedIn) return;
        deleting = true;
        string word = deleteWord;
        Backend.Call(Api.DeleteAccount, token, new ApiRequest { text = word }, res =>
        {
            if (!res.ok)
            {
                deleting = false;
                pageMsg = UiKit.Col(UiKit.BadHex, res.error);
                return;
            }
            Profile = null;
            token = null;
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.Save();
            systemOpen = false;
            deletedAt = Time.unscaledTime;
        });
    }

    // Аккаунт удалён: пару секунд показываем это, потом игра закрывается
    bool TickDeleted()
    {
        if (deletedAt < 0f) return false;
        Time.timeScale = 0f;
        if (Time.unscaledTime - deletedAt > 3f) { deletedAt = float.MaxValue; Quit(); }
        return true;
    }

    bool DrawDeleted()
    {
        if (deletedAt < 0f) return false;
        UiKit.Apply();
        GUI.depth = -300;
        Color old = GUI.color;
        GUI.color = new Color(0.03f, 0.035f, 0.045f, 1f);
        GUI.DrawTexture(UiKit.FullRect, Texture2D.whiteTexture);
        GUI.color = old;
        float w = Mathf.Min(520f, UiKit.W - 40f);
        Rect r = new Rect((UiKit.W - w) / 2f, UiKit.H / 2f - 50f, w, 100f);
        LocGUI.Label(new Rect(r.x, r.y, w, 30f), "Аккаунт удалён", new GUIStyle(UiKit.Title) { alignment = TextAnchor.MiddleCenter });
        LocGUI.Label(new Rect(r.x, r.y + 40f, w, 50f), "Все данные стёрты с сервера. Игра сейчас закроется.", new GUIStyle(UiKit.Body) { alignment = TextAnchor.UpperCenter, wordWrap = true });
        Event e = Event.current;
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel) e.Use();
        return true;
    }
}
