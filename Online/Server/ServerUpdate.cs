using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEngine;

/// <summary>
/// Принудительное обновление (final-8). В каждом запросе игра присылает номер своей сборки (ApiRequest.build, Api.Build).
/// На странице администратора есть кнопка «Требовать сборку N»: после неё сервер отвечает старым сборкам
/// «нужно обновить игру» (updateRequired) на любой запрос, а игра закрывается экраном с кнопкой «Обновить» (UpdateGate).
/// Старые сборки (до final-8) этого поля не знают и видят текст ошибки «Вышло обновление…».
/// Включайте, когда новая версия уже вышла в Google Play, иначе игрокам будет нечем обновиться.
/// </summary>
public partial class ServerCore
{
    partial class Db
    {
        public int minBuild;   // не ниже этой сборки (0 - любые)
    }

    public const string StoreUrl = "https://play.google.com/store/apps/details?id=com.cineticagames.strategyca";

    /// <summary>Сборка игры старше той, что требует сервер.</summary>
    public bool TooOld(ApiRequest r)
    {
        if (r == null) return false;
        lock (sync) return db != null && db.minBuild > 0 && r.build < db.minBuild;
    }

    public ApiResponse UpdateReply()
    {
        return new ApiResponse
        {
            ok = false, updateRequired = true, updateUrl = StoreUrl,
            error = "Вышло обновление Strategyca. Обновите игру в Google Play, чтобы продолжить",
        };
    }

    // Блок на главной странице администратора
    string AdminUpdateBox(string key)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"box\"><h2>Обновление игры</h2>");
        sb.Append(db.minBuild > 0
            ? $"<p>Сейчас сервер пускает только сборку <b>{db.minBuild}</b> и новее. Старые версии видят экран «Обновите игру».</p>"
            : "<p>Сейчас пускаются все версии игры.</p>");
        sb.Append($"<p class=\"muted\">Сборка этой программы сервера: {Api.Build}. Включайте, только когда эта версия уже вышла в Google Play (иначе игрокам нечем обновиться).</p>");
        if (db.minBuild != Api.Build)
            sb.Append($"<a class=\"btn\" href=\"{Api.WebAdmin}/update?key={key}&build={Api.Build}\">Требовать сборку {Api.Build}</a>");
        if (db.minBuild > 0)
            sb.Append($"<a class=\"btn ghost\" href=\"{Api.WebAdmin}/update?key={key}&build=0\">Пускать все версии</a>");
        sb.Append("</div>");
        return sb.ToString();
    }

    WebReply AdminUpdate(Dictionary<string, string> args)
    {
        if (!AdminOk(args)) return AdminDenied();
        if (int.TryParse(Arg(args, "build"), out int b) && b >= 0 && b <= Api.Build)
        {
            db.minBuild = b;
            Save();
            Debug.Log(b > 0 ? $"[Сервер] Принудительное обновление: только сборка {b} и новее" : "[Сервер] Принудительное обновление выключено");
        }
        return WebReply.Go($"{Api.WebAdmin}?key={WebUtility.UrlEncode(Arg(args, "key"))}");
    }
}
