namespace GearEnhance;

// Fixed material/cash prices: unaffected by item value, stage or F12 tuning.
public static class SuccessBoost
{
    public const string BitcoinTemplate = "59faff1d86f7746c51718c9c";
    public static int Bitcoins(bool evolution, int stars) => evolution ? stars >= 3 ? 10 : 3 : 1;
    public static int Roubles(bool evolution, int stars) => evolution ? stars >= 3 ? 3000000 : 1000000 : 500000;
    public static int Points(bool evolution) => evolution ? 10 : 7;
    // 1.3.0 transcendence attempts, either track.
    public const int TranscendBitcoins = 3;
    public const int TranscendRoubles = 1000000;
    public const int TranscendPoints = 5;
}
