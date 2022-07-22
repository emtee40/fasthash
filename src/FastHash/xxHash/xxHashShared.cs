﻿using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Genbox.FastHash.xxHash;

internal static class xxHashShared
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong XXH64_avalanche(ulong hash)
    {
        hash ^= hash >> 33;
        hash *= xxHashConstants.PRIME64_2;
        hash ^= hash >> 29;
        hash *= xxHashConstants.PRIME64_3;
        hash ^= hash >> 32;
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong XXH3_avalanche(ulong hash)
    {
        hash ^= hash >> 37;
        hash *= 0x165667919E3779F9UL;
        hash ^= hash >> 32;
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong XXH_mult32to64(ulong x, ulong y) => (uint)x * (ulong)(uint)y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong XXH_xorshift64(ulong v64, int shift) => v64 ^ (v64 >> shift);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe Uint128 XXH_mult64to128(ulong lhs, ulong rhs)
    {
        if (Bmi2.IsSupported)
        {
            ulong product_low;
            ulong product_high = Bmi2.X64.MultiplyNoFlags(lhs, rhs, &product_low);
            return new Uint128(product_low, product_high);
        }

        ulong lo_lo = XXH_mult32to64(lhs & 0xFFFFFFFF, rhs & 0xFFFFFFFF);
        ulong hi_lo = XXH_mult32to64(lhs >> 32, rhs & 0xFFFFFFFF);
        ulong lo_hi = XXH_mult32to64(lhs & 0xFFFFFFFF, rhs >> 32);
        ulong hi_hi = XXH_mult32to64(lhs >> 32, rhs >> 32);

        ulong cross = (lo_lo >> 32) + (hi_lo & 0xFFFFFFFF) + lo_hi;
        ulong upper = (hi_lo >> 32) + (cross >> 32) + hi_hi;
        ulong lower = (cross << 32) | (lo_lo & 0xFFFFFFFF);

        Uint128 r128;
        r128.Low = lower;
        r128.High = upper;
        return r128;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong XXH3_mul128_fold64(ulong lhs, ulong rhs)
    {
        Uint128 product = XXH_mult64to128(lhs, rhs);
        return product.Low ^ product.High;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe ulong XXH3_mergeAccs(ulong* acc, byte* secret, ulong start)
    {
        ulong result64 = start;

        for (int i = 0; i < 4; i++)
            result64 += XXH3_mix2Accs(acc + 2 * i, secret + 16 * i);

        return XXH3_avalanche(result64);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe ulong XXH3_mix2Accs(ulong* acc, byte* secret) => XXH3_mul128_fold64(acc[0] ^ Utilities.Read64(secret), acc[1] ^ Utilities.Read64(secret + 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_hashLong_internal_loop(ulong* acc, byte* input, int len, byte* secret, int secretSize, XXH3_f_accumulate_512 f_acc512, XXH3_f_scrambleAcc f_scramble)
    {
        int nbStripesPerBlock = (secretSize - xxHashConstants.STRIPE_LEN) / xxHashConstants.SECRET_CONSUME_RATE;
        int block_len = xxHashConstants.STRIPE_LEN * nbStripesPerBlock;
        int nb_blocks = (len - 1) / block_len;

        for (int n = 0; n < nb_blocks; n++)
        {
            XXH3_accumulate(acc, input + n * block_len, secret, nbStripesPerBlock, f_acc512);
            f_scramble(acc, secret + secretSize - xxHashConstants.STRIPE_LEN);
        }

        /* last partial block */
        //  XXH_ASSERT(len > XXH_STRIPE_LEN);
        int nbStripes = (len - 1 - block_len * nb_blocks) / xxHashConstants.STRIPE_LEN;
        // XXH_ASSERT(nbStripes <= (secretSize / XXH_SECRET_CONSUME_RATE));
        XXH3_accumulate(acc, input + nb_blocks * block_len, secret, nbStripes, f_acc512);

        byte* p = input + len - xxHashConstants.STRIPE_LEN;
        f_acc512(acc, p, secret + secretSize - xxHashConstants.STRIPE_LEN - xxHashConstants.SECRET_LASTACC_START);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void XXH3_accumulate(ulong* acc, byte* input, byte* secret, int nbStripes, XXH3_f_accumulate_512 f_acc512)
    {
        for (int n = 0; n < nbStripes; n++)
        {
            byte* inp = input + n * xxHashConstants.STRIPE_LEN;
            f_acc512(acc, inp, secret + n * xxHashConstants.SECRET_CONSUME_RATE);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_accumulate_512_avx2(ulong* acc, byte* input, byte* secret)
    {
        Vector256<ulong> acc_vec0 = Unsafe.Read<Vector256<ulong>>(acc + 0);
        Vector256<ulong> acc_vec1 = Unsafe.Read<Vector256<ulong>>(acc + 4);

        Vector256<uint> data_vec0 = Unsafe.Read<Vector256<ulong>>((ulong*)input + 0).AsUInt32();
        Vector256<uint> data_vec1 = Unsafe.Read<Vector256<ulong>>((ulong*)input + 4).AsUInt32();

        Vector256<uint> key_vec0 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 0).AsUInt32();
        Vector256<uint> key_vec1 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 4).AsUInt32();

        Vector256<uint> data_key0 = Avx2.Xor(data_vec0, key_vec0);
        Vector256<uint> data_key1 = Avx2.Xor(data_vec1, key_vec1);

        Vector256<uint> data_key_lo0 = Avx2.Shuffle(data_key0, xxHashConstants.MM_SHUFFLE_0_3_0_1);
        Vector256<uint> data_key_lo1 = Avx2.Shuffle(data_key1, xxHashConstants.MM_SHUFFLE_0_3_0_1);

        Vector256<ulong> product0 = Avx2.Multiply(data_key0, data_key_lo0);
        Vector256<ulong> product1 = Avx2.Multiply(data_key1, data_key_lo1);

        Vector256<ulong> data_swap0 = Avx2.Shuffle(data_vec0, xxHashConstants.MM_SHUFFLE_1_0_3_2).AsUInt64();
        Vector256<ulong> data_swap1 = Avx2.Shuffle(data_vec1, xxHashConstants.MM_SHUFFLE_1_0_3_2).AsUInt64();

        Vector256<ulong> sum0 = Avx2.Add(acc_vec0, data_swap0);
        Vector256<ulong> sum1 = Avx2.Add(acc_vec1, data_swap1);

        Vector256<ulong> result0 = Avx2.Add(product0, sum0);
        Vector256<ulong> result1 = Avx2.Add(product1, sum1);

        Unsafe.Write(acc + 0, result0);
        Unsafe.Write(acc + 4, result1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_accumulate_512_sse2(ulong* acc, byte* input, byte* secret)
    {
        Vector128<ulong> acc_vec0 = Unsafe.Read<Vector128<ulong>>(acc + 0);
        Vector128<ulong> acc_vec1 = Unsafe.Read<Vector128<ulong>>(acc + 2);
        Vector128<ulong> acc_vec2 = Unsafe.Read<Vector128<ulong>>(acc + 4);
        Vector128<ulong> acc_vec3 = Unsafe.Read<Vector128<ulong>>(acc + 6);

        Vector128<uint> data_vec0 = Unsafe.Read<Vector128<ulong>>((ulong*)input + 0).AsUInt32();
        Vector128<uint> data_vec1 = Unsafe.Read<Vector128<ulong>>((ulong*)input + 2).AsUInt32();
        Vector128<uint> data_vec2 = Unsafe.Read<Vector128<ulong>>((ulong*)input + 4).AsUInt32();
        Vector128<uint> data_vec3 = Unsafe.Read<Vector128<ulong>>((ulong*)input + 6).AsUInt32();

        Vector128<uint> key_vec0 = Unsafe.Read<Vector128<ulong>>((ulong*)secret + 0).AsUInt32();
        Vector128<uint> key_vec1 = Unsafe.Read<Vector128<ulong>>((ulong*)secret + 2).AsUInt32();
        Vector128<uint> key_vec2 = Unsafe.Read<Vector128<ulong>>((ulong*)secret + 4).AsUInt32();
        Vector128<uint> key_vec3 = Unsafe.Read<Vector128<ulong>>((ulong*)secret + 6).AsUInt32();

        Vector128<uint> data_key0 = Sse2.Xor(data_vec0, key_vec0);
        Vector128<uint> data_key1 = Sse2.Xor(data_vec1, key_vec1);
        Vector128<uint> data_key2 = Sse2.Xor(data_vec2, key_vec2);
        Vector128<uint> data_key3 = Sse2.Xor(data_vec3, key_vec3);

        Vector128<uint> data_key_lo0 = Sse2.Shuffle(data_key0, xxHashConstants.MM_SHUFFLE_0_3_0_1);
        Vector128<uint> data_key_lo1 = Sse2.Shuffle(data_key1, xxHashConstants.MM_SHUFFLE_0_3_0_1);
        Vector128<uint> data_key_lo2 = Sse2.Shuffle(data_key2, xxHashConstants.MM_SHUFFLE_0_3_0_1);
        Vector128<uint> data_key_lo3 = Sse2.Shuffle(data_key3, xxHashConstants.MM_SHUFFLE_0_3_0_1);

        Vector128<ulong> product0 = Sse2.Multiply(data_key0, data_key_lo0);
        Vector128<ulong> product1 = Sse2.Multiply(data_key1, data_key_lo1);
        Vector128<ulong> product2 = Sse2.Multiply(data_key2, data_key_lo2);
        Vector128<ulong> product3 = Sse2.Multiply(data_key3, data_key_lo3);

        Vector128<ulong> data_swap0 = Sse2.Shuffle(data_vec0, xxHashConstants.MM_SHUFFLE_1_0_3_2).AsUInt64();
        Vector128<ulong> data_swap1 = Sse2.Shuffle(data_vec1, xxHashConstants.MM_SHUFFLE_1_0_3_2).AsUInt64();
        Vector128<ulong> data_swap2 = Sse2.Shuffle(data_vec2, xxHashConstants.MM_SHUFFLE_1_0_3_2).AsUInt64();
        Vector128<ulong> data_swap3 = Sse2.Shuffle(data_vec3, xxHashConstants.MM_SHUFFLE_1_0_3_2).AsUInt64();

        Vector128<ulong> sum0 = Sse2.Add(acc_vec0, data_swap0);
        Vector128<ulong> sum1 = Sse2.Add(acc_vec1, data_swap1);
        Vector128<ulong> sum2 = Sse2.Add(acc_vec2, data_swap2);
        Vector128<ulong> sum3 = Sse2.Add(acc_vec3, data_swap3);

        Vector128<ulong> result0 = Sse2.Add(product0, sum0);
        Vector128<ulong> result1 = Sse2.Add(product1, sum1);
        Vector128<ulong> result2 = Sse2.Add(product2, sum2);
        Vector128<ulong> result3 = Sse2.Add(product3, sum3);

        Unsafe.Write(acc + 0, result0);
        Unsafe.Write(acc + 2, result1);
        Unsafe.Write(acc + 4, result2);
        Unsafe.Write(acc + 6, result3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_accumulate_512_scalar(ulong* acc, byte* input, byte* secret)
    {
        for (int i = 0; i < xxHashConstants.ACC_NB; i++)
            XXH3_scalarRound(acc, input, secret, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void XXH3_scalarRound(ulong* acc, byte* input, byte* secret, int lane)
    {
        ulong* xacc = acc;
        byte* xinput = input;
        byte* xsecret = secret;

        ulong data_val = Utilities.Read64(xinput + lane * 8);
        ulong data_key = data_val ^ Utilities.Read64(xsecret + lane * 8);
        xacc[lane ^ 1] += data_val;
        xacc[lane] += XXH_mult32to64(data_key & 0xFFFFFFFF, data_key >> 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_scrambleAcc_avx2(ulong* acc, byte* secret)
    {
        Vector256<ulong> acc_vec0 = Unsafe.Read<Vector256<ulong>>(acc + 0);
        Vector256<ulong> acc_vec1 = Unsafe.Read<Vector256<ulong>>(acc + 4);

        Vector256<ulong> shifted0 = Avx2.ShiftRightLogical(acc_vec0, 47);
        Vector256<ulong> shifted1 = Avx2.ShiftRightLogical(acc_vec1, 47);

        Vector256<ulong> data_vec0 = Avx2.Xor(acc_vec0, shifted0);
        Vector256<ulong> data_vec1 = Avx2.Xor(acc_vec1, shifted1);

        Vector256<ulong> key_vec0 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 0);
        Vector256<ulong> key_vec1 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 4);

        Vector256<uint> data_key0 = Avx2.Xor(data_vec0, key_vec0).AsUInt32();
        Vector256<uint> data_key1 = Avx2.Xor(data_vec1, key_vec1).AsUInt32();

        Vector256<uint> data_key_hi0 = Avx2.Shuffle(data_key0, xxHashConstants.MM_SHUFFLE_0_3_0_1);
        Vector256<uint> data_key_hi1 = Avx2.Shuffle(data_key1, xxHashConstants.MM_SHUFFLE_0_3_0_1);

        Vector256<ulong> prod_lo0 = Avx2.Multiply(data_key0, xxHashConstants.M256i_XXH_PRIME32_1);
        Vector256<ulong> prod_lo1 = Avx2.Multiply(data_key1, xxHashConstants.M256i_XXH_PRIME32_1);

        Vector256<ulong> prod_hi0 = Avx2.Multiply(data_key_hi0, xxHashConstants.M256i_XXH_PRIME32_1);
        Vector256<ulong> prod_hi1 = Avx2.Multiply(data_key_hi1, xxHashConstants.M256i_XXH_PRIME32_1);

        Vector256<ulong> result0 = Avx2.Add(prod_lo0, Avx2.ShiftLeftLogical(prod_hi0, 32));
        Vector256<ulong> result1 = Avx2.Add(prod_lo1, Avx2.ShiftLeftLogical(prod_hi1, 32));

        Unsafe.Write(acc + 0, result0);
        Unsafe.Write(acc + 4, result1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_scrambleAcc_sse2(ulong* acc, byte* secret)
    {
        Vector128<uint> acc_vec0 = Unsafe.Read<Vector128<ulong>>(acc + 0).AsUInt32();
        Vector128<uint> acc_vec1 = Unsafe.Read<Vector128<ulong>>(acc + 2).AsUInt32();
        Vector128<uint> acc_vec2 = Unsafe.Read<Vector128<ulong>>(acc + 4).AsUInt32();
        Vector128<uint> acc_vec3 = Unsafe.Read<Vector128<ulong>>(acc + 6).AsUInt32();

        Vector128<uint> shifted0 = Sse2.ShiftRightLogical(acc_vec0, 47);
        Vector128<uint> shifted1 = Sse2.ShiftRightLogical(acc_vec1, 47);
        Vector128<uint> shifted2 = Sse2.ShiftRightLogical(acc_vec2, 47);
        Vector128<uint> shifted3 = Sse2.ShiftRightLogical(acc_vec3, 47);

        Vector128<uint> data_vec0 = Sse2.Xor(acc_vec0, shifted0);
        Vector128<uint> data_vec1 = Sse2.Xor(acc_vec1, shifted1);
        Vector128<uint> data_vec2 = Sse2.Xor(acc_vec2, shifted2);
        Vector128<uint> data_vec3 = Sse2.Xor(acc_vec3, shifted3);

        Vector128<uint> key_vec0 = Unsafe.Read<Vector128<ulong>>((ulong*)secret + 0).AsUInt32();
        Vector128<uint> key_vec1 = Unsafe.Read<Vector128<ulong>>((ulong*)secret + 2).AsUInt32();
        Vector128<uint> key_vec2 = Unsafe.Read<Vector128<ulong>>((ulong*)secret + 4).AsUInt32();
        Vector128<uint> key_vec3 = Unsafe.Read<Vector128<ulong>>((ulong*)secret + 6).AsUInt32();

        Vector128<uint> data_key0 = Sse2.Xor(data_vec0, key_vec0);
        Vector128<uint> data_key1 = Sse2.Xor(data_vec1, key_vec1);
        Vector128<uint> data_key2 = Sse2.Xor(data_vec2, key_vec2);
        Vector128<uint> data_key3 = Sse2.Xor(data_vec3, key_vec3);

        Vector128<uint> data_key_hi0 = Sse2.Shuffle(data_key0.AsUInt32(), xxHashConstants.MM_SHUFFLE_0_3_0_1);
        Vector128<uint> data_key_hi1 = Sse2.Shuffle(data_key1.AsUInt32(), xxHashConstants.MM_SHUFFLE_0_3_0_1);
        Vector128<uint> data_key_hi2 = Sse2.Shuffle(data_key2.AsUInt32(), xxHashConstants.MM_SHUFFLE_0_3_0_1);
        Vector128<uint> data_key_hi3 = Sse2.Shuffle(data_key3.AsUInt32(), xxHashConstants.MM_SHUFFLE_0_3_0_1);

        Vector128<ulong> prod_lo0 = Sse2.Multiply(data_key0, xxHashConstants.M128i_XXH_PRIME32_1);
        Vector128<ulong> prod_lo1 = Sse2.Multiply(data_key1, xxHashConstants.M128i_XXH_PRIME32_1);
        Vector128<ulong> prod_lo2 = Sse2.Multiply(data_key2, xxHashConstants.M128i_XXH_PRIME32_1);
        Vector128<ulong> prod_lo3 = Sse2.Multiply(data_key3, xxHashConstants.M128i_XXH_PRIME32_1);

        Vector128<ulong> prod_hi0 = Sse2.Multiply(data_key_hi0, xxHashConstants.M128i_XXH_PRIME32_1);
        Vector128<ulong> prod_hi1 = Sse2.Multiply(data_key_hi1, xxHashConstants.M128i_XXH_PRIME32_1);
        Vector128<ulong> prod_hi2 = Sse2.Multiply(data_key_hi2, xxHashConstants.M128i_XXH_PRIME32_1);
        Vector128<ulong> prod_hi3 = Sse2.Multiply(data_key_hi3, xxHashConstants.M128i_XXH_PRIME32_1);

        Vector128<ulong> result0 = Sse2.Add(prod_lo0, Sse2.ShiftLeftLogical(prod_hi0, 32));
        Vector128<ulong> result1 = Sse2.Add(prod_lo1, Sse2.ShiftLeftLogical(prod_hi1, 32));
        Vector128<ulong> result2 = Sse2.Add(prod_lo2, Sse2.ShiftLeftLogical(prod_hi2, 32));
        Vector128<ulong> result3 = Sse2.Add(prod_lo3, Sse2.ShiftLeftLogical(prod_hi3, 32));

        Unsafe.Write(acc + 0, result0);
        Unsafe.Write(acc + 2, result1);
        Unsafe.Write(acc + 4, result2);
        Unsafe.Write(acc + 6, result3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_scrambleAcc_scalar(ulong* acc, byte* secret)
    {
        for (int i = 0; i < xxHashConstants.ACC_NB; i++)
            XXH3_scalarScrambleRound(acc, secret, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void XXH3_scalarScrambleRound(ulong* acc, byte* secret, int lane)
    {
        ulong* xacc = acc;
        byte* xsecret = secret;

        ulong key64 = Utilities.Read64(xsecret + lane * 8);
        ulong acc64 = xacc[lane];
        acc64 = XXH_xorshift64(acc64, 47);
        acc64 ^= key64;
        acc64 *= xxHashConstants.PRIME32_1;
        xacc[lane] = acc64;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_initCustomSecret_avx2(byte* customSecret, ulong seed64)
    {
        Vector256<ulong> seed = Vector256.Create(seed64, 0U - seed64, seed64, 0U - seed64);

        fixed (byte* secret = &xxHashConstants.kSecret[0])
        {
            Vector256<ulong> src0 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 0);
            Vector256<ulong> src1 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 4);
            Vector256<ulong> src2 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 8);
            Vector256<ulong> src3 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 12);
            Vector256<ulong> src4 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 16);
            Vector256<ulong> src5 = Unsafe.Read<Vector256<ulong>>((ulong*)secret + 20);

            Vector256<ulong> dst0 = Avx2.Add(src0, seed);
            Vector256<ulong> dst1 = Avx2.Add(src1, seed);
            Vector256<ulong> dst2 = Avx2.Add(src2, seed);
            Vector256<ulong> dst3 = Avx2.Add(src3, seed);
            Vector256<ulong> dst4 = Avx2.Add(src4, seed);
            Vector256<ulong> dst5 = Avx2.Add(src5, seed);

            Unsafe.Write((ulong*)customSecret + 0, dst0);
            Unsafe.Write((ulong*)customSecret + 4, dst1);
            Unsafe.Write((ulong*)customSecret + 8, dst2);
            Unsafe.Write((ulong*)customSecret + 12, dst3);
            Unsafe.Write((ulong*)customSecret + 16, dst4);
            Unsafe.Write((ulong*)customSecret + 20, dst5);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_initCustomSecret_sse2(byte* customSecret, ulong seed64)
    {
        Vector128<long> seed = Vector128.Create((long)seed64, (long)(0U - seed64));

        fixed (byte* secret = &xxHashConstants.kSecret[0])
        {
            Vector128<long> src0 = Unsafe.Read<Vector128<long>>((long*)secret + 0);
            Vector128<long> src1 = Unsafe.Read<Vector128<long>>((long*)secret + 2);
            Vector128<long> src2 = Unsafe.Read<Vector128<long>>((long*)secret + 4);
            Vector128<long> src3 = Unsafe.Read<Vector128<long>>((long*)secret + 6);
            Vector128<long> src4 = Unsafe.Read<Vector128<long>>((long*)secret + 8);
            Vector128<long> src5 = Unsafe.Read<Vector128<long>>((long*)secret + 10);
            Vector128<long> src6 = Unsafe.Read<Vector128<long>>((long*)secret + 12);
            Vector128<long> src7 = Unsafe.Read<Vector128<long>>((long*)secret + 14);
            Vector128<long> src8 = Unsafe.Read<Vector128<long>>((long*)secret + 16);
            Vector128<long> src9 = Unsafe.Read<Vector128<long>>((long*)secret + 18);
            Vector128<long> src10 = Unsafe.Read<Vector128<long>>((long*)secret + 20);
            Vector128<long> src11 = Unsafe.Read<Vector128<long>>((long*)secret + 22);

            Vector128<long> dst0 = Sse2.Add(src0, seed);
            Vector128<long> dst1 = Sse2.Add(src1, seed);
            Vector128<long> dst2 = Sse2.Add(src2, seed);
            Vector128<long> dst3 = Sse2.Add(src3, seed);
            Vector128<long> dst4 = Sse2.Add(src4, seed);
            Vector128<long> dst5 = Sse2.Add(src5, seed);
            Vector128<long> dst6 = Sse2.Add(src6, seed);
            Vector128<long> dst7 = Sse2.Add(src7, seed);
            Vector128<long> dst8 = Sse2.Add(src8, seed);
            Vector128<long> dst9 = Sse2.Add(src9, seed);
            Vector128<long> dst10 = Sse2.Add(src10, seed);
            Vector128<long> dst11 = Sse2.Add(src11, seed);

            Unsafe.Write((long*)customSecret + 0, dst0);
            Unsafe.Write((long*)customSecret + 2, dst1);
            Unsafe.Write((long*)customSecret + 4, dst2);
            Unsafe.Write((long*)customSecret + 6, dst3);
            Unsafe.Write((long*)customSecret + 8, dst4);
            Unsafe.Write((long*)customSecret + 10, dst5);
            Unsafe.Write((long*)customSecret + 12, dst6);
            Unsafe.Write((long*)customSecret + 14, dst7);
            Unsafe.Write((long*)customSecret + 16, dst8);
            Unsafe.Write((long*)customSecret + 18, dst9);
            Unsafe.Write((long*)customSecret + 20, dst10);
            Unsafe.Write((long*)customSecret + 22, dst11);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void XXH3_initCustomSecret_scalar(byte* customSecret, ulong seed)
    {
        fixed (byte* kSecretPtr = &xxHashConstants.kSecret[0])
        {
            int nbRounds = xxHashConstants.SECRET_DEFAULT_SIZE / 16;

            for (int i = 0; i < nbRounds; i++)
            {
                ulong lo = Utilities.Read64(kSecretPtr + 16 * i) + seed;
                ulong hi = Utilities.Read64(kSecretPtr + 16 * i + 8) - seed;
                Utilities.Write64(customSecret + 16 * i, lo);
                Utilities.Write64(customSecret + 16 * i + 8, hi);
            }
        }
    }

    internal static unsafe ulong XXH3_mix16B(byte* input, byte* secret, ulong seed64)
    {
        ulong input_lo = Utilities.Read64(input);
        ulong input_hi = Utilities.Read64(input, 8);

        return XXH3_mul128_fold64(
            input_lo ^ (Utilities.Read64(secret) + seed64),
            input_hi ^ (Utilities.Read64(secret, 8) - seed64)
        );
    }
}