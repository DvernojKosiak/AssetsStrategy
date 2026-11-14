using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// ---------- Аккаунты: данные и связь с сервером ----------
// Аккаунты и города хранит сервер (Assets/Online/Server). Игра говорит с ним через IAccountBackend:
// OnlineAccountBackend шлёт HTTP-запросы. Все ответы приходят в callback на главном потоке.

/// <summary>Всё общение игры с сервером аккаунтов и городов. Ответы - в callback.</summary>
public interface IAccountBackend
{
    /// <summary>Подпись для игрока: куда подключаемся.</summary>
    string Name { get; }

    void Ping(Action<AccountResult> done);
    void Register(string login, string displayName, string password, Action<AccountResult> done);
    void Login(string login, string password, Action<AccountResult> done);
    /// <summary>Вход по сохранённому токену (без пароля).</summary>
    void Resume(string token, Action<AccountResult> done);
    void Logout(string token, Action done);
    /// <summary>Сохранить изменения профиля (имя, время в игре).</summary>
    void SaveProfile(string token, AccountProfile profile, Action<bool> done);
    void ChangePassword(string token, string oldPassword, string newPassword, Action<AccountResult> done);

    /// <summary>Город игрока. cityJson == null - города ещё нет (новый игрок).</summary>
    void LoadCity(string token, Action<ApiResponse> done);   // city (null - нет города), reports (уже вычтены)
    /// <summary>Сохранить город; acks - отчёты о набегах, которые игра уже учла. В ответе - новые отчёты.</summary>
    void SaveCity(string token, string cityJson, List<string> acks, Action<ApiResponse> done);

    // ---------- Набеги на игроков ----------
    /// <summary>Найти деревню другого игрока: в ответе raidId и её город (city).</summary>
    void FindOpponent(string token, Action<ApiResponse> done);
    void StartRaid(string token, string raidId, Action<ApiResponse> done);
    void FinishRaid(string token, ApiRequest result, Action<ApiResponse> done);
    // ---------- Рынок между игроками ----------
    void MarketList(string token, Action<ApiResponse> done);
    void MarketPost(string token, MarketLot lot, Action<ApiResponse> done);
    void MarketBuy(string token, string lotId, Action<ApiResponse> done);
    void MarketCancel(string token, string lotId, Action<ApiResponse> done);

    /// <summary>Клан: любой запрос клана (Api.Clan*) - поля в req, токен подставляется.</summary>
    void Clan(string path, string token, ApiRequest req, Action<ApiResponse> done);

    /// <summary>Нападающий: отправить снимок боя, получить приказы защитника.</summary>
    void BattlePush(string token, string raidId, BattleSnapshot snapshot, Action<ApiResponse> done);
    /// <summary>Защитник: отправить приказы, получить снимок боя у своей деревни.</summary>
    void BattleWatch(string token, string lastRaidId, bool live, List<BattleCommand> commands, Action<ApiResponse> done);

    /// <summary>Любой запрос по пути (Api.*): поля в req, токен подставляется. Так ходят запросы живого набега (Api.Siege*).</summary>
    void Call(string path, string token, ApiRequest req, Action<ApiResponse> done);
}

/// <summary>Сервер Strategyca по HTTP (адрес вида http://localhost:7777).</summary>
public class OnlineAccountBackend : IAccountBackend
{
    const int TimeoutSeconds = 10;
    readonly string address;

    public OnlineAccountBackend(string address)
    {
        address = (address ?? "").Trim().TrimEnd('/');
        if (address != "" && !address.StartsWith("http://") && !address.StartsWith("https://")) address = "http://" + address;
        this.address = address;
    }

    public string Address => address;
    public string Name => "Сервер " + address;

