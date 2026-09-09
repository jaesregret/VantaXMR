using System;
using System.Runtime.InteropServices;

var path = @"C:\Vanta\RandomX\build\Release\randomx.dll";

var handle = NativeLibrary.Load(path);

Console.WriteLine($"DLL carregada: 0x{handle.ToInt64():X}");
Console.WriteLine();

string[] candidates =
[
    "randomx_alloc_cache",
    "randomx_init_cache",
    "randomx_create_vm",
    "randomx_calculate_hash",
    "randomx_destroy_vm",
    "randomx_release_cache",
    "randomx_get_flags",
    "_randomx_alloc_cache",
    "_randomx_init_cache",
    "_randomx_create_vm",
    "_randomx_calculate_hash"
];

foreach (var name in candidates)
{
    if (NativeLibrary.TryGetExport(handle, name, out var address))
        Console.WriteLine($"FOUND: {name} -> 0x{address.ToInt64():X}");
}

NativeLibrary.Free(handle);