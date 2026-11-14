using System;
using System.Collections.Generic;
using UnityEngine;
#if STRATEGYCA_GPGS && UNITY_ANDROID && !UNITY_EDITOR
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

/// <summary>
/// Вход через Google Play Games (плагин «Google Play Games plugin for Unity» v2).
/// Работает только в сборке для Android, когда плагин добавлен в проект: символ STRATEGYCA_GPGS ставится сам
/// (Assets/Online/Editor/GooglePlayDefines.cs). Без плагина Available = false, и игра входит как раньше - по логину.
///
/// Как устроено: плагин сам входит в Google Play Games при запуске (или по кнопке - ManuallyAuthenticate),
/// потом даёт одноразовый код (RequestServerSideAccess). Игра отправляет код серверу (Api.GoogleLogin),
/// сервер спрашивает у Google, кто это, и находит аккаунт, привязанный к этому игроку Google.
/// </summary>
public static class PlayGames
{
#if STRATEGYCA_GPGS && UNITY_ANDROID && !UNITY_EDITOR
    static bool activated;

    public static bool Available => true;
    public static bool IsSignedIn => activated && PlayGamesPlatform.Instance.IsAuthenticated();

    static void Activate()
    {
        if (activated) return;
        PlayGamesPlatform.Activate();
        activated = true;
    }

    /// <summary>Войти в Google Play Games. interactive - показать окно входа Google (по кнопке).</summary>
    public static void SignIn(bool interactive, Action<bool> done)
    {
        try
        {
            Activate();
            Action<SignInStatus> cb = s => MainThreadQueue.Post(() => done(s == SignInStatus.Success));
            if (interactive) PlayGamesPlatform.Instance.ManuallyAuthenticate(cb);
            else PlayGamesPlatform.Instance.Authenticate(cb);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Google Play Games] " + e.Message);
            done(false);
        }
    }

    /// <summary>Одноразовый код для сервера (null - не дали: не вошли или в плагине не указан Web client ID).</summary>
    public static void ServerCode(Action<string> done)
    {
        try
        {
            Activate();
            PlayGamesPlatform.Instance.RequestServerSideAccess(false, code => MainThreadQueue.Post(() => done(string.IsNullOrEmpty(code) ? null : code)));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Google Play Games] " + e.Message);
            done(null);
        }
    }
#else
    public static bool Available => false;
    public static bool IsSignedIn => false;
    public static void SignIn(bool interactive, Action<bool> done) { done(false); }
    public static void ServerCode(Action<string> done) { done(null); }
#endif
}

/// <summary>Ответы Google и Firebase приходят из других потоков - выполняем их в Update главного потока.</summary>
public class MainThreadQueue : MonoBehaviour
{
    static MainThreadQueue inst;
    static readonly Queue<Action> queue = new Queue<Action>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { inst = null; lock (queue) queue.Clear(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (inst != null) return;
        var go = new GameObject("MainThreadQueue");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<MainThreadQueue>();
    }

    public static void Post(Action a)
    {
        if (a == null) return;
        lock (queue) queue.Enqueue(a);
    }

    void Update()
    {
        while (true)
        {
            Action a;
            lock (queue)
            {
                if (queue.Count == 0) return;
                a = queue.Dequeue();
            }
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
