public class BomResult
{
    public List<BomLineItemData> Lines { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public double Subtotal { get; set; }
    public double GrandTotal { get; set; }
}

public class BomLineItemData
{
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public double Quantity { get; set; }
    public string? Unit { get; set; }
    public double UnitCost { get; set; }
    public double LineTotal { get; set; }
}