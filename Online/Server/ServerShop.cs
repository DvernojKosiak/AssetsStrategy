using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Магазин марок и места под лоты на сервере.
///
/// ПОКУПКА МАРОК (Api.ShopClaim). Игра купила набор в Google Play и присылает чек Unity IAP.
///  - Сервер проверяет подпись покупки ключом приложения из Play Console (файл google_play_key.txt
///    в папке данных сервера или переменная окружения STRATEGYCA_PLAY_KEY). Нет ключа - проверка выключена
///    (для проверок в Unity: там «ненастоящий» магазин), об этом пишется в окно сервера.
///  - Каждая покупка засчитывается один раз (по purchaseToken / номеру заказа): повторный чек - ok, но без марок.
///  - Марки приходят наградой (WarGrant.marks): сразу в ответе и при загрузке города, если игра не успела их учесть.
///
/// МЕСТА ПОД ЛОТЫ (Api.MarketSlotBuy). Сколько лотов игрок может держать на рынке - AccountProfile.lotSlots.
/// Игра сама списывает марки и золото (LotRules.SlotCost) и просит сервер открыть место.
/// </summary>
public partial class ServerCore
{
    [Serializable]
    class StoredPurchase
    {
        public string key;          // purchaseToken (или номер заказа)
        public string accountId;
        public string productId;
        public string orderId;
        public int marks;
        public string timeUtc;
        public bool verified;       // подпись проверена ключом Google Play
    }

    const string PlayKeyFile = "google_play_key.txt";
    bool keyWarned;

    // Ключ лицензирования приложения (base64, из Play Console: Монетизация → Настройка монетизации → Лицензирование)
    string PlayKey()
    {
        string k = Environment.GetEnvironmentVariable("STRATEGYCA_PLAY_KEY");
        if (string.IsNullOrWhiteSpace(k))
        {
            string p = Path.Combine(dataDir, PlayKeyFile);
            try { if (File.Exists(p)) k = File.ReadAllText(p); } catch (Exception) { }
        }
        if (string.IsNullOrWhiteSpace(k)) return null;
        var sb = new StringBuilder();
        foreach (char ch in k) if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        return sb.ToString();
    }

    void FixShopData()
    {
        if (db.purchases == null) db.purchases = new List<StoredPurchase>();
        foreach (var a in db.accounts)
        {
            if (a == null || a.profile == null || a.profile.lotSlots > 0) continue;
            // Старые аккаунты: сколько лотов уже выставлено - столько мест и оставляем
            int mine = db.lots.FindAll(x => x.sellerId == a.profile.id).Count;
            a.profile.lotSlots = Math.Min(Api.MaxLotsPerPlayer, Math.Max(LotRules.StartSlots, mine));
        }
        Debug.Log(PlayKey() != null ? "[Сервер] Магазин марок: покупки проверяются ключом Google Play"
                                    : $"[Сервер] Магазин марок: ключа Google Play нет ({PlayKeyFile}), покупки не проверяются (подходит только для проверок)");
    }

    int LotSlots(StoredAccount a)
    {
        if (a.profile.lotSlots <= 0) a.profile.lotSlots = LotRules.StartSlots;
        return Math.Min(Api.MaxLotsPerPlayer, a.profile.lotSlots);
    }

    // ---------- Места под лоты ----------

    ApiResponse MarketSlotBuy(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        int have = LotSlots(me);
        if (have >= Api.MaxLotsPerPlayer) return ApiResponse.Fail($"Открыты все {Api.MaxLotsPerPlayer} мест");
        // Игра присылает, сколько мест у неё было: если места уже открыли (двойное нажатие), второй раз не открываем
        if (r.slot > 0 && r.slot != have) return ApiResponse.Fail("Места уже обновились — откройте рынок ещё раз");
        me.profile.lotSlots = have + 1;
        Save();
        return new ApiResponse { ok = true, profile = me.profile.Copy() };
    }

    // ---------- Покупка марок ----------

