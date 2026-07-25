namespace Game.Shared.IO;

/// <summary>
///     Pure-managed MD5 (RFC 1321) implementation, used in place of <see cref="System.Security.Cryptography.MD5" />.
/// </summary>
/// <remarks>
///     The BCL's MD5 type carries <c>[UnsupportedOSPlatform("browser")]</c> and throws
///     <c>CryptographicException: Cryptography_UnknownHashAlgorithm</c> at runtime under the WASM browser
///     target (confirmed against a real published Game.Web build, not just the platform attribute) - the
///     browser's underlying Web Crypto API (SubtleCrypto) never supported MD5, since it was deprecated before
///     that API existed. MD5 has no OS-native dependency of its own, so a small self-contained implementation
///     sidesteps the platform gap entirely and runs identically on every host - one code path, not a
///     per-host special case.
/// </remarks>
public static class PortableMd5
{
    private static readonly uint[] K =
    [
        0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
        0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
        0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
        0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
        0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
        0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
        0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
        0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391
    ];

    private static readonly int[] Shift =
    [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21
    ];

    /// <summary>
    ///     Computes the MD5 hash of the remaining bytes in <paramref name="stream" />.
    /// </summary>
    /// <param name="stream">Stream to hash from its current position to its end.</param>
    /// <returns>The 16-byte MD5 digest.</returns>
    public static byte[] ComputeHash(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return ComputeHash(buffer.ToArray());
    }

    /// <summary>
    ///     Computes the MD5 hash of the supplied bytes.
    /// </summary>
    /// <param name="message">Bytes to hash.</param>
    /// <returns>The 16-byte MD5 digest.</returns>
    public static byte[] ComputeHash(ReadOnlySpan<byte> message)
    {
        var originalBitLength = (ulong)message.Length * 8;

        var paddedLength = message.Length + 1;
        while (paddedLength % 64 != 56)
        {
            paddedLength++;
        }

        paddedLength += 8;

        var padded = new byte[paddedLength];
        message.CopyTo(padded);
        padded[message.Length] = 0x80;
        BitConverter.TryWriteBytes(padded.AsSpan(paddedLength - 8), originalBitLength);

        var a0 = 0x67452301u;
        var b0 = 0xefcdab89u;
        var c0 = 0x98badcfeu;
        var d0 = 0x10325476u;

        Span<uint> words = stackalloc uint[16];
        for (var offset = 0; offset < padded.Length; offset += 64)
        {
            var block = padded.AsSpan(offset, 64);
            for (var i = 0; i < 16; i++)
            {
                words[i] = BitConverter.ToUInt32(block.Slice(i * 4, 4));
            }

            var a = a0;
            var b = b0;
            var c = c0;
            var d = d0;

            for (var i = 0; i < 64; i++)
            {
                uint f;
                int g;
                if (i < 16)
                {
                    f = (b & c) | (~b & d);
                    g = i;
                }
                else if (i < 32)
                {
                    f = (d & b) | (~d & c);
                    g = (5 * i + 1) % 16;
                }
                else if (i < 48)
                {
                    f = b ^ c ^ d;
                    g = (3 * i + 5) % 16;
                }
                else
                {
                    f = c ^ (b | ~d);
                    g = (7 * i) % 16;
                }

                f = f + a + K[i] + words[g];
                a = d;
                d = c;
                c = b;
                b += RotateLeft(f, Shift[i]);
            }

            a0 += a;
            b0 += b;
            c0 += c;
            d0 += d;
        }

        var digest = new byte[16];
        BitConverter.TryWriteBytes(digest.AsSpan(0, 4), a0);
        BitConverter.TryWriteBytes(digest.AsSpan(4, 4), b0);
        BitConverter.TryWriteBytes(digest.AsSpan(8, 4), c0);
        BitConverter.TryWriteBytes(digest.AsSpan(12, 4), d0);
        return digest;
    }

    private static uint RotateLeft(uint value, int bits)
    {
        return (value << bits) | (value >> (32 - bits));
    }
}
