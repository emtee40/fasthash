﻿using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using static Genbox.FastHash.MixFunctions;

namespace Genbox.FastHash.Benchmarks;

[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MixerBenchmarks
{
    private static readonly MixSpec[] _all =
    {
        new MixSpec(nameof(Murmur_32), static (h, seed) => Murmur_32((uint)(h + seed))),
        new MixSpec(nameof(Murmur_32_Seed), static (h, seed) => Murmur_32_Seed((uint)h, (uint)seed)),
        new MixSpec(nameof(Murmur_32_SeedMix), static (h, seed) => Murmur_32_SeedMix((uint)h, (uint)seed)),
        new MixSpec(nameof(Murmur_64), static (h, seed) => Murmur_64(h + seed)),
        new MixSpec(nameof(Murmur_64_Seed), Murmur_64_Seed),
        new MixSpec(nameof(Murmur_64_SeedMix), Murmur_64_SeedMix),
        new MixSpec(nameof(Mx3_64), static (h, seed) => Mx3_64(h + seed)),
        new MixSpec(nameof(Mx3_64_Seed), Mx3_64_Seed),
        new MixSpec(nameof(Mx3_64_SeedMix), Mx3_64_SeedMix),
        new MixSpec(nameof(Xmx_64), static (h, seed) => Xmx_64(h + seed)),
        new MixSpec(nameof(Xmx_64_Seed), Xmx_64_Seed),
        new MixSpec(nameof(Xmx_64_SeedMix), Xmx_64_SeedMix),
        new MixSpec(nameof(MoreMur_64), static (h, seed) => MoreMur_64(h + seed)),
        new MixSpec(nameof(MoreMur_64_Seed), MoreMur_64_Seed),
        new MixSpec(nameof(MoreMur_64_SeedMix), MoreMur_64_SeedMix),
        new MixSpec(nameof(Murmur14_64), static (h, seed) => Murmur14_64(h + seed)),
        new MixSpec(nameof(Murmur14_64_Seed), Murmur14_64_Seed),
        new MixSpec(nameof(Murmur14_64_SeedMix), Murmur14_64_SeedMix),
        new MixSpec(nameof(XXH2_32), static (h, seed) => XXH2_32((uint)(h + seed))),
        new MixSpec(nameof(XXH2_32_Seed), static (h, seed) => XXH2_32_Seed((uint)h, (uint)seed)),
        new MixSpec(nameof(XXH2_32_SeedMix), static (h, seed) => XXH2_32_SeedMix((uint)h, (uint)seed)),
        new MixSpec(nameof(XXH2_64), static (h, seed) => XXH2_64(h + seed)),
        new MixSpec(nameof(XXH2_64_Seed), XXH2_64_Seed),
        new MixSpec(nameof(XXH2_64_SeedMix), XXH2_64_SeedMix),
        new MixSpec(nameof(FastHash_64), static (h, seed) => FastHash_64(h + seed)),
        new MixSpec(nameof(FastHash_64_Seed), FastHash_64_Seed),
        new MixSpec(nameof(FastHash_64_SeedMix), FastHash_64_SeedMix),
        new MixSpec(nameof(Nasam_64), static (h, seed) => Nasam_64(h + seed)),
        new MixSpec(nameof(Nasam_64_Seed), Nasam_64_Seed),
        new MixSpec(nameof(Nasam_64_SeedMix), Nasam_64_SeedMix),
        new MixSpec(nameof(City_64_Seed), City_64_Seed)
    };

    [Benchmark]
    [ArgumentsSource(nameof(GetFunctions))]
    public ulong MixerBenchmark(MixSpec func)
    {
        return func.Function(42, 42);
    }

    public static IEnumerable<object[]> GetFunctions() => _all.Select(x => new object[] { x });

    public readonly struct MixSpec
    {
        private readonly string _name;
        public readonly Func<ulong, ulong, ulong> Function;

        public MixSpec(string name, Func<ulong, ulong, ulong> function)
        {
            _name = name;
            Function = function;
        }

        public override string ToString() => _name;
    }
}