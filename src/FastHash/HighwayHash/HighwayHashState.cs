﻿using System.Runtime.CompilerServices;

namespace Genbox.FastHash.HighwayHash;

[SkipLocalsInit]
internal struct HighwayHashState
{
    internal ulong mul0_0;
    internal ulong mul0_1;
    internal ulong mul0_2;
    internal ulong mul0_3;
    internal ulong mul1_0;
    internal ulong mul1_1;
    internal ulong mul1_2;
    internal ulong mul1_3;
    internal ulong v0_0;
    internal ulong v0_1;
    internal ulong v0_2;
    internal ulong v0_3;
    internal ulong v1_0;
    internal ulong v1_1;
    internal ulong v1_2;
    internal ulong v1_3;

    public HighwayHashState()
    {
        mul0_0 = 0;
        mul0_1 = 0;
        mul0_2 = 0;
        mul0_3 = 0;
        mul1_0 = 0;
        mul1_1 = 0;
        mul1_2 = 0;
        mul1_3 = 0;
        v0_0 = 0;
        v0_1 = 0;
        v0_2 = 0;
        v0_3 = 0;
        v1_0 = 0;
        v1_1 = 0;
        v1_2 = 0;
        v1_3 = 0;
    }
}