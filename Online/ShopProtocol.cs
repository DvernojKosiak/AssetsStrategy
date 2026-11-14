using System;
using UnityEngine;

// ---------- Магазин марок и места под лоты: общие правила игры и сервера ----------

/// <summary>Набор марок за реальные деньги (Google Play: «Управляемый товар», потребляемый).</summary>
[Serializable]
public class StampPack
{
    public string id;        // id товара в Play Console (Монетизация → Товары → Встроенные товары)
    public string name;
    public int marks;        // сколько марок
    public int bonus;        // из них подарок (показываем «+N в подарок»)
    public string price;     // рекомендуемая цена (если магазин не ответил, показываем её)
    public bool best;        // «выгоднее всего»
}

public static class StampPacks
{
    /// <summary>Наборы марок. id должны совпадать с id товаров в Play Console.</summary>
    public static readonly StampPack[] All =
    {
        new StampPack { id = "marks_80",   name = "Горсть марок",     marks = 80,   bonus = 0,    price = "0,99 €" },
        new StampPack { id = "marks_220",  name = "Кошель марок",     marks = 220,  bonus = 20,   price = "1,99 €" },
        new StampPack { id = "marks_575",  name = "Мешок марок",      marks = 575,  bonus = 75,   price = "4,49 €" },
        new StampPack { id = "marks_1500", name = "Сундук марок",     marks = 1500, bonus = 300,  price = "9,99 €", best = true },
        new StampPack { id = "marks_3400", name = "Казна гильдии",    marks = 3400, bonus = 900,  price = "19,99 €" },
        new StampPack { id = "marks_9500", name = "Сокровищница",     marks = 9500, bonus = 3000, price = "49,99 €" },
    };

    /// <summary>Понятный текст, если сервер старый и не знает новых запросов.</summary>
    public static string ServerError(string error)
    {
        if (error != null && error.StartsWith("Неизвестный запрос"))
            return "Сервер старой версии. Замените StrategycaServer.exe на новый из пакета marki-1 и перезапустите его";
        return error;
    }

    public static StampPack Find(string id)
    {
        foreach (var p in All) if (p.id == id) return p;
        return null;
    }
}

/// <summary>
/// Рынок игроков: места под лоты и доставка.
///  - Открыто одно место. Следующие покупаются за марки и золото, всего до Api.MaxLotsPerPlayer.
///    Сколько мест можно держать, зависит от уровня Торгового дома (LotSlotsByHouse).
///  - Доставка: покупатель платит цену лота и ещё сбор за доставку (DeliveryFee). Продавец получает ровно цену лота,
///    сбор сгорает - так марки понемногу уходят из игры.
/// </summary>
public static class LotRules
{
    public const int StartSlots = 1;

    // Цена места номер n (2..10): марки и золото
    static readonly int[] SlotMarks = { 0, 0, 10, 20, 35, 50, 75, 100, 140, 190, 250 };
    static readonly int[] SlotGold  = { 0, 0, 100, 200, 350, 500, 750, 1000, 1400, 1900, 2500 };

    // Сколько мест можно держать при Торговом доме Ур.1 / 2 / 3
    static readonly int[] LotSlotsByHouse = { 4, 7, 10 };

    /// <summary>Цена следующего места, когда их уже owned. false - больше мест не бывает.</summary>
    public static bool SlotCost(int owned, out int marks, out int gold)
    {
        int n = owned + 1;
        marks = gold = 0;
        if (n > Api.MaxLotsPerPlayer || n >= SlotMarks.Length) return false;
        marks = SlotMarks[n];
        gold = SlotGold[n];
        return true;
    }

    public static int SlotsForHouse(int houseLevel)
    {
        return LotSlotsByHouse[Mathf.Clamp(houseLevel, 1, LotSlotsByHouse.Length) - 1];
    }

    public const float DeliveryRate = 0.03f;   // 3% от цены, округление вверх, не меньше 1

    /// <summary>Сбор за доставку (сгорает). 10 → 1, 70 → 3, 200 → 6, 1000 → 30.</summary>
    public static int DeliveryFee(int price)
    {
        if (price <= 0) return 0;
        return Math.Max(1, (int)Math.Ceiling(price * DeliveryRate - 0.0001));
    }

    public static int BuyerPays(int price) { return price + DeliveryFee(price); }
}

// ---------- Покупка в Google Play: что игра присылает серверу ----------

/// <summary>Чек Unity IAP (product.receipt): {"Store":"GooglePlay","TransactionID":"...","Payload":"..."}.</summary>
[Serializable]
public class IapReceipt
{
    public string Store;
    public string TransactionID;
    public string Payload;
}

/// <summary>Payload чека Google Play: подписанный JSON покупки и подпись.</summary>
[Serializable]
public class GooglePayload
{
    public string json;
    public string signature;
}

/// <summary>Сама покупка (поле json чека Google Play).</summary>
[Serializable]
public class GooglePurchase
{
    public string orderId;
    public string packageName;
    public string productId;
    public long purchaseTime;
    public int purchaseState;     // 0 - оплачено, 1 - отменено, 2 - ожидает оплаты
    public string purchaseToken;
}
