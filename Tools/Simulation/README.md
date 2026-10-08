# Standalone simulation benchmark

This .NET 8 console project compiles the actual C# rules and policy observation sources used by Unity. It has no Unity assembly or NuGet dependencies. A .NET SDK with the .NET 8 runtime is required.

From the repository root:

```sh
dotnet run --project Tools/Simulation/Simulation.Benchmark.csproj --configuration Release -- --games 100 --size 7 --seed 42
```

The default includes fair seat observations, legal masks and both stages of the existing policy encoding. Add `--rules-only` to measure legal-action generation and transitions without policy encoding. Supported policy board sizes are 5, 6, 7, 9 and 11. The rules assembly itself accepts arbitrary board dimensions and seat counts.

The output records rules version, seed, board size, actions, captures, limits, elapsed time, rate and cumulative allocation. An unavailable peak resident-memory measurement is `null`. Allocation is total allocated memory during the run, not retained RAM.

The driver chooses random legal actions and interrupts at 100 rounds or 20,000 actions. It exercises complete games but does not train weights or measure opponent strength. PPO, neural inference, Python communication and spectator rendering are excluded. Compare actual training throughput separately; this number is not a prediction of training speed or phone move latency.

Generated `bin/` and `obj/` files are ignored. Gameplay and training changes should be validated with the shared-rules and scene-parity Unity tests before relying on benchmark results. Implementation and current measurements are in [Shared C# Simulation](../../Docs/Shared_Simulation_Implementation.md).
