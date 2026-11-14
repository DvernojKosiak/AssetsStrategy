using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Окно Strategyca → Телефон: всё, чтобы собрать игру для Android в пару нажатий.
///  1. «Адрес сервера» - вшивается в игру (Assets/Resources/strategyca_server.txt), на телефоне его не нужно вводить.
///  2. «Настроить Android» - платформа Android, только альбомная ориентация, IL2CPP + ARM64, интернет,
///     разрешён http (на случай сервера без HTTPS), ввод «Both» (касания доходят и до окон IMGUI).
///  3. «Собрать APK» - файл Builds/Strategyca.apk, его можно скинуть на телефон и установить.
///     «Собрать AAB» - файл для Google Play (нужен ключ подписи: Player Settings → Publishing Settings).
/// </summary>
public class PhoneBuild : EditorWindow
{
    const string ServerFile = "Assets/Resources/strategyca_server.txt";
    const string DefaultId = "com.cineticagames.strategyca";

    string server;
    string log = "";
    Vector2 scroll;

    [MenuItem("Strategyca/Телефон (Android)")]
    static void Open()
    {
        var w = GetWindow<PhoneBuild>("Телефон");
        w.minSize = new Vector2(460f, 420f);
    }

    void OnEnable() { server = ReadServer(); }

    static string ReadServer()
    {
        return File.Exists(ServerFile) ? File.ReadAllText(ServerFile).Trim() : "";
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("1. Адрес сервера для телефона", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Например https://strategyca.duckdns.org или http://185.10.20.30:7777. " +
            "Он вшивается в игру - на телефоне вводить не нужно (на экране входа его всё равно можно поменять). " +
            "Пусто - игра ищет сервер на этом компьютере (localhost), это только для Unity.", MessageType.None);
        server = EditorGUILayout.TextField("Адрес", server);
        if (GUILayout.Button("Сохранить адрес")) SaveServer(server);
        string cur = ReadServer();
        EditorGUILayout.LabelField("Сейчас вшит:", cur == "" ? "(нет - localhost)" : cur);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("2. Настройки Android", EditorStyles.boldLabel);
        if (GUILayout.Button("Настроить Android", GUILayout.Height(30f))) EditorApplication.delayCall += () => SetupAndroid();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("3. Сцена игры", EditorStyles.boldLabel);
        // На телефоне запускается ПЕРВАЯ сцена из списка сборки, а не та, что открыта в Unity
        string first = FirstBuildScene();
        string open = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        EditorGUILayout.LabelField("Открыта в Unity:", open == "" ? "(сцена не сохранена)" : open);
        EditorGUILayout.LabelField("Запустится на телефоне:", first == "" ? "(список сборки пуст)" : first);
        if (open != "" && open != first)
        {
            EditorGUILayout.HelpBox("На телефоне запустится не та сцена, что открыта в Unity. Если игра у вас - открытая сцена, нажмите кнопку ниже.", MessageType.Warning);
            if (GUILayout.Button("Собирать открытую сцену (" + Path.GetFileNameWithoutExtension(open) + ")", GUILayout.Height(26f)))
                EditorApplication.delayCall += () => UseOpenScene(open);
        }

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("4. Сборка", EditorStyles.boldLabel);
        EditorUserBuildSettings.development = EditorGUILayout.ToggleLeft("Для проверки: показывать ошибки прямо на экране телефона (только APK; в AAB для Google Play - всегда выключено)", EditorUserBuildSettings.development);
        // Сборка - после OnGUI, иначе Unity пишет ошибку EndLayoutGroup
        if (GUILayout.Button("Собрать APK (поставить на свой телефон)", GUILayout.Height(30f))) EditorApplication.delayCall += () => Build(false);
        if (GUILayout.Button("Собрать AAB (для Google Play)")) EditorApplication.delayCall += () => Build(true);

        if (log != "")
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(log, MessageType.Info);
        }
        EditorGUILayout.EndScrollView();
    }

    static string FirstBuildScene()
    {
        foreach (var sc in EditorBuildSettings.scenes) if (sc.enabled && File.Exists(sc.path)) return sc.path;
        return "";
    }

    static void UseOpenScene(string path)
    {
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        Debug.Log("Strategyca: в сборке одна сцена - " + path);
        foreach (var w in Resources.FindObjectsOfTypeAll<PhoneBuild>()) { w.log = "Сцена для телефона: " + path; w.Repaint(); }
    }

    static void SaveServer(string a)
    {
        a = (a ?? "").Trim().TrimEnd('/');
        Directory.CreateDirectory(Path.GetDirectoryName(ServerFile));
        if (a == "") { if (File.Exists(ServerFile)) AssetDatabase.DeleteAsset(ServerFile); }
        else File.WriteAllText(ServerFile, a);
        AssetDatabase.Refresh();
        Debug.Log(a == "" ? "Strategyca: адрес сервера убран (localhost)" : "Strategyca: в игру вшит сервер " + a);
    }

    [MenuItem("Strategyca/Телефон: настроить Android")]
    public static void SetupAndroidMenu() { SetupAndroid(); }

    static string SetupAndroid()
    {
        var sb = new System.Text.StringBuilder();
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            if (EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android)) sb.AppendLine("• Платформа: Android");
            else sb.AppendLine("• Не удалось переключить на Android: установите модуль Android Build Support (Unity Hub → Installs → Add modules)");
        }

        // Имя пакета в Google Play - всегда com.cineticagames.strategyca (final-1). После первой загрузки в Play Console
        // его уже не поменять, поэтому ставим его принудительно, даже если в Player Settings было другое
        string id = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        if (id != DefaultId)
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, DefaultId);
            sb.AppendLine("• Имя пакета: " + DefaultId + (string.IsNullOrEmpty(id) ? "" : " (было " + id + ")"));
        }
        else sb.AppendLine("• Имя пакета: " + DefaultId + " (уже стоит)");
        PlayerSettings.productName = GameName;
        sb.AppendLine("• Иконка: " + AppIcon.Apply());

        // Только альбомная ориентация
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        sb.AppendLine("• Ориентация: только альбомная");

        // Google Play принимает только 64 бита - нужен IL2CPP
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        sb.AppendLine("• IL2CPP, ARM64");

        if ((int)PlayerSettings.Android.minSdkVersion < 24) PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)24;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.Android.startInFullscreen = true;
        PlayerSettings.Android.renderOutsideSafeArea = true;   // карта под вырезом камеры, окна - в безопасной зоне (UiKit)
        sb.AppendLine("• Android 7.0+, интернет, весь экран");

        // Через сериализацию - эти поля есть не во всех версиях Unity
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets != null && assets.Length > 0)
        {
            var so = new SerializedObject(assets[0]);
            var http = so.FindProperty("insecureHttpOption");
            if (http != null && http.intValue != 2) { http.intValue = 2; sb.AppendLine("• Разрешён http:// (если сервер без HTTPS)"); }
            var input = so.FindProperty("activeInputHandler");
            if (input != null && input.intValue != 2)
            {
                input.intValue = 2;
                sb.AppendLine("• Ввод: Both (старый + новый). закройте и снова откройте проект в Unity, чтобы это вступило в силу");
            }
            so.ApplyModifiedProperties();
        }
        AssetDatabase.SaveAssets();
        if (ReadServer() == "") sb.AppendLine("! Адрес сервера не вшит - на телефоне его придётся ввести на экране входа (✎ под кнопкой «Войти»)");
        string res = "Готово:\n" + sb.ToString().TrimEnd();
        Debug.Log("Strategyca: " + res);
        foreach (var w in Resources.FindObjectsOfTypeAll<PhoneBuild>()) { w.log = res; w.Repaint(); }
        return res;
    }

    [MenuItem("Strategyca/Телефон: собрать APK")]
    public static void BuildApkMenu() { Build(false); }

    const string GameName = "Strategyca";

    static void Build(bool bundle)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) SetupAndroid();
        // Название под иконкой на телефоне - имя игры, а не имя папки проекта
        if (PlayerSettings.productName != GameName) PlayerSettings.productName = GameName;
        // Имя пакета Google Play - всегда одно (final-1)
        if (PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android) != DefaultId)
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, DefaultId);
            Debug.Log("Strategyca: имя пакета " + DefaultId);
        }
        AppIcon.Apply();   // иконка игры - компас (Assets/Icon)
        var scenes = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes) if (s.enabled && File.Exists(s.path)) scenes.Add(s.path);
        if (scenes.Count == 0)
        {
            EditorUtility.DisplayDialog("Strategyca", "В списке сборки нет сцен. File → Build Profiles (Build Settings) → Add Open Scenes, затем соберите снова.", "Понятно");
            return;
        }
        // Для Google Play: никакой «Development Console» и отладки (Play такие сборки не принимает),
        // и обязательно свой ключ подписи (keystore) - тестовым ключом Unity Play подписанное не примет
        if (bundle)
        {
            EditorUserBuildSettings.development = false;
            if (!PlayerSettings.Android.useCustomKeystore || string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
            {
                EditorUtility.DisplayDialog("Strategyca",
                    "Для Google Play нужен свой ключ подписи.\n\nEdit → Project Settings → Player → Android → Publishing Settings → " +
                    "Keystore Manager → Create New (запомните пароли и сохраните файл .keystore в надёжном месте — без него обновления не выпустить). " +
                    "Потом снова нажмите «Собрать AAB».", "Понятно");
                SettingsService.OpenProjectSettings("Project/Player");
                return;
            }
            if (string.IsNullOrEmpty(PlayerSettings.Android.keystorePass) || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
            {
                EditorUtility.DisplayDialog("Strategyca",
                    "Unity не помнит пароли ключа после перезапуска. Project Settings → Player → Android → Publishing Settings: " +
                    "введите пароль keystore и пароль ключа (alias), потом снова «Собрать AAB».", "Понятно");
                SettingsService.OpenProjectSettings("Project/Player");
                return;
            }
        }
        Directory.CreateDirectory("Builds");
        // Google Play принимает обновление, только если номер версии больше прошлого - растим его сами
        if (bundle)
        {
            // ... и не меньше номера сборки игры (Api.Build): сервер требует сборку по этому номеру (final-9)
            int cur = PlayerSettings.Android.bundleVersionCode;
            PlayerSettings.Android.bundleVersionCode = cur < Api.Build ? Api.Build : cur + 1;
            AssetDatabase.SaveAssets();
            Debug.Log("Strategyca: номер версии для Google Play - " + PlayerSettings.Android.bundleVersionCode);
        }
        EditorUserBuildSettings.buildAppBundle = bundle;
        string path = bundle ? "Builds/Strategyca.aab" : "Builds/Strategyca.apk";
        var opt = new BuildPlayerOptions { scenes = scenes.ToArray(), locationPathName = path, target = BuildTarget.Android, options = !bundle && EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None };
        var report = BuildPipeline.BuildPlayer(opt);
        bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        string msg = ok ? $"Собрано: {Path.GetFullPath(path)} ({new FileInfo(path).Length / (1024 * 1024)} МБ)" + (bundle ? $", номер версии {PlayerSettings.Android.bundleVersionCode}" : "")
                        : "Сборка не удалась - ошибки в Console (красные строки).";
        Debug.Log("Strategyca: " + msg);
        foreach (var w in Resources.FindObjectsOfTypeAll<PhoneBuild>()) { w.log = msg; w.Repaint(); }
        if (ok) EditorUtility.RevealInFinder(path);
    }
}
