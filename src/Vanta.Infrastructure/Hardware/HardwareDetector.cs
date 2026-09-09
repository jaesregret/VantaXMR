namespace Vanta.Infrastructure;

public static class HardwareDetector
{
    public static int DetectLogicalCpuCount() => Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 1;

    public static int DetectPreferredThreadCount()
    {
        var logicalCount = DetectLogicalCpuCount();
        return Math.Max(1, logicalCount);
    }

    public static long DetectAvailableMemoryBytes()
    {
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }
}
