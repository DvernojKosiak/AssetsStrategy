using UnityEditor;
using UnityEngine;

/// <summary>
/// Тестовый сервер прямо в редакторе: окно Strategyca → Сервер.
/// По умолчанию сервер запускается сам при открытии Unity, так что достаточно нажать Play.
/// </summary>
[InitializeOnLoad]
public class ServerWindow : EditorWindow
{
    const string AutoKey = "Strategyca.Server.Auto";
    const string PortKey = "Strategyca.Server.Port";

    static bool Auto
    {
        get => EditorPrefs.GetBool(AutoKey, true);
        set => EditorPrefs.SetBool(AutoKey, value);
    }

    static int Port
    {
        get => EditorPrefs.GetInt(PortKey, Api.DefaultPort);
        set => EditorPrefs.SetInt(PortKey, value);
    }

    static string lastError;
    static string adminLogin = "", adminNote = "";
    Vector2 scroll;

    static ServerWindow()
    {
        // Перед перекомпиляцией скриптов и при выходе из Unity сервер освобождает порт
        AssemblyReloadEvents.beforeAssemblyReload += StrategycaServer.Stop;
        EditorApplication.quitting += StrategycaServer.Stop;
        // Окна дополнительных игроков (Multiplayer Play Mode) свой сервер не запускают: сервер один - в главном окне Unity
        if (IsVirtualPlayer) return;
        if (Auto) EditorApplication.delayCall += () => { if (StrategycaServer.Running == null) StartServer(); };
        // Перед Play: сервер почему-то не работает (не запустился, порт был занят) - пробуем ещё раз
        EditorApplication.playModeStateChanged += st =>
        {
            if (st == PlayModeStateChange.ExitingEditMode && Auto && StrategycaServer.Running == null) StartServer();
        };
    }

    static bool IsVirtualPlayer => Application.dataPath.Replace('\\', '/').Contains("/Library/VP/");

    static void StartServer()
    {
        StrategycaServer.Start(Port, StrategycaServer.DefaultDataDir, out lastError);
    }

    [MenuItem("Strategyca/Сервер")]
    static void Open()
    {
        GetWindow<ServerWindow>("Сервер Strategyca").minSize = new Vector2(360, 320);
    }

    void OnInspectorUpdate() { Repaint(); }

    void OnGUI()
    {
        StrategycaServer s = StrategycaServer.Running;
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.Space();

        if (s != null)
        {
            EditorGUILayout.HelpBox($"Сервер работает: http://localhost:{s.Port}\nИгроков: {s.Core.PlayerCount}    Запросов: {s.Requests}", MessageType.Info);
            if (GUILayout.Button("Остановить", GUILayout.Height(28))) StrategycaServer.Stop();
        }
        else
        {
            EditorGUILayout.HelpBox("Сервер остановлен" + (string.IsNullOrEmpty(lastError) ? "" : "\nОшибка: " + lastError), MessageType.Warning);
            Port = EditorGUILayout.IntField("Порт", Port);
            if (GUILayout.Button("Запустить", GUILayout.Height(28))) StartServer();
        }

        EditorGUILayout.Space();
        Auto = EditorGUILayout.ToggleLeft("Запускать сервер вместе с Unity", Auto);

        if (s != null) DrawAdmin(s);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Данные (аккаунты и города):", EditorStyles.miniLabel);
        EditorGUILayout.SelectableLabel(StrategycaServer.DefaultDataDir, EditorStyles.textField, GUILayout.Height(18));
        if (GUILayout.Button("Открыть папку данных")) EditorUtility.RevealInFinder(StrategycaServer.DefaultDataDir + "/");
        EditorGUILayout.EndScrollView();
    }

    // Админ: кто видит в игре кнопки дней войны, и сами кнопки для каждой идущей войны
    static void DrawAdmin(StrategycaServer s)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Админ сервера", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Сейчас: " + s.Core.AdminName, EditorStyles.wordWrappedLabel);
        EditorGUILayout.BeginHorizontal();
        adminLogin = EditorGUILayout.TextField("Логин", adminLogin);
        if (GUILayout.Button("Сделать админом", GUILayout.Width(130)) && adminLogin.Trim() != "") adminNote = s.Core.SetAdmin(adminLogin.Trim());
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("Админ видит в игре (Клан → Война) кнопки дней войны.", EditorStyles.miniLabel);

        var wars = s.Core.ActiveWars();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Клановые войны", EditorStyles.boldLabel);
        if (wars.Count == 0) EditorGUILayout.LabelField("Сейчас войн нет", EditorStyles.miniLabel);
        foreach (var w in wars)
        {
            EditorGUILayout.LabelField(w.Value);
            EditorGUILayout.BeginHorizontal();
            for (int d = 1; d <= WarRules.Days; d++)
                if (GUILayout.Button(d.ToString(), GUILayout.Width(28))) adminNote = s.Core.SetWarDay(w.Key, d);
            if (GUILayout.Button("Закончить")) adminNote = s.Core.SetWarDay(w.Key, WarRules.Days + 1);
            EditorGUILayout.EndHorizontal();
        }
        if (!string.IsNullOrEmpty(adminNote)) EditorGUILayout.HelpBox(adminNote, MessageType.None);
    }
}
