using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Сам включает код Google Play, когда в проект добавлен нужный плагин (для сборки Android):
///   STRATEGYCA_GPGS - Google Play Games plugin for Unity (вход через Google Play Games, PlayGames.cs);
///   STRATEGYCA_FCM  - Firebase Cloud Messaging (push-уведомления, PushNotifications.cs);
///   STRATEGYCA_IAP  - Unity In-App Purchasing (магазин марок, StampShop.cs);
///   STRATEGYCA_LOCALNOTIF - Mobile Notifications (уведомления о стройках и армии без Firebase, LocalNotifications.cs).
/// Символы только добавляются (Project Settings → Player → Android → Scripting Define Symbols), сами не убираются:
/// если плагин удалили - уберите символ там же.
/// </summary>
[InitializeOnLoad]
static class GooglePlayDefines
{
    struct Plugin { public string define, type, script; }

    static readonly Plugin[] Plugins =
    {
        new Plugin { define = "STRATEGYCA_GPGS", type = "GooglePlayGames.PlayGamesPlatform", script = "PlayGamesPlatform" },
        new Plugin { define = "STRATEGYCA_FCM", type = "Firebase.Messaging.FirebaseMessaging", script = "FirebaseMessaging" },
        new Plugin { define = "STRATEGYCA_IAP", type = "UnityEngine.Purchasing.UnityPurchasing", script = "UnityPurchasing" },
        new Plugin { define = "STRATEGYCA_LOCALNOTIF", type = "Unity.Notifications.Android.AndroidNotificationCenter", script = "AndroidNotificationCenter" },
    };

    static GooglePlayDefines() { EditorApplication.delayCall += Sync; }

    [MenuItem("Strategyca/Google Play: проверить плагины")]
    static void SyncFromMenu()
    {
        Sync();
        var have = new List<string>();
        foreach (var p in Plugins) if (Present(p)) have.Add(p.define);
        EditorUtility.DisplayDialog("Google Play", have.Count == 0
            ? "Плагины Google Play Games, Firebase Messaging и In-App Purchasing не найдены. Как их добавить - в ЧТО_ИЗМЕНЕНО.md пакета google-play-1."
            : "Включено для Android: " + string.Join(", ", have), "OK");
    }

    static bool Present(Plugin p)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { if (asm.GetType(p.type, false) != null) return true; }
            catch (Exception) { }
        }
        // Сборка плагина может быть только для Android - тогда ищем его скрипт или библиотеку
        foreach (string guid in AssetDatabase.FindAssets(p.script))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (file == p.script || file == p.type.Substring(0, p.type.LastIndexOf('.'))) return true;
        }
        return false;
    }

    static void Sync()
    {
        NamedBuildTarget target = NamedBuildTarget.Android;
        PlayerSettings.GetScriptingDefineSymbols(target, out string[] current);
        var list = new List<string>(current);
        var added = new List<string>();
        foreach (var p in Plugins)
            if (!list.Contains(p.define) && Present(p)) { list.Add(p.define); added.Add(p.define); }
        if (added.Count == 0) return;
        PlayerSettings.SetScriptingDefineSymbols(target, list.ToArray());
        Debug.Log("[Strategyca] Найдены плагины Google Play, для Android включено: " + string.Join(", ", added));
    }
}
