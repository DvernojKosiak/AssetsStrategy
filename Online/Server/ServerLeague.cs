using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Лиги на сервере: рейтинг игроков (набеги на игроков и клановые войны) и кланов (клановые войны),
/// мировая таблица лучших, история изменений и награда, когда впервые дошли до новой лиги.
/// </summary>
public partial class ServerCore
{
    const int RatingLogKeep = 20;

    static void LogRating(List<RatingChange> log, int delta, int rating, string text)
    {
        log.Add(new RatingChange { delta = delta, rating = rating, text = text, timeUtc = Now() });
        if (log.Count > RatingLogKeep) log.RemoveRange(0, log.Count - RatingLogKeep);
    }

    static string Signed(int d) { return d > 0 ? "+" + d : d.ToString(); }

    /// <summary>Изменить рейтинг игрока. Первый раз в новой лиге - награда на базу.</summary>
    void ChangePlayerRating(StoredAccount a, int delta, string why)
    {
        if (a == null || a.profile == null) return;
        int was = a.profile.rating;
        a.profile.rating = Math.Max(0, was + delta);
        int d = a.profile.rating - was;
        LogRating(a.ratingLog, d, a.profile.rating, why);

        int league = Leagues.Of(a.profile.rating);
        if (league > a.bestLeague)
        {
            for (int l = a.bestLeague + 1; l <= league; l++)
                a.grants.Add(LeagueGrant(l, 1f, $"Новая лига: {Leagues.Name(l)}! Награда на базу"));
            a.bestLeague = league;
        }
    }

    /// <summary>Изменить рейтинг клана. Первый раз в новой лиге - каждому участнику награда (половина игроцкой).</summary>
    void ChangeClanRating(StoredClan c, int delta, string why)
    {
        if (c == null) return;
        int was = c.rating;
        int wasLeague = Leagues.Of(was);
        c.rating = Math.Max(0, was + delta);
        LogRating(c.ratingLog, c.rating - was, c.rating, why);

        int league = Leagues.Of(c.rating);
        if (league != wasLeague)
            SystemMessage(c, league > wasLeague ? $"Клан поднялся в лигу «{Leagues.Name(league)}»!" : $"Клан опустился в лигу «{Leagues.Name(league)}»");
        if (league > c.bestLeague)
        {
            foreach (var id in c.members)
            {
                StoredAccount m = FindById(id);
                if (m == null) continue;
                for (int l = c.bestLeague + 1; l <= league; l++)
                    m.grants.Add(LeagueGrant(l, 0.5f, $"Клан «{c.name}» дошёл до лиги «{Leagues.Name(l)}»! Награда на базу"));
            }
            c.bestLeague = league;
        }
    }

    static WarGrant LeagueGrant(int league, float k, string text)
    {
        int res = (int)(Leagues.RewardRes(league) * k);
        return new WarGrant
        {
            id = Guid.NewGuid().ToString("N"),
            text = text,
            gold = (int)(Leagues.RewardGold(league) * k),
            wood = res, stone = res, food = res,
            iron = (int)(Leagues.RewardIron(league) * k),
        };
    }

    /// <summary>Рейтинг за набег на игрока. outcome: 1 - победа, 0 - ничья, -1 - поражение. Возвращает изменение у защитника.</summary>
    // outcome: 2 - победа, 1 - небольшая победа, 0 - ничья, -1 - не удался. damagePct - сколько % города разрушено
    int RaidRating(StoredAccount attacker, StoredAccount defender, int outcome, int damagePct)
    {
        if (attacker == null || defender == null || outcome == 0) return 0;
        int ra = attacker.profile.rating, rd = defender.profile.rating;
        int dA, dD;
        string what;
        if (outcome > 0)
        {
            int b = Leagues.Bonus(ra, rd, 6);
            dA = Math.Max(2, Leagues.RaidWin + b);
            dD = -Math.Max(1, Leagues.RaidLossDefender + b / 2);
            if (outcome == 1) { dA = Math.Max(1, dA / 2); dD = Math.Min(-1, dD / 2); }
            what = outcome == 2 ? "победа" : "небольшая победа";
        }
        else
        {
            int b = Leagues.Bonus(rd, ra, 4);   // отбился от сильного - получил больше
            dA = -Math.Max(1, Leagues.RaidFail + b / 2);
            dD = Math.Max(1, Leagues.RaidHeld + b);
            what = "не удался";
        }
        int beforeD = defender.profile.rating;
        ChangePlayerRating(attacker, dA, $"Набег на {defender.profile.displayName}: {what} (разрушено {damagePct}%)");
        ChangePlayerRating(defender, dD, outcome > 0 ? $"{attacker.profile.displayName} разорил(а) вашу деревню (разрушено {damagePct}%)" : $"Отбили набег {attacker.profile.displayName} (разрушено {damagePct}%)");
        return defender.profile.rating - beforeD;
    }

    // ---------- Таблица ----------

    ApiResponse LeagueTopReq(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        StoredClan myClan = ClanOf(me);
        int filter = r.league;
        var board = new LeagueBoard();

        // Кланы
        var clans = new List<StoredClan>(db.clans);
        clans.Sort((a, b) => a.rating != b.rating ? b.rating.CompareTo(a.rating) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        board.totalClans = clans.Count;
        for (int i = 0; i < clans.Count; i++)
        {
            StoredClan c = clans[i];
            bool mine = myClan != null && c.id == myClan.id;
            if (mine) board.clanPlace = i + 1;
            if (filter >= 0 && Leagues.Of(c.rating) != filter) continue;
            if (board.clans.Count >= Api.LeagueTopSize) continue;
            board.clans.Add(new LeagueRow { place = i + 1, id = c.id, name = c.name, rating = c.rating, members = c.members.Count, me = mine });
        }

        // Игроки
        var clanNames = new Dictionary<string, string>();
        foreach (var c in db.clans) clanNames[c.id] = c.name;
        var players = db.accounts.FindAll(a => a != null && a.profile != null && !a.npc);   // NPC-деревень в таблице нет
        players.Sort((a, b) => a.profile.rating != b.profile.rating ? b.profile.rating.CompareTo(a.profile.rating)
                                                                     : string.Compare(a.profile.displayName, b.profile.displayName, StringComparison.OrdinalIgnoreCase));
        board.totalPlayers = players.Count;
        for (int i = 0; i < players.Count; i++)
        {
            StoredAccount a = players[i];
            bool mine = a == me;
            if (mine) board.myPlace = i + 1;
            if (filter >= 0 && Leagues.Of(a.profile.rating) != filter) continue;
            if (board.players.Count >= Api.LeagueTopSize) continue;
            string clan = !string.IsNullOrEmpty(a.clanId) && clanNames.TryGetValue(a.clanId, out string cn) ? cn : "";
            board.players.Add(new LeagueRow { place = i + 1, id = a.profile.id, name = a.profile.displayName, clan = clan, rating = a.profile.rating,
                                              hall = CityLoss.HallLevel(ReadCity(a.profile.id)), me = mine });
        }

        board.myRating = me.profile.rating;
        board.myBest = Math.Max(me.bestLeague, Leagues.Of(me.profile.rating));
        board.history = new List<RatingChange>(me.ratingLog);
        if (myClan != null)
        {
            board.clanName = myClan.name;
            board.clanRating = myClan.rating;
            board.clanBest = Math.Max(myClan.bestLeague, Leagues.Of(myClan.rating));
            board.clanHistory = new List<RatingChange>(myClan.ratingLog);
        }
        return new ApiResponse { ok = true, league = board, profile = me.profile.Copy() };
    }
}
