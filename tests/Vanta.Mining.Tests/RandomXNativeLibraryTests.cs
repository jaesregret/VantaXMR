using System.Runtime.InteropServices;

namespace Vanta.Mining.Tests;

public sealed class RandomXNativeLibraryTests
{
    private static readonly string[] RequiredExports =
    [
        "randomx_alloc_cache",
        "randomx_init_cache",
        "randomx_create_vm",
        "randomx_calculate_hash",
        "randomx_destroy_vm",
        "randomx_release_cache",
        "randomx_get_flags"
    ];

    [Fact]
    public void NativeRandomXDll_IsX64AndExportsOfficialCApi()
    {
        Assert.True(Environment.Is64BitProcess);

        var path = Path.Combine(AppContext.BaseDirectory, "randomx.dll");
        Assert.True(File.Exists(path), $"Expected native RandomX DLL at {path}");

        var handle = NativeLibrary.Load(path);
        try
        {
            foreach (var export in RequiredExports)
            {
                Assert.True(NativeLibrary.TryGetExport(handle, export, out _), $"Missing export: {export}");
            }
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }
}