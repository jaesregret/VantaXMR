namespace Vanta.Mining;

/// <summary>
/// Owns one initialized native RandomX cache. Multiple <see cref="RandomXAlgorithm"/>
/// instances can create separate VMs from this cache.
/// </summary>
public sealed class RandomXCache : IDisposable
{
    private readonly object _sync = new();
    private IntPtr _cache;
    private byte[]? _seedHash;
    private bool _disposed;

    internal IntPtr Handle
    {
        get
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_cache == IntPtr.Zero)
                {
                    throw new InvalidOperationException("The RandomX cache has not been initialized.");
                }

                return _cache;
            }
        }
    }

    public void Initialize(ReadOnlySpan<byte> seedHash)
    {
        if (seedHash.Length is not (0 or 32))
        {
            throw new ArgumentException("RandomX seed hash must be exactly 32 bytes.", nameof(seedHash));
        }

        var key = seedHash.Length == 0 ? new byte[32] : seedHash.ToArray();

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_seedHash is not null && _seedHash.AsSpan().SequenceEqual(key))
            {
                return;
            }

            if (_cache == IntPtr.Zero)
            {
                try
                {
                    _cache = RandomXAlgorithm.NativeMethods.randomx_alloc_cache(
                        RandomXAlgorithm.NativeMethods.randomx_get_flags());
                }
                catch (DllNotFoundException ex)
                {
                    throw new PlatformNotSupportedException(
                        "The official RandomX native library was not found. Install a compatible librandomx binary before mining.", ex);
                }
                catch (BadImageFormatException ex)
                {
                    throw new PlatformNotSupportedException(
                        "The RandomX native library does not match the process architecture.", ex);
                }

                if (_cache == IntPtr.Zero)
                {
                    throw new InvalidOperationException("RandomX cache allocation failed.");
                }
            }

            RandomXAlgorithm.NativeMethods.randomx_init_cache(_cache, key, (nuint)key.Length);
            _seedHash = key;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_cache != IntPtr.Zero)
            {
                RandomXAlgorithm.NativeMethods.randomx_release_cache(_cache);
                _cache = IntPtr.Zero;
            }

            _seedHash = null;
        }
    }
}