    ApiResponse ShopClaim(ApiRequest r)
    {
        StoredAccount me = FindByToken(r.token);
        if (me == null) return ApiResponse.Fail(SessionGone);
        if (string.IsNullOrEmpty(r.receipt) || r.receipt.Length > 64 * 1024) return ApiResponse.Fail("Нет чека покупки");

        IapReceipt rec = null;
        try { rec = JsonUtility.FromJson<IapReceipt>(r.receipt); } catch (Exception) { }
        if (rec == null) return ApiResponse.Fail("Чек покупки не читается");

        string key = PlayKey();
        string productId = r.productId, orderId = rec.TransactionID, token = rec.TransactionID;
        bool verified = false;

        if (rec.Store == "GooglePlay")
        {
            GooglePayload pl = null;
            GooglePurchase gp = null;
            try { pl = JsonUtility.FromJson<GooglePayload>(rec.Payload); } catch (Exception) { }
            if (pl == null || string.IsNullOrEmpty(pl.json)) return ApiResponse.Fail("Чек Google Play не читается");
            try { gp = JsonUtility.FromJson<GooglePurchase>(pl.json); } catch (Exception) { }
            if (gp == null || string.IsNullOrEmpty(gp.productId)) return ApiResponse.Fail("Чек Google Play не читается");
            if (key != null)
            {
                if (!VerifyGoogle(key, pl.json, pl.signature)) return Reject(me, "подпись не сошлась");
                verified = true;
            }
            if (gp.purchaseState != 0) return ApiResponse.Fail("Покупка ещё не оплачена — марки придут, когда Google Play подтвердит оплату");
            productId = gp.productId;
            orderId = gp.orderId;
            token = !string.IsNullOrEmpty(gp.purchaseToken) ? gp.purchaseToken : gp.orderId;
        }
        else if (key != null)
            return Reject(me, $"магазин «{rec.Store}» (принимаются только покупки Google Play)");

        if (key == null && !keyWarned)
        {
            keyWarned = true;
            Debug.LogWarning($"[Сервер] Покупки марок НЕ проверяются: нет ключа Google Play ({PlayKeyFile} в папке данных). Для проверок в Unity это нормально, для игры в Google Play ключ нужен.");
        }

        StampPack pack = StampPacks.Find(productId);
        if (pack == null) return ApiResponse.Fail("Такого набора марок нет: " + productId);
        if (string.IsNullOrEmpty(token)) return ApiResponse.Fail("В чеке нет номера покупки");

        StoredPurchase done = db.purchases.Find(x => x.key == token);
        if (done != null)
        {
            // Уже засчитана: игре надо просто закрыть покупку в магазине
            return new ApiResponse { ok = true, grants = new List<WarGrant>(me.grants), shopMarks = 0, error = done.accountId == me.profile.id ? null : "Эта покупка уже засчитана другому аккаунту" };
        }

        db.purchases.Add(new StoredPurchase { key = token, accountId = me.profile.id, productId = pack.id, orderId = orderId, marks = pack.marks, timeUtc = Now(), verified = verified });
        me.grants.Add(new WarGrant { id = Guid.NewGuid().ToString("N"), text = $"Покупка «{pack.name}»", marks = pack.marks });
        Save();
        Debug.Log($"[Сервер] {me.profile.displayName} купил {pack.id} ({pack.marks} марок){(verified ? "" : ", без проверки подписи")}");
        return new ApiResponse { ok = true, grants = new List<WarGrant>(me.grants), shopMarks = pack.marks };
    }

    static ApiResponse Reject(StoredAccount me, string why)
    {
        Debug.LogWarning($"[Сервер] Покупка марок от {me.profile.displayName} отклонена: {why}");
        return ApiResponse.Fail("Покупка не прошла проверку. Если деньги списались, напишите в поддержку — Google Play вернёт их сам, если покупка не будет подтверждена за 3 дня.");
    }

    // ---------- Подпись Google Play: RSA SHA1 по ключу приложения ----------

    static bool VerifyGoogle(string base64Key, string json, string signature)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(signature)) return false;
        try
        {
            RSAParameters p = ParsePublicKey(Convert.FromBase64String(base64Key));
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(p);
                return rsa.VerifyData(Encoding.UTF8.GetBytes(json), Convert.FromBase64String(signature), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Сервер] Ключ Google Play не читается: " + e.Message);
            return false;
        }
    }

    // SubjectPublicKeyInfo (DER): SEQUENCE { SEQUENCE { OID, NULL }, BIT STRING { SEQUENCE { INTEGER n, INTEGER e } } }
    static RSAParameters ParsePublicKey(byte[] der)
    {
        int i = 0;
        Expect(der, ref i, 0x30); ReadLen(der, ref i);
        Expect(der, ref i, 0x30); int algLen = ReadLen(der, ref i); i += algLen;
        Expect(der, ref i, 0x03); ReadLen(der, ref i);
        if (der[i++] != 0) throw new FormatException("bit string");
        Expect(der, ref i, 0x30); ReadLen(der, ref i);
        byte[] n = ReadInt(der, ref i), e = ReadInt(der, ref i);
        return new RSAParameters { Modulus = n, Exponent = e };
    }

    static void Expect(byte[] d, ref int i, byte tag) { if (d[i++] != tag) throw new FormatException("ключ не RSA"); }

    static int ReadLen(byte[] d, ref int i)
    {
        int b = d[i++];
        if (b < 0x80) return b;
        int n = b & 0x7f, len = 0;
        for (int k = 0; k < n; k++) len = (len << 8) | d[i++];
        return len;
    }

    static byte[] ReadInt(byte[] d, ref int i)
    {
        Expect(d, ref i, 0x02);
        int len = ReadLen(d, ref i);
        int start = i;
        i += len;
        while (len > 1 && d[start] == 0) { start++; len--; }   // ведущий ноль знака
        byte[] r = new byte[len];
        Array.Copy(d, start, r, 0, len);
        return r;
    }
}
