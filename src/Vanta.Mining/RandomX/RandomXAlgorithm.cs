using System.Runtime.InteropServices;

namespace Vanta.Mining;

public sealed class RandomXAlgorithm : IMiningAlgorithm
{
    private readonly RandomXCache? _sharedCache;
    private RandomXCache? _ownedCache;
    private IntPtr _vm;
    private byte[]? _seedHash;
    private bool _initialized;

    public RandomXAlgorithm(RandomXCache? sharedCache = null)
    {
        _sharedCache = sharedCache;
    }

    public string Name => "RandomX";

    public void Initialize(ReadOnlySpan<byte> seedHash)
    {
        if (_initialized && _seedHash!.AsSpan().SequenceEqual(seedHash))
        {
            return;
        }

        if (seedHash.Length is not (0 or 32))
        {
            throw new ArgumentException("RandomX seed hash must be exactly 32 bytes.", nameof(seedHash));
        }

        var key = seedHash.Length == 0 ? new byte[32] : seedHash.ToArray();
        DestroyVm();

        var cache = _sharedCache ?? (_ownedCache ??= new RandomXCache());
        cache.Initialize(key);
        _seedHash = key;
        _vm = NativeMethods.randomx_create_vm(
            NativeMethods.randomx_get_flags() | NativeMethods.RandomXFlags.Jit,
            cache.Handle,
            IntPtr.Zero);

        if (_vm == IntPtr.Zero)
        {
            throw new InvalidOperationException("RandomX VM creation failed.");
        }

        _initialized = true;
    }

    public void Hash(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (!_initialized)
        {
            Initialize(ReadOnlySpan<byte>.Empty);
        }

        if (output.Length < 32)
        {
            throw new ArgumentException("RandomX output must provide at least 32 bytes.", nameof(output));
        }

        var inputBytes = input.ToArray();
        var outputBytes = new byte[32];
        NativeMethods.randomx_calculate_hash(_vm, inputBytes, (nuint)inputBytes.Length, outputBytes);
        outputBytes.CopyTo(output);
    }

    public void Dispose()
    {
        if (_vm != IntPtr.Zero)
        {
            DestroyVm();
        }

        _ownedCache?.Dispose();
        _ownedCache = null;
        _initialized = false;
        _seedHash = null;
    }

    private void DestroyVm()
    {
        if (_vm == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.randomx_destroy_vm(_vm);
        _vm = IntPtr.Zero;
        _initialized = false;
    }

    internal static class NativeMethods
    {
        [Flags]
        public enum RandomXFlags : uint
        {
            Default = 0,
            LargePages = 1,
            HardAes = 2,
            FullMem = 4,
            Jit = 8
        }

        [DllImport("randomx", EntryPoint = "randomx_get_flags", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern RandomXFlags randomx_get_flags();

        [DllImport("randomx", EntryPoint = "randomx_alloc_cache", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr randomx_alloc_cache(RandomXFlags flags);

        [DllImport("randomx", EntryPoint = "randomx_init_cache", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern void randomx_init_cache(IntPtr cache, byte[] key, nuint keySize);

        [DllImport("randomx", EntryPoint = "randomx_create_vm", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr randomx_create_vm(RandomXFlags flags, IntPtr cache, IntPtr dataset);

        [DllImport("randomx", EntryPoint = "randomx_destroy_vm", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern void randomx_destroy_vm(IntPtr vm);

        [DllImport("randomx", EntryPoint = "randomx_release_cache", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern void randomx_release_cache(IntPtr cache);

        [DllImport("randomx", EntryPoint = "randomx_calculate_hash", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        public static extern void randomx_calculate_hash(
            IntPtr vm,
            byte[] input,
            nuint inputSize,
            [Out] byte[] output);
    }
}
