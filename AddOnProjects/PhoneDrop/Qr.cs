using System.Text;

namespace CommandHub.AddOn.PhoneDrop;

/// <summary>
/// A small QR code encoder: byte mode, error correction level M, versions 1–10 (up to 213 bytes, plenty for a
/// web address). Follows ISO/IEC 18004 the same way Project Nayuki's reference encoder does.
/// </summary>
public static class Qr
{
    // Level M, versions 1–10: total codewords, blocks, error-correction codewords per block.
    private static readonly int[] RawCodewords = [0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346];
    private static readonly int[] Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];
    private static readonly int[] EccPerBlock = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[][] Alignment =
    [
        [], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
    ];

    /// <summary>The modules (true = dark), [row, column], without the quiet zone. Throws if the text is too long.</summary>
    public static bool[,] Encode(string text, int? forceMask = null)
    {
        var data = Encoding.UTF8.GetBytes(text);
        int version = 0;
        for (int v = 1; v <= 10; v++)
        {
            int capacityBits = (RawCodewords[v] - Blocks[v] * EccPerBlock[v]) * 8;
            int needed = 4 + (v < 10 ? 8 : 16) + data.Length * 8;
            if (needed <= capacityBits) { version = v; break; }
        }
        if (version == 0) throw new ArgumentException("Too long for a QR code here.", nameof(text));

        var codewords = AddEccAndInterleave(DataCodewords(data, version), version);
        int size = version * 4 + 17;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];
        DrawFunctionPatterns(modules, isFunction, version);
        DrawCodewords(modules, isFunction, codewords);

        // Try all eight masks and keep the one with the lowest penalty.
        int bestMask = 0;
        long bestPenalty = long.MaxValue;
        for (int m = 0; m < 8; m++)
        {
            ApplyMask(modules, isFunction, m);
            DrawFormatBits(modules, isFunction, m);
            long p = Penalty(modules);
            if (p < bestPenalty) { bestPenalty = p; bestMask = m; }
            ApplyMask(modules, isFunction, m); // undo (XOR)
        }
        if (forceMask is { } fm) bestMask = fm;
        ApplyMask(modules, isFunction, bestMask);
        DrawFormatBits(modules, isFunction, bestMask);
        return modules;
    }

    private static byte[] DataCodewords(byte[] data, int version)
    {
        int capacity = RawCodewords[version] - Blocks[version] * EccPerBlock[version];
        var bits = new List<bool>();
        void Append(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
        Append(0b0100, 4);                                   // byte mode
        Append(data.Length, version < 10 ? 8 : 16);         // character count
        foreach (var b in data) Append(b, 8);
        Append(0, Math.Min(4, capacity * 8 - bits.Count));  // terminator
        Append(0, (8 - bits.Count % 8) % 8);                // to a byte boundary
        for (int pad = 0xEC; bits.Count < capacity * 8; pad ^= 0xEC ^ 0x11) Append(pad, 8);
        var result = new byte[capacity];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) result[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        return result;
    }

    private static byte[] AddEccAndInterleave(byte[] data, int version)
    {
        int numBlocks = Blocks[version], eccLen = EccPerBlock[version], raw = RawCodewords[version];
        int numShort = numBlocks - raw % numBlocks;
        int shortLen = raw / numBlocks;
        var divisor = Divisor(eccLen);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            int datLen = shortLen - eccLen + (i < numShort ? 0 : 1);
            var dat = data.Skip(k).Take(datLen).ToArray();
            k += datLen;
            var ecc = Remainder(dat, divisor);
            var block = new byte[shortLen + 1];
            Array.Copy(dat, block, dat.Length);
            Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
            blocks.Add(block);
        }
        var result = new List<byte>(raw);
        for (int i = 0; i < blocks[0].Length; i++)
            for (int j = 0; j < blocks.Count; j++)
                if (i != shortLen - eccLen || j >= numShort) result.Add(blocks[j][i]); // short blocks have no byte here
        return [.. result];
    }

    private static int Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z;
    }

    private static byte[] Divisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = (byte)Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] Remainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
        }
        return result;
    }

    private static void Set(bool[,] m, bool[,] f, int x, int y, bool dark)
    {
        m[y, x] = dark;
        f[y, x] = true;
    }

    private static void DrawFunctionPatterns(bool[,] m, bool[,] f, int version)
    {
        int size = m.GetLength(0);
        for (int i = 0; i < size; i++)
        {
            Set(m, f, 6, i, i % 2 == 0);
            Set(m, f, i, 6, i % 2 == 0);
        }
        foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= size || y < 0 || y >= size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(m, f, x, y, dist != 2 && dist != 4);
                }
        var pos = Alignment[version];
        for (int i = 0; i < pos.Length; i++)
            for (int j = 0; j < pos.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0)) continue;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(m, f, pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        DrawFormatBits(m, f, 0); // reserve the format areas (real bits drawn after masking)
        if (version >= 7)
        {
            int rem = version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                Set(m, f, a, b, bit);
                Set(m, f, b, a, bit);
            }
        }
    }

    private static void DrawFormatBits(bool[,] m, bool[,] f, int mask)
    {
        int size = m.GetLength(0);
        int data = 0 << 3 | mask; // level M = 0
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = (data << 10 | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) Set(m, f, 8, i, Bit(i));
        Set(m, f, 8, 7, Bit(6));
        Set(m, f, 8, 8, Bit(7));
        Set(m, f, 7, 8, Bit(8));
        for (int i = 9; i < 15; i++) Set(m, f, 14 - i, 8, Bit(i));
        for (int i = 0; i < 8; i++) Set(m, f, size - 1 - i, 8, Bit(i));
        for (int i = 8; i < 15; i++) Set(m, f, 8, size - 15 + i, Bit(i));
        Set(m, f, 8, size - 8, true); // always dark
    }

    private static void DrawCodewords(bool[,] m, bool[,] f, byte[] data)
    {
        int size = m.GetLength(0);
        int i = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;
                    if (!f[y, x] && i < data.Length * 8)
                    {
                        m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
        }
    }

    private static void ApplyMask(bool[,] m, bool[,] f, int mask)
    {
        int size = m.GetLength(0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (f[y, x]) continue;
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) m[y, x] = !m[y, x];
            }
    }

    /// <summary>A simplified penalty (runs, 2×2 blocks, dark balance): enough to avoid masks that are hard to scan.</summary>
    private static long Penalty(bool[,] m)
    {
        int size = m.GetLength(0);
        long p = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++)
            {
                int run = 1;
                for (int b = 1; b < size; b++)
                {
                    bool cur = pass == 0 ? m[a, b] : m[b, a], prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                    else run = 1;
                }
            }
        for (int y = 0; y < size - 1; y++)
            for (int x = 0; x < size - 1; x++)
                if (m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) p += 3;
        int dark = 0;
        foreach (var v in m) if (v) dark++;
        int total = size * size;
        p += (Math.Abs(dark * 20 - total * 10) + total - 1) / total * 10;
        return p;
    }
}
