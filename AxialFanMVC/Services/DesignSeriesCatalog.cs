namespace AxialFanMVC.Services
{
    public static class DesignSeriesCatalog
    {
        // Standard tip diameters (mm) offered when generating a series.
        // Centralized here so it's one place to update if the catalog changes.
        public static readonly int[] StandardDiametersMm = { 800, 1000, 1250, 1600 };
    }
}