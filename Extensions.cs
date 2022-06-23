using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;

namespace rgit;

public static class Extensions
{
    public static void DrawTextWithClip(this DrawingContext context, IBrush foreground, Point origin, Size clipSize, FormattedText text)
    {
        using (context.PushClip(new Rect(origin, clipSize)))
            context.DrawText(foreground, origin, text);
    }

    public static double Distance(this Point a, Point b)
    {
        var x = a.X - b.X;
        var y = a.Y - b.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    public static string WorkingDirectory(this LibGit2Sharp.Repository repo) => repo.Info.WorkingDirectory;

    public static uint Murmur2(this string s) => Murmur2(s.AsSpan(), 0);

    public static uint Murmur2(ReadOnlySpan<char> data, uint seed = 0)
    {
        var bytes = MemoryMarshal.AsBytes(data);
        return Murmur2(bytes, seed);
    }

    public static unsafe uint Murmur2(ReadOnlySpan<byte> data, uint seed = 0)
    {
        if (data.Length == 0)
            return 0;

        const uint m = 0x5bd1e995;
        const int r = 24;

        var len = (uint)data.Length;
        var h = seed ^ len;

        fixed (byte* fixedData = &data[0])
        {
            var pData = fixedData;
            while (len >= 4)
            {
                var k = *(uint*)pData;

                k *= m;
                k ^= k >> r;
                k *= m;

                h *= m;
                h ^= k;

                pData += 4;
                len -= 4;
            }

            // Handle the last few bytes of the input array
#pragma warning disable S907 // Goto
            switch (len)
            {
                case 3:
                    h ^= (uint)(pData[2] << 16);
                    goto case 2;
                case 2:
                    h ^= (uint)(pData[1] << 8);
                    goto case 1;
                case 1:
                    h ^= pData[0];
                    h *= m;
                    break;
            }
#pragma warning restore

            // Do a few final mixes of the hash to ensure the last few
            // bytes are well-incorporated.
            h ^= h >> 13;
            h *= m;
            h ^= h >> 15;
        }

        return h;
    }
}