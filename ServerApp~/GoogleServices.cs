using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using UnityEngine;

/// <summary>
/// Связь сервера с Google (только в отдельной программе сервера; сервер в Unity работает без этого):
///  - вход через Google Play Games: одноразовый код из игры меняется на игрока (playerId, имя);
///  - push-уведомления через Firebase Cloud Messaging (HTTP v1).
/// Что нужно, берётся из server-config.json (googleClientId, googleClientSecret) и файла ключа Firebase
/// (firebase-service-account.json) в папке данных сервера. Нет настроек - эта часть просто выключена.
/// </summary>
public static class GoogleServices
{
    static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    static ServerCore core;
    static string clientId, clientSecret;

    // Firebase
    static string projectId, clientEmail, tokenUri;
    static RSA key;
    static string accessToken;
    static DateTime accessUntil;
    static readonly BlockingCollection<PushMessage> queue = new BlockingCollection<PushMessage>(new ConcurrentQueue<PushMessage>(), 5000);
    static Thread worker;

    public static void Install(ServerCore c)
    {
        core = c;
        ServerConfig cfg = c.Config ?? new ServerConfig();
        string cfgPath = Path.Combine(c.DataDir, "server-config.json");

        clientId = (cfg.googleClientId ?? "").Trim();
        clientSecret = (cfg.googleClientSecret ?? "").Trim();
        if (clientId != "" && clientSecret != "")
        {
            ServerCore.GoogleResolver = Resolve;
            Console.WriteLine("Вход через Google Play Games: включён.");
        }
        else Console.WriteLine($"Вход через Google Play Games: выключен (впишите googleClientId и googleClientSecret в {cfgPath}).");

        string keyPath = Path.Combine(c.DataDir, string.IsNullOrWhiteSpace(cfg.firebaseKeyFile) ? "firebase-service-account.json" : cfg.firebaseKeyFile.Trim());
        if (!File.Exists(keyPath)) { Console.WriteLine($"Push-уведомления: выключены (положите ключ Firebase сюда: {keyPath})."); return; }
        try
        {
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(keyPath)))
            {
                JsonElement r = doc.RootElement;
                projectId = r.GetProperty("project_id").GetString();
                clientEmail = r.GetProperty("client_email").GetString();
                tokenUri = r.TryGetProperty("token_uri", out JsonElement tu) ? tu.GetString() : "https://oauth2.googleapis.com/token";
                key = RSA.Create();
                key.ImportFromPem(r.GetProperty("private_key").GetString());
            }
            ServerCore.PushSender = m => { if (m != null) queue.TryAdd(m); };
            worker = new Thread(Work) { IsBackground = true, Name = "Push" };
            worker.Start();
            Console.WriteLine($"Push-уведомления: включены (проект Firebase {projectId}).");
        }
        catch (Exception e) { Console.WriteLine("Push-уведомления: не удалось прочитать ключ Firebase: " + e.Message); }
    }

    // ---------- Google Play Games ----------

    static GoogleIdentity Resolve(string code)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code, ["client_id"] = clientId, ["client_secret"] = clientSecret,
            ["redirect_uri"] = "", ["grant_type"] = "authorization_code",
        });
        HttpResponseMessage res = http.PostAsync("https://oauth2.googleapis.com/token", form).GetAwaiter().GetResult();
        string text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        string token;
        using (JsonDocument doc = JsonDocument.Parse(text))
        {
            if (!res.IsSuccessStatusCode || !doc.RootElement.TryGetProperty("access_token", out JsonElement at))
            {
                Debug.LogWarning("[Google] Код входа не принят: " + text);
                return new GoogleIdentity { error = "Google не подтвердил вход. Попробуйте ещё раз" };
            }
            token = at.GetString();
        }

        var req = new HttpRequestMessage(HttpMethod.Get, "https://games.googleapis.com/games/v1/players/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        res = http.SendAsync(req).GetAwaiter().GetResult();
        text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode)
        {
            Debug.LogWarning("[Google] Не удалось узнать игрока: " + text);
            return new GoogleIdentity { error = "Google Play Games не ответил. Попробуйте ещё раз" };
        }
        using (JsonDocument doc = JsonDocument.Parse(text))
        {
            JsonElement r = doc.RootElement;
            string id = r.TryGetProperty("playerId", out JsonElement p) ? p.GetString() : null;
            string name = r.TryGetProperty("displayName", out JsonElement n) ? n.GetString() : "";
            if (string.IsNullOrEmpty(id)) return new GoogleIdentity { error = "Google не прислал игрока" };
            return new GoogleIdentity { playerId = id, name = name };
        }
    }

    // ---------- Push (Firebase Cloud Messaging) ----------

    static void Work()
    {
        foreach (PushMessage m in queue.GetConsumingEnumerable())
        {
            try { Send(m); }
            catch (Exception e) { Debug.LogWarning("[Push] Не отправлено: " + e.Message); }
        }
    }

    static string B64(byte[] b) { return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }

    static string AccessToken()
    {
        if (accessToken != null && DateTime.UtcNow < accessUntil) return accessToken;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string head = B64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        string claims = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = clientEmail, ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
            ["aud"] = tokenUri, ["iat"] = now, ["exp"] = now + 3600,
        }));
        string unsigned = head + "." + claims;
        string jwt = unsigned + "." + B64(key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = jwt,
        });
        HttpResponseMessage res = http.PostAsync(tokenUri, form).GetAwaiter().GetResult();
        string text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using (JsonDocument doc = JsonDocument.Parse(text))
        {
            if (!res.IsSuccessStatusCode || !doc.RootElement.TryGetProperty("access_token", out JsonElement at))
                throw new Exception("ключ Firebase не принят: " + text);
            accessToken = at.GetString();
            int sec = doc.RootElement.TryGetProperty("expires_in", out JsonElement ex) ? ex.GetInt32() : 3600;
            accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, sec - 120));
        }
        return accessToken;
    }

    static void Send(PushMessage m)
    {
        var android = new Dictionary<string, object>
        {
            ["priority"] = "HIGH",
            ["notification"] = new Dictionary<string, object>
            {
                ["tag"] = m.tag ?? "strategyca",
                ["icon"] = "ic_stat_strategyca",   // белый значок из Assets/Plugins/Android/StrategycaNotify.androidlib
                ["color"] = "#F4CC6B",
                ["sound"] = "default",
            },
        };
        var body = new Dictionary<string, object>
        {
            ["message"] = new Dictionary<string, object>
            {
                ["token"] = m.token,
                ["notification"] = new Dictionary<string, object> { ["title"] = m.title, ["body"] = m.body },
                ["android"] = android,
                ["data"] = new Dictionary<string, string> { ["kind"] = m.kind.ToString() },
            },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken());
        HttpResponseMessage res = http.SendAsync(req).GetAwaiter().GetResult();
        if (res.IsSuccessStatusCode) return;
        string text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        // Приложение удалили или токен устарел - больше на этот телефон не пишем
        if (res.StatusCode == HttpStatusCode.NotFound || text.Contains("UNREGISTERED") || (res.StatusCode == HttpStatusCode.BadRequest && text.Contains("registration token")))
        {
            core?.DropPushToken(m.token);
            return;
        }
        if (res.StatusCode == HttpStatusCode.Unauthorized) accessToken = null;
        Debug.LogWarning($"[Push] Firebase ответил {(int)res.StatusCode}: {text}");
    }
}
