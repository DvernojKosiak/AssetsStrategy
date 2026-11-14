using UnityEngine;

/// <summary>
/// Принудительное обновление (final-8): сервер ответил, что эта сборка устарела (ApiResponse.updateRequired).
/// Игра закрывается экраном «Нужно обновить игру» с кнопкой в Google Play; играть дальше нельзя,
/// пока не поставят новую версию. Как включить - ServerUpdate.cs (кнопка на странице администратора).
/// </summary>
public class UpdateGate : MonoBehaviour
{
    static UpdateGate inst;
    static string url = "", text = "";

    /// <summary>Экран обновления открыт - игра стоит.</summary>
    public static bool Active => inst != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; url = text = ""; }

    /// <summary>Ответ сервера: если там «обновите игру» - показать экран.</summary>
    public static void Check(ApiResponse res)
    {
        if (res == null || !res.updateRequired) return;
        url = string.IsNullOrEmpty(res.updateUrl) ? "https://play.google.com/store/apps/details?id=" + Application.identifier : res.updateUrl;
        text = string.IsNullOrEmpty(res.error) ? "Вышло обновление Strategyca. Обновите игру в Google Play, чтобы продолжить" : res.error;
        if (inst != null) return;
        var go = new GameObject("UpdateGate");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<UpdateGate>();
    }

    void OnDestroy() { if (inst == this) inst = null; }

    void Update() { Time.timeScale = 0f; }
    void LateUpdate() { Time.timeScale = 0f; }

    void OnGUI()
    {
        UiKit.Apply();
        GUI.depth = -5000;   // поверх всего, и касания сюда - первыми
        // Затемнение на весь экран: под ним ничего не нажимается
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.DrawTexture(new Rect(0f, 0f, UiKit.W, UiKit.H), Texture2D.whiteTexture);
        GUI.color = old;

        float w = Mathf.Min(440f, UiKit.W - 24f), h = 210f;
        Rect r = new Rect((UiKit.W - w) * 0.5f, (UiKit.H - h) * 0.5f, w, h);
        Rect c = UiKit.Window(r, "Нужно обновить игру");
        float th = UiKit.TextH(text, c.width, UiKit.Body);
        LocGUI.Label(new Rect(c.x, c.y + 4f, c.width, th), text, UiKit.Body);
        LocGUI.Label(new Rect(c.x, c.y + th + 10f, c.width, 40f),
            UiKit.Col(UiKit.MutedHex, "Город и всё накопленное сохранены на сервере и будут ждать вас в новой версии."), UiKit.Small);
        if (UiKit.Button(new Rect(c.x + (c.width - 200f) * 0.5f, c.yMax - 40f, 200f, 36f), "Обновить", UiKit.Kind.Primary))
            OpenStore();
        // События дальше не пускаем: ни окна, ни карта под экраном не должны получить нажатие
        if (Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp
            || Event.current.type == EventType.ScrollWheel || Event.current.type == EventType.KeyDown)) Event.current.Use();
    }

    static void OpenStore()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Сначала приложение Google Play, если его нет - страница в браузере
        try
        {
            using (var intentClass = new AndroidJavaClass("android.content.Intent"))
            using (var uriClass = new AndroidJavaClass("android.net.Uri"))
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "market://details?id=" + Application.identifier);
                var intent = new AndroidJavaObject("android.content.Intent", intentClass.GetStatic<string>("ACTION_VIEW"), uri);
                intent.Call<AndroidJavaObject>("setPackage", "com.android.vending");
                intent.Call<AndroidJavaObject>("addFlags", 0x10000000);   // FLAG_ACTIVITY_NEW_TASK
                activity.Call("startActivity", intent);
                return;
            }
        }
        catch (System.Exception) { }
#endif
        Application.OpenURL(url);
    }
}
