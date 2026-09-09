namespace Vanta.Core;

public static class HashrateCalculator
{
    public static double Calculate(long hashes, TimeSpan elapsed)
    {
        if (hashes <= 0 || elapsed <= TimeSpan.Zero)
        {
            return 0d;
        }

        return hashes / elapsed.TotalSeconds;
    }
}
