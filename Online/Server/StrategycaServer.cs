using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// HTTP-сервер Strategyca. Принимает запросы игры (POST JSON, см. Api) и передаёт их в ServerCore.
/// Запускается из редактора (меню Strategyca → Сервер, или сам при открытии Unity)
/// или отдельной программой StrategycaServer (Assets/ServerApp~), тогда Unity для игры по сети не нужен.
/// Сам разбирает HTTP поверх TCP: не нужны права администратора, и принимает запросы
/// с любым адресом (через туннель вроде playit.gg адрес у запроса чужой).
/// </summary>
public class StrategycaServer
{
    public static StrategycaServer Running { get; private set; }

    const int HeaderLimit = 16 * 1024;
    const int ReadTimeoutMs = 15000;

    readonly List<TcpListener> listeners = new List<TcpListener>();
    readonly ServerCore core;
    readonly int port;
    readonly bool lan;
    volatile bool stopped;
    int requests;
    Timer warTimer, pushTimer;

    public int Port => port;
    public ServerCore Core => core;
    public int Requests => requests;
    public string LastError { get; private set; }

    StrategycaServer(int port, string dataDir, bool lan)
    {
        this.port = port;
        this.lan = lan;
        core = new ServerCore(dataDir);
    }

    /// <summary>
    /// Запустить сервер. dataDir - папка с аккаунтами и городами. lan - принимать подключения
    /// и с других компьютеров сети (иначе только с этого; туннелю этого хватает). null - не удалось (см. error).
    /// </summary>
    public static StrategycaServer Start(int port, string dataDir, out string error, bool lan = false)
    {
        error = null;
        if (Running != null) return Running;
        StrategycaServer s = null;
        try
        {
            s = new StrategycaServer(port, dataDir, lan);
            s.Listen(lan ? IPAddress.Any : IPAddress.Loopback, true);
            s.Listen(lan ? IPAddress.IPv6Any : IPAddress.IPv6Loopback, false);   // "localhost" бывает и IPv6
            Running = s;
            foreach (TcpListener l in s.listeners) s.AcceptLoop(l);
            // Клановые войны идут сами, даже если никто не присылает запросов
            s.warTimer = new Timer(_ => s.TickWars(), null, 200, (int)(WarRules.Tick * 1000));
            // Push: щит скоро кончится, стройки готовы
            s.pushTimer = new Timer(_ => s.TickPush(), null, 5000, 15000);
            Debug.Log($"[Сервер] Запущен: http://localhost:{port}  (данные: {dataDir}, игроков: {s.core.PlayerCount})");
            Debug.Log($"[Сервер] Жалобы игроков (модерация): http://localhost:{port}{Api.WebAdmin}?key={s.core.Config.adminKey}");
            return s;
        }
        catch (Exception e)
        {
            error = e is SocketException se && se.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Порт {port} уже занят. Возможно, сервер уже запущен (в Unity или отдельной программой)."
                : e.Message;
            if (s != null) s.CloseAll();
            Debug.LogWarning("[Сервер] Не удалось запустить: " + error);
            return null;
        }
    }

    void Listen(IPAddress ip, bool required)
    {
        try
        {
            var l = new TcpListener(ip, port);
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) l.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            l.Start();
            listeners.Add(l);
        }
        catch (Exception)
        {
            if (required) throw;
        }
    }

    public static void Stop()
    {
        StrategycaServer s = Running;
        Running = null;
        if (s == null) return;
        s.CloseAll();
        Debug.Log("[Сервер] Остановлен");
    }

    void TickWars()
    {
        if (stopped) return;
        try { core.TickWars(); }
        catch (Exception e) { LastError = e.Message; Debug.LogWarning("[Сервер] Ошибка войны: " + e); }
    }

    void TickPush()
    {
        if (stopped) return;
        try { core.TickPush(); }
        catch (Exception e) { LastError = e.Message; Debug.LogWarning("[Сервер] Ошибка уведомлений: " + e); }
    }

    void CloseAll()
    {
        stopped = true;
        if (warTimer != null) { warTimer.Dispose(); warTimer = null; }
        if (pushTimer != null) { pushTimer.Dispose(); pushTimer = null; }
        foreach (TcpListener l in listeners)
            try { l.Stop(); } catch (Exception) { }
        listeners.Clear();
    }

    async void AcceptLoop(TcpListener l)
    {
        while (!stopped)
        {
            TcpClient client;
            try { client = await l.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch (Exception) { if (stopped) return; continue; }
            _ = Task.Run(() => Serve(client));
        }
    }

    // ---------- Один запрос ----------

    void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = ReadTimeoutMs;
                client.SendTimeout = ReadTimeoutMs;
                NetworkStream stream = client.GetStream();
                Interlocked.Increment(ref requests);
                int code;
                string body;
                location = null;
                try { Process(stream, out code, out body); }
                catch (IOException) { return; }   // клиент отвалился
                catch (Exception e)
                {
                    LastError = e.Message;
                    Debug.LogWarning("[Сервер] Ошибка запроса: " + e);
                    code = 500;
                    body = JsonUtility.ToJson(ApiResponse.Fail("Ошибка сервера"));
                }
                Write(stream, code, body, body.StartsWith("{") ? "application/json" : "text/html", location);
            }
            catch (Exception) { }
        }
    }

    void Process(NetworkStream stream, out int code, out string body)
    {
        // Заголовок запроса - до пустой строки
        var head = new List<byte>(1024);
        int b;
        while (true)
        {
            b = stream.ReadByte();
            if (b < 0) throw new IOException("closed");
            head.Add((byte)b);
            int n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n') break;
            if (n > HeaderLimit) { code = 431; body = Fail("Слишком большой запрос"); return; }
        }
        string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
        string[] first = lines[0].Split(' ');
        string method = first.Length > 0 ? first[0] : "";
        string path = first.Length > 1 ? first[1] : "/";
        int q = path.IndexOf('?');
        var args = new Dictionary<string, string>();
        if (q >= 0) { ParseForm(path.Substring(q + 1), args); path = path.Substring(0, q); }
        long length = 0;
        bool expect = false;
        foreach (string line in lines)
        {
            int c = line.IndexOf(':');
            if (c <= 0) continue;
            string name = line.Substring(0, c).Trim(), value = line.Substring(c + 1).Trim();
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) long.TryParse(value, out length);
            if (name.Equals("Expect", StringComparison.OrdinalIgnoreCase) && value.StartsWith("100", StringComparison.Ordinal)) expect = true;
        }

        // Открыли адрес сервера в браузере: страницы (политика, правила, удаление аккаунта, модерация) или «Сервер работает»
        if (method == "GET" || method == "HEAD")
        {
            WebReply page = core.HandleWeb("GET", path, args);
            if (page == null) { code = 200; body = StatusPage(); return; }
            code = page.code; body = page.body; location = page.location;
            return;
        }
        if (method != "POST") { code = 405; body = Fail("Нужен POST"); return; }
        if (length < 0 || length > Api.MaxCityJson + 64 * 1024) { code = 413; body = Fail("Слишком большой запрос"); return; }

        if (expect)   // большие запросы игра шлёт после «можно» от сервера
        {
            byte[] go = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
            stream.Write(go, 0, go.Length);
        }
        byte[] data = new byte[length];
        int got = 0;
        while (got < length)
        {
            int r = stream.Read(data, got, (int)length - got);
            if (r <= 0) throw new IOException("closed");
            got += r;
        }
        string text = Encoding.UTF8.GetString(data);

        // Форма со страницы сервера (удаление аккаунта)
        if (!path.StartsWith("/api/"))
        {
            ParseForm(text, args);
            WebReply page = core.HandleWeb("POST", path, args);
            if (page == null) { code = 404; body = StatusPage(); return; }
            code = page.code; body = page.body; location = page.location;
            return;
        }

        ApiRequest req = null;
        try { req = JsonUtility.FromJson<ApiRequest>(text); }
        catch (Exception) { }
        if (req == null) { code = 400; body = Fail("Непонятный запрос"); return; }
        code = 200;
        if (req.version != Api.Version) { body = Fail("Версия игры не совпадает с сервером — обновите игру"); return; }
        if (core.TooOld(req)) { body = JsonUtility.ToJson(core.UpdateReply()); return; }   // принудительное обновление (final-8)
        body = JsonUtility.ToJson(core.Handle(path, req));
    }

    static string Fail(string error) { return JsonUtility.ToJson(ApiResponse.Fail(error)); }

    [ThreadStatic] static string location;   // перенаправление для страницы (код 303)

    // "a=1&b=%D0%B0" → словарь (адрес страницы и формы в браузере)
    static void ParseForm(string s, Dictionary<string, string> into)
    {
        if (string.IsNullOrEmpty(s)) return;
        foreach (string pair in s.Split('&'))
        {
            if (pair.Length == 0) continue;
            int e = pair.IndexOf('=');
            string k = e < 0 ? pair : pair.Substring(0, e), v = e < 0 ? "" : pair.Substring(e + 1);
            try { into[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); }
            catch (Exception) { }
        }
    }

    string StatusPage()
    {
        return "<!doctype html><meta charset=\"utf-8\"><title>Strategyca</title>"
            + "<body style=\"font-family:sans-serif;padding:24px\"><h2>Сервер Strategyca работает</h2>"
            + $"<p>Игроков: {core.PlayerCount}. Запросов: {requests}.</p>"
            + "<p>Этот адрес нужно вписать в игре: экран входа → «Сервер» → «Адрес сервера».</p>"
            + $"<p><a href=\"{Api.WebPrivacy}\">Политика конфиденциальности</a> · <a href=\"{Api.WebTerms}\">Правила игры</a> · <a href=\"{Api.WebDelete}\">Удалить аккаунт</a></p></body>";
    }

    static void Write(NetworkStream stream, int code, string body, string type, string location = null)
    {
        byte[] data = Encoding.UTF8.GetBytes(body);
        string head = $"HTTP/1.1 {code} {(code == 200 ? "OK" : code == 303 ? "See Other" : "Error")}\r\n"
            + (string.IsNullOrEmpty(location) ? "" : $"Location: {location}\r\n")
            + $"Content-Type: {type}; charset=utf-8\r\n"
            + $"Content-Length: {data.Length}\r\n"
            + "Connection: close\r\n\r\n";
        byte[] h = Encoding.ASCII.GetBytes(head);
        stream.Write(h, 0, h.Length);
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    /// <summary>
    /// Папка данных сервера по умолчанию (вне папки проекта, в GitHub не попадает).
    /// У Unity и у отдельной программы она одна и та же, так что аккаунты и города общие.
    /// </summary>
#if UNITY_5_3_OR_NEWER
    public static string DefaultDataDir => Path.Combine(Application.persistentDataPath, "Server");
#else
    public static string DefaultDataDir => ServerApp.DefaultDataDir;
#endif
}
