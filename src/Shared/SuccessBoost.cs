namespace GearEnhance;

// Fixed material/cash prices: unaffected by item value, stage or F12 tuning.
public static class SuccessBoost
{
    public const string BitcoinTemplate = "59faff1d86f7746c51718c9c";
    public static int Bitcoins(bool evolution, int stars) => evolution ? stars >= 3 ? 10 : 3 : 1;
    public static int Roubles(bool evolution, int stars) => evolution ? stars >= 3 ? 3000000 : 1000000 : 500000;
    public static int Points(bool evolution) => evolution ? 10 : 7;
}
