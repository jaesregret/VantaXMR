# Vanta

Vanta is a legitimate CPU miner for Monero (XMR) that runs only when started explicitly by the user. The project is intentionally conservative: it does not persist, auto-start, hide its process, or add stealth or credential-stealing behavior. It requires a public Monero wallet address only and does not accept seed phrases or private keys.

## Requirements

- .NET SDK 10.0 or another compatible .NET SDK installed locally
- A supported native RandomX library if you intend to run real mining
- A public Monero wallet address
- A compatible Monero mining pool with a reachable host and port. The reference endpoint used by this project is SupportXMR: `pool.supportxmr.com:443` with TLS.

## Current status

The reference protocol is the standard JSON-RPC Monero Stratum dialect used by XMRig-compatible pools: login with `login`, `pass`, and `agent`; jobs with `blob`, `job_id`, `target`, `difficulty`, and optional `seed_hash`; shares with `id`, `job_id`, `nonce`, and `result`. SupportXMR requires a public Monero address as `login`; password is conventionally `x`. The client validates the TLS certificate using the platform trust store.

Protocol references:

- SupportXMR: [supportxmr.com](https://supportxmr.com/)
- XMRig Stratum extensions and message shapes: [STRATUM_EXT.md](https://github.com/xmrig/xmrig-proxy/blob/master/doc/STRATUM_EXT.md)
- Official RandomX API used by the native binding: [randomx.h](https://github.com/tevadores/RandomX/blob/master/src/randomx.h)

## Build

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

## Configuration

Create a `config/config.json` file or use the example file as a starting point:

```json
{
  "Mining": {
    "Coin": "XMR",
    "Algorithm": "RandomX",
    "WalletAddress": "",
    "Pool": {
      "Host": "pool.supportxmr.com",
      "Port": 443,
      "Tls": true
    },
    "Threads": 0,
    "DonateLevel": 0
  }
}
```

Important rules:

- `WalletAddress` must be a public Monero address only.
- Seed phrases, private keys, mnemonics, and credentials are rejected.
- `Threads` equal to `0` means "auto-detect a sensible default".
- CLI arguments override JSON values when both are supplied.

## Running the CLI

```bash
dotnet run --project src/Vanta.Cli -- start --config config/config.json
```

Commands:

- `vanta start`
- `vanta config`
- `vanta benchmark`
- `vanta status`
- `vanta version`

## Benchmark

```bash
dotnet run --project src/Vanta.Cli -- benchmark --threads 4 --seconds 10
```

The benchmark is meant to measure local CPU performance before live mining. Real RandomX hashing requires the official RandomX C library (`librandomx`) to be available. Vanta binds its cache, VM, and hash functions through P/Invoke and does not provide a substitute implementation.

## Starting mining

```bash
dotnet run --project src/Vanta.Cli -- start --wallet <public_wallet_address> --pool example.pool:3333 --threads 4 --tls true
```

Stop mining immediately with `Ctrl+C`.

## Security and legal notes

- Only a public wallet address is accepted.
- No seed, private key, API key, or credential is stored in the source code.
- The miner is intentionally explicit and visible in the CLI.
- The project does not auto-start, hide its process, or persist itself.

## Current limitations

- Real RandomX mining requires an actual native `librandomx` binary and is not bundled in this repository.
- SupportXMR is the reference public pool endpoint; pool availability, geographic routing, account policies, and port availability can change.
- The app does not support hidden or stealth operation.

## License
VANTAXMR SOFTWARE LICENSE
Copyright © 2026 JaesRegret & Vze7. All rights reserved.
...
Vanta is **proprietary software**. See [LICENSE](./LICENSE).
