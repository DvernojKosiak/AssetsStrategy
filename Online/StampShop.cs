using System.Collections.Generic;
using UnityEngine;
#if STRATEGYCA_IAP
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using Unity.Services.Core;
#endif

/// <summary>
/// Магазин марок: марки за реальные деньги через Google Play (Unity IAP). Наборы - StampPacks.All.
/// Где виден: кнопка «◈ Магазин марок» под кнопкой «Постройки» (рисует BuildingPlacer через DrawButton) - открывает своё окно.
///
/// Как идёт покупка:
///  1. Игрок жмёт цену - открывается окно оплаты Google Play.
///  2. Оплачено - игра отправляет чек серверу (Api.ShopClaim). Сервер проверяет подпись Google и засчитывает покупку один раз.
///  3. Сервер ответил - марки приходят в город (как награда, WarGrant.marks), покупка закрывается в Google Play.
///     Нет связи - покупка ждёт: игра повторит сама, а Google Play вернёт её и после перезапуска игры.
///
/// Покупки работают, только когда в проект добавлен пакет In-App Purchasing и символ STRATEGYCA_IAP
/// (см. ЧТО_ИЗМЕНЕНО.md). Без него в редакторе Unity покупка «понарошку» (проверка без денег), а в сборке
/// кнопки показывают «Покупки пока не подключены».
/// Нужен вход в аккаунт: марки хранятся в городе на сервере.
/// </summary>
public class StampShop : MonoBehaviour
#if STRATEGYCA_IAP
    , IDetailedStoreListener
#endif
{
    public static StampShop Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("StampShop");
        DontDestroyOnLoad(go);
        go.AddComponent<StampShop>();
    }

    string msg = "";
    bool claiming;
    float retryTimer;
    bool open;
    static Rect buttonRect, winRect;
    static int buttonFrame = -10;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (open)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) open = false;
        }
#if STRATEGYCA_IAP
        // Оплаченные, но ещё не засчитанные покупки: повторяем, пока сервер не ответит
        if (pending.Count > 0 && !claiming && AccountManager.LoggedIn && !WarCityMode.Active)
        {
            retryTimer -= Time.unscaledDeltaTime;
            if (retryTimer <= 0f) ClaimNext();
        }
#endif
    }

    static bool CanBuyHere(out string why)
    {
        why = null;
        if (!AccountManager.LoggedIn) why = "Войдите в аккаунт — марки хранятся на сервере";
        else if (WarCityMode.Active) why = "На войне магазин закрыт — вернитесь домой";
        return why == null;
    }

    // ---------- Покупка ----------

    public void Buy(StampPack p)
    {
        if (claiming) return;
        if (!CanBuyHere(out string why)) { msg = UiKit.Col(UiKit.BadHex, why); return; }
#if STRATEGYCA_IAP
        if (controller == null) { msg = UiKit.Col(UiKit.BadHex, initError ?? "Магазин Google Play ещё загружается…"); TryInit(); return; }
        Product pr = controller.products.WithID(p.id);
        if (pr == null || !pr.availableToPurchase) { msg = UiKit.Col(UiKit.BadHex, "Этот набор сейчас недоступен в Google Play"); return; }
        msg = UiKit.Col(UiKit.MutedHex, "Открываем оплату Google Play…");
        controller.InitiatePurchase(pr);
#elif UNITY_EDITOR || DEVELOPMENT_BUILD
        // Проверка без денег: «ненастоящий» чек. Сервер без ключа Google Play его примет, с ключом - откажет
        var rec = new IapReceipt { Store = "fake", TransactionID = "test-" + System.Guid.NewGuid().ToString("N"), Payload = "" };
        Claim(p.id, JsonUtility.ToJson(rec), null);
#else
        msg = UiKit.Col(UiKit.BadHex, "Покупки пока не подключены");
#endif
    }

    // Чек - на сервер; ответ - марки в город
    void Claim(string productId, string receipt, System.Action<bool> confirm)
    {
        claiming = true;
        msg = UiKit.Col(UiKit.MutedHex, "Засчитываем покупку…");
        var req = new ApiRequest { productId = productId, receipt = receipt };
        AccountManager.Backend.Clan(Api.ShopClaim, AccountManager.Token, req, res =>
        {
            claiming = false;
            if (!res.ok)
            {
                msg = UiKit.Col(UiKit.BadHex, StampPacks.ServerError(res.error));
                if (confirm != null) confirm(false);
                return;
            }
            AccountManager.TakeGrants(res.grants);   // марки - в город, как награда (сообщение покажет AccountManager)
            if (res.shopMarks > 0) msg = UiKit.Col(UiKit.GoodHex, $"Готово: +{res.shopMarks} ◈");
            else msg = UiKit.Col(UiKit.MutedHex, string.IsNullOrEmpty(res.error) ? "Эта покупка уже засчитана" : res.error);
            if (confirm != null) confirm(true);
        });
    }