    // Один запрос: POST JSON, ответ ApiResponse. Ошибки связи превращаются в понятный текст.
    void Send(string path, ApiRequest req, Action<ApiResponse> done)
    {
        UnityWebRequest www;
        try
        {
            www = new UnityWebRequest(address + path, "POST");
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(req)));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = TimeoutSeconds;
        }
        catch (Exception e)
        {
            done(ApiResponse.Fail("Неверный адрес сервера: " + e.Message));
            return;
        }

        UnityWebRequestAsyncOperation op = www.SendWebRequest();
        op.completed += _ =>
        {
            ApiResponse res;
            if (www.result == UnityWebRequest.Result.ConnectionError)
                res = ApiResponse.Fail("Нет связи с сервером. Он запущен? (" + address + ")");
            else
            {
                res = null;
                try { res = JsonUtility.FromJson<ApiResponse>(www.downloadHandler.text); }
                catch (Exception) { }
                if (res == null) res = ApiResponse.Fail($"Сервер ответил ошибкой ({www.responseCode})");
            }
            www.Dispose();
            WorldClock.Sync(res);
            CityShield.Sync(res);   // щит города приходит в каждом ответе
            UpdateGate.Check(res);  // сервер требует новую сборку - экран «Обновите игру» (final-8)
            done(res);
        };
    }

    static AccountResult ToResult(ApiResponse r)
    {
        return r.ok ? AccountResult.Ok(r.profile, r.token) : AccountResult.Fail(r.error);
    }

    public void Ping(Action<AccountResult> done)
    {
        Send(Api.Ping, new ApiRequest(), r => done(r.ok ? new AccountResult { ok = true, error = r.serverName } : AccountResult.Fail(r.error)));
    }

    public void Register(string login, string displayName, string password, Action<AccountResult> done)
    {
        string err = AccountRules.CheckLogin(login) ?? AccountRules.CheckName(displayName) ?? AccountRules.CheckPassword(password);
        if (err != null) { done(AccountResult.Fail(err)); return; }
        Send(Api.Register, new ApiRequest { login = login, displayName = displayName, password = password }, r => done(ToResult(r)));
    }

    public void Login(string login, string password, Action<AccountResult> done)
    {
        Send(Api.Login, new ApiRequest { login = login, password = password }, r => done(ToResult(r)));
    }

    public void Resume(string token, Action<AccountResult> done)
    {
        Send(Api.Resume, new ApiRequest { token = token }, r => done(ToResult(r)));
    }

    public void Logout(string token, Action done)
    {
        Send(Api.Logout, new ApiRequest { token = token }, r => { if (done != null) done(); });
    }

    public void SaveProfile(string token, AccountProfile profile, Action<bool> done)
    {
        Send(Api.SaveProfile, new ApiRequest { token = token, profile = profile }, r => { if (done != null) done(r.ok); });
    }

    public void ChangePassword(string token, string oldPassword, string newPassword, Action<AccountResult> done)
    {
        string err = AccountRules.CheckPassword(newPassword);
        if (err != null) { done(AccountResult.Fail(err)); return; }
        Send(Api.ChangePassword, new ApiRequest { token = token, password = oldPassword, newPassword = newPassword }, r => done(ToResult(r)));
    }

    public void LoadCity(string token, Action<ApiResponse> done)
    {
        Send(Api.LoadCity, new ApiRequest { token = token }, r =>
        {
            if (r.ok && !r.hasCity) r.city = null;
            done(r);
        });
    }

    public void SaveCity(string token, string cityJson, List<string> acks, Action<ApiResponse> done)
    {
        Send(Api.SaveCity, new ApiRequest { token = token, city = cityJson, acks = acks }, r => { if (done != null) done(r); });
    }

    public void FindOpponent(string token, Action<ApiResponse> done)
    {
        Send(Api.FindOpponent, new ApiRequest { token = token }, done);
    }

    public void StartRaid(string token, string raidId, Action<ApiResponse> done)
    {
        Send(Api.StartRaid, new ApiRequest { token = token, raidId = raidId }, done);
    }

    public void MarketList(string token, Action<ApiResponse> done) { Send(Api.MarketList, new ApiRequest { token = token }, done); }
    public void MarketPost(string token, MarketLot lot, Action<ApiResponse> done) { Send(Api.MarketPost, new ApiRequest { token = token, lot = lot }, done); }
    public void MarketBuy(string token, string lotId, Action<ApiResponse> done) { Send(Api.MarketBuy, new ApiRequest { token = token, lotId = lotId }, done); }
    public void MarketCancel(string token, string lotId, Action<ApiResponse> done) { Send(Api.MarketCancel, new ApiRequest { token = token, lotId = lotId }, done); }

    public void Clan(string path, string token, ApiRequest req, Action<ApiResponse> done)
    {
        req.token = token;
        Send(path, req, done);
    }

    public void Call(string path, string token, ApiRequest req, Action<ApiResponse> done)
    {
        req.token = token;
        Send(path, req, done);
    }

    public void BattlePush(string token, string raidId, BattleSnapshot snapshot, Action<ApiResponse> done)
    {
        Send(Api.BattlePush, new ApiRequest { token = token, raidId = raidId, battle = snapshot }, done);
    }

    public void BattleWatch(string token, string lastRaidId, bool live, List<BattleCommand> commands, Action<ApiResponse> done)
    {
        Send(Api.BattleWatch, new ApiRequest { token = token, raidId = lastRaidId, live = live, commands = commands }, done);
    }

    public void FinishRaid(string token, ApiRequest result, Action<ApiResponse> done)
    {
        result.token = token;
        Send(Api.FinishRaid, result, r => { if (done != null) done(r); });
    }
}
