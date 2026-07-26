namespace Seto90;

/// <summary>
/// Sesion de tienda: comprar y vender con dinero real. Logica pura y testeable sin ventana
/// (shop-smoke); el dibujo y los sonidos viven en el runtime, igual que con TurnBattleSession.
///
/// Nota de diseno: la tienda de los JRPG 90s era dos listas y un numero — la gracia estaba en
/// las reglas: el precio de venta es la mitad (redondeado, minimo 1), ves cuantos tenes de cada
/// item antes de comprar, y el "no te alcanza" es un mensaje, nunca un crash de inventario.
/// </summary>
public sealed class ShopSession
{
    public ShopDef Shop { get; }
    readonly GameProject project;
    readonly List<string> inventory;

    public int Money { get; private set; }
    public bool Selling { get; private set; }
    public int SelectedIndex { get; set; }
    public string Log { get; private set; }

    public sealed record Row(string ItemId, string Name, int Price, int Owned);

    public IReadOnlyList<Row> Rows { get; private set; } = [];

    public ShopSession(ShopDef shop, GameProject project, int money, List<string> inventory)
    {
        Shop = shop;
        this.project = project;
        this.inventory = inventory;
        Money = Math.Max(0, money);
        Log = $"Bienvenido a {shop.Name}.";
        Rebuild();
    }

    public static int SellValue(int price) => Math.Max(1, price / 2);

    /// <summary>El runtime pisa el log tras una compra (ej. avisar que el arma se auto-equipo).</summary>
    public void SetLog(string text) => Log = text;

    /// <summary>Item de la fila seleccionada (para que el runtime sepa que se compro).</summary>
    public string? SelectedItemId => Rows.Count > 0 ? Rows[Math.Clamp(SelectedIndex, 0, Rows.Count - 1)].ItemId : null;

    public void SelectNext() => SelectedIndex = Math.Min(Math.Max(0, Rows.Count - 1), SelectedIndex + 1);
    public void SelectPrevious() => SelectedIndex = Math.Max(0, SelectedIndex - 1);

    public void ToggleMode()
    {
        Selling = !Selling;
        SelectedIndex = 0;
        Log = Selling ? "Que me vendes? Pago la mitad del precio." : $"Bienvenido a {Shop.Name}.";
        Rebuild();
    }

    /// <summary>Compra o vende la fila seleccionada. Devuelve false si no se pudo (sin dinero/sin items).</summary>
    public bool Confirm()
    {
        if (Rows.Count == 0) { Log = Selling ? "No tenes nada para vender." : "No hay nada en venta."; return false; }
        var row = Rows[Math.Clamp(SelectedIndex, 0, Rows.Count - 1)];
        if (Selling)
        {
            inventory.Remove(row.ItemId);
            Money += SellValue(row.Price);
            Log = $"Vendiste {row.Name} por {SellValue(row.Price)}.";
        }
        else
        {
            if (Money < row.Price) { Log = UiStrings.CantAfford(row.Name); return false; }
            Money -= row.Price;
            inventory.Add(row.ItemId);
            Log = $"Compraste {row.Name}.";
        }
        Rebuild();
        return true;
    }

    void Rebuild()
    {
        // Precio 0 = item CLAVE (el objeto de trama): no aparece en el mostrador de venta. Antes
        // se podia vender el item central del juego por $1 (max(1, 0/2)) — agujero real.
        Rows = Selling
            ? [.. inventory.GroupBy(id => id).Select(g =>
                {
                    var item = project.Items.FirstOrDefault(i => i.Id == g.Key);
                    return new Row(g.Key, item?.Name ?? g.Key, item?.Price ?? 0, g.Count());
                }).Where(r => r.Price > 0).OrderBy(r => r.Name)]
            : [.. Shop.ItemIds.Select(id =>
                {
                    var item = project.Items.FirstOrDefault(i => i.Id == id);
                    return new Row(id, item?.Name ?? id, item?.Price ?? 0, inventory.Count(x => x == id));
                })];
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, Rows.Count - 1));
    }
}