#if STRATEGYCA_IAP
    // ---------- Google Play (Unity IAP) ----------

    IStoreController controller;
    string initError;
    bool initStarted;
    readonly List<Product> pending = new List<Product>();
    readonly Dictionary<string, int> fails = new Dictionary<string, int>();

    void Start() { if (SalesOn) TryInit(); }

    async void TryInit()
    {
        if (initStarted || controller != null) return;
        initStarted = true;
        try { await UnityServices.InitializeAsync(); }
        catch (System.Exception e) { Debug.LogWarning("[Магазин] Unity Services: " + e.Message); }
        var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
        foreach (var p in StampPacks.All) builder.AddProduct(p.id, ProductType.Consumable);
        UnityPurchasing.Initialize(this, builder);
    }

    public void OnInitialized(IStoreController c, IExtensionProvider e)
    {
        controller = c;
        initError = null;
    }

    public void OnInitializeFailed(InitializationFailureReason error) { OnInitializeFailed(error, null); }

    public void OnInitializeFailed(InitializationFailureReason error, string message)
    {
        initStarted = false;   // попробуем ещё раз при следующей покупке
        initError = error == InitializationFailureReason.PurchasingUnavailable ? "Покупки отключены на этом устройстве"
                  : error == InitializationFailureReason.NoProductsAvailable ? "Google Play не нашёл наборы марок (проверьте id товаров в Play Console)"
                  : "Google Play недоступен: " + (message ?? error.ToString());
        Debug.LogWarning("[Магазин] " + initError);
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs e)
    {
        // Не закрываем покупку, пока сервер её не засчитал (иначе марки могли бы пропасть)
        if (!pending.Exists(x => x.transactionID == e.purchasedProduct.transactionID)) pending.Add(e.purchasedProduct);
        retryTimer = 0f;
        return PurchaseProcessingResult.Pending;
    }

    void ClaimNext()
    {
        Product pr = pending[0];
        retryTimer = 30f;
        Claim(pr.definition.id, pr.receipt, ok =>
        {
            if (ok)
            {
                controller.ConfirmPendingPurchase(pr);
                pending.Remove(pr);
                retryTimer = 0f;
                return;
            }
            // Сервер отказал или нет связи: попробуем позже; после нескольких отказов - до перезапуска игры
            string k = pr.transactionID ?? "";
            fails[k] = (fails.TryGetValue(k, out int n) ? n : 0) + 1;
            if (fails[k] >= 5) pending.Remove(pr);
        });
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason reason)
    {
        msg = reason == PurchaseFailureReason.UserCancelled ? UiKit.Col(UiKit.MutedHex, "Покупка отменена")
            : UiKit.Col(UiKit.BadHex, "Покупка не прошла: " + reason);
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureDescription d)
    {
        OnPurchaseFailed(product, d.reason);
    }

    static string PriceOf(StampPack p)
    {
        var s = Instance;
        if (s != null && s.controller != null)
        {
            Product pr = s.controller.products.WithID(p.id);
            if (pr != null && pr.metadata != null && !string.IsNullOrEmpty(pr.metadata.localizedPriceString)) return pr.metadata.localizedPriceString;
        }
        return p.price;
    }
#elif UNITY_EDITOR || DEVELOPMENT_BUILD
    static string PriceOf(StampPack p) { return p.price; }
#else
    static string PriceOf(StampPack p) { return p.price; }
