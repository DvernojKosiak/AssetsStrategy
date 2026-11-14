using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Перезапуск игры без выхода из неё: как будто нажали Play заново. Нужен, чтобы перейти из домашнего города
/// в военный и обратно (в игре всегда один город) и чтобы окно дополнительного игрока Unity вернулось ко входу.
///  1. Удаляем всё, что живёт между сценами (DontDestroyOnLoad).
///  2. Сбрасываем статические поля - вызываем все [RuntimeInitializeOnLoadMethod(SubsystemRegistration / BeforeSceneLoad)].
///  3. Загружаем сцену заново, затем вызываем [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] (они создают менеджеры).
/// afterReset вызывается после сброса, до загрузки сцены: там ставят, что делать после перезапуска (например, WarCityMode).
/// </summary>
public class GameRestart : MonoBehaviour
{
    static bool running;

    public static bool Running => running;

    public static void Now(System.Action afterReset)
    {
        if (running) return;
        running = true;
        var go = new GameObject("GameRestart");
        DontDestroyOnLoad(go);
        go.AddComponent<GameRestart>().StartCoroutine(Run(go, afterReset));
    }

    static IEnumerator Run(GameObject self, System.Action afterReset)
    {
        Time.timeScale = 1f;
        foreach (GameObject root in self.scene.GetRootGameObjects())
            if (root != self) Destroy(root);
        yield return null;   // удалённые объекты исчезают в конце кадра

        Invoke(RuntimeInitializeLoadType.SubsystemRegistration);
        Invoke(RuntimeInitializeLoadType.BeforeSceneLoad);
        running = true;      // сброс мог обнулить и нас
        if (afterReset != null) afterReset();

        AsyncOperation op = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex, LoadSceneMode.Single);
        while (op != null && !op.isDone) yield return null;
        Invoke(RuntimeInitializeLoadType.AfterSceneLoad);
        running = false;
        Destroy(self);
    }

    static readonly Dictionary<RuntimeInitializeLoadType, List<MethodInfo>> methods = new Dictionary<RuntimeInitializeLoadType, List<MethodInfo>>();

    static void Invoke(RuntimeInitializeLoadType when)
    {
        if (!methods.TryGetValue(when, out List<MethodInfo> list))
        {
            list = new List<MethodInfo>();
            foreach (System.Type t in typeof(GameRestart).Assembly.GetTypes())
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var a = m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                    if (a != null && a.loadType == when && m.GetParameters().Length == 0) list.Add(m);
                }
            methods[when] = list;
        }
        foreach (MethodInfo m in list)
        {
            if (m.DeclaringType == typeof(GameRestart)) continue;
            try { m.Invoke(null, null); }
            catch (System.Exception e) { Debug.LogWarning("[Перезапуск] " + m.DeclaringType.Name + "." + m.Name + ": " + e.Message); }
        }
    }
}