#endif

    // ---------- Окно ----------

    const float RowH = 40f;

    static string Intro => "Марки ◈ — деньги рынка игроков: на них покупают товары и бойцов у других игроков и открывают новые места под лоты.";

    /// <summary>Список наборов (вкладка рынка игроков и меню города). Возвращает высоту.</summary>
    public static float Draw(Rect r, bool compact)
    {
        var s = Instance;
        float y = r.y;
        GUIStyle wrap = new GUIStyle(UiKit.Small) { wordWrap = true };
        float introH = UiKit.TextH(Intro, r.width, wrap);
        LocGUI.Label(new Rect(r.x, y, r.width, introH), UiKit.Col(UiKit.MutedHex, Intro), wrap);
        y += introH + 6f;

        bool can = CanBuyHere(out string why);
        if (!can)
        {
            LocGUI.Label(new Rect(r.x, y, r.width, 22f), UiKit.Col(UiKit.BadHex, why), UiKit.Small);
            y += 24f;
        }

        GUIStyle line = new GUIStyle(UiKit.Body) { wordWrap = false, clipping = TextClipping.Clip, richText = true };
        GUIStyle sub = new GUIStyle(UiKit.Small) { wordWrap = false, clipping = TextClipping.Clip, richText = true };
        float btnW = compact ? 86f : 120f;
        for (int i = 0; i < StampPacks.All.Length; i++)
        {
            StampPack p = StampPacks.All[i];
            Rect row = new Rect(r.x, y, r.width, RowH - 4f);
            Color oc = GUI.color;
            GUI.color = p.best ? new Color(1f, 0.85f, 0.4f, 0.10f) : new Color(1f, 1f, 1f, i % 2 == 0 ? 0.04f : 0.02f);
            GUI.DrawTexture(row, Texture2D.whiteTexture);
            GUI.color = oc;

            string name = $"<b>{p.name}</b>" + (p.best ? "  " + UiKit.Col(UiKit.AccentHex, "выгодно") : "");
            string amount = $"<color=#9FE0FF><b>{p.marks}</b></color> ◈" + (p.bonus > 0 ? "  " + UiKit.Col(UiKit.GoodHex, $"+{p.bonus} в подарок") : "");
            float tx = r.x + 6f, tw = r.width - btnW - 16f;
            LocGUI.Label(new Rect(tx, y + 1f, tw, 20f), name, line);
            LocGUI.Label(new Rect(tx, y + 18f, tw, 18f), amount, sub);
            bool busy = s == null || s.claiming;
            if (UiKit.Button(new Rect(row.xMax - btnW - 4f, y + 4f, btnW, RowH - 12f), PriceOf(p), can && !busy, UiKit.Kind.Primary,
                    can ? "Оплата через Google Play" : why))
                s.Buy(p);
            y += RowH;
        }

        if (s != null && !string.IsNullOrEmpty(s.msg))
        {
            float mh = UiKit.TextH(s.msg, r.width, wrap);
            LocGUI.Label(new Rect(r.x, y + 2f, r.width, mh), s.msg, wrap);
            y += mh + 4f;
        }
        string foot = "Покупку засчитывает сервер. Если связь пропала, марки придут сами, когда она вернётся.";
        float fh = UiKit.TextH(foot, r.width, wrap);
        LocGUI.Label(new Rect(r.x, y + 2f, r.width, fh), UiKit.Col(UiKit.MutedHex, foot), wrap);
        y += fh + 4f;
        return y - r.y;
    }

    // ---------- Кнопка под «Постройками» и окно ----------

    public static bool IsOpen => Instance != null && Instance.open;

    /// <summary>
    /// Продажа марок за деньги включена. Пока false - кнопки «Магазин марок» в игре нет и покупки не подключаются
    /// (первый выпуск в Google Play - без платных покупок). Включить: поставить true и собрать игру заново.
    /// </summary>
    public static readonly bool SalesOn = true;

    /// <summary>Мышь над кнопкой или окном магазина (карта под ними не кликается).</summary>
    public static bool IsPointerOver(Vector2 gp)
    {
        if (Instance == null) return false;
        return (Time.frameCount - buttonFrame <= 2 && buttonRect.Contains(gp)) || (Instance.open && winRect.Contains(gp));
    }

    /// <summary>Кнопка «Магазин марок» (рисует BuildingPlacer под кнопкой «Постройки»). Возвращает нижний край.</summary>
    public static float DrawButton(Rect r)
    {
        var s = Instance;
        if (s == null || WarCityMode.Active || !SalesOn) return r.y - 6f;
        buttonRect = r;
        buttonFrame = Time.frameCount;
        // Сколько марок - видно наверху, рядом с ресурсами; на кнопке только название
        if (UiKit.Button(r, "◈ Магазин марок", s.open ? UiKit.Kind.Primary : UiKit.Kind.Normal, "Марки за реальные деньги: покупки на рынке игроков и места под лоты"))
        {
            s.open = !s.open;
            if (s.open) s.msg = "";
        }
        return r.yMax;
    }

    float winContentH = 380f;
    Vector2 winScroll;

    bool wasOpen;

    void OnGUI()
    {
        open = UiKit.KeepFront("stamps", open, ref wasOpen);   // одно большое окно за раз
        if (!open || WarView.IsOpen) return;
        if (WarCityMode.Active) { open = false; return; }
        UiKit.Apply();
        GUI.depth = -10;
        float w = Mathf.Min(560f, UiKit.W - 40f);
        float h = Mathf.Min(UiKit.WindowH(winContentH + 28f), UiKit.H - 80f);
        winRect = new Rect((UiKit.W - w) / 2f, (UiKit.H - h) / 2f, w, h);
        if (UiKit.Phone) { winRect = UiKit.CenterWindow(w, UiKit.WindowH(winContentH + 28f)); w = winRect.width; h = winRect.height; }
        CameraController.BlockZoom();
        Rect c = UiKit.Window(winRect, "Магазин марок", out bool close);
        if (close) { open = false; return; }
        var rm = ResourceManager.Instance;
        LocGUI.Label(new Rect(c.x, c.y, c.width, 22f), $"У вас: <color=#9FE0FF><b>{(rm != null ? rm.marks : 0)}</b></color> ◈", UiKit.Body);
        Rect view = new Rect(c.x, c.y + 26f, c.width, c.height - 26f);
        winScroll = UiKit.BeginScrollView(view, winScroll, new Rect(0f, 0f, view.width - 16f, winContentH));
        float ch = Draw(new Rect(0f, 0f, view.width - 16f, winContentH), false);
        if (Event.current.type == EventType.Repaint) winContentH = ch;
        GUI.EndScrollView();
    }
}
