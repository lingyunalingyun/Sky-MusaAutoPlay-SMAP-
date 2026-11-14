using System.IO;

namespace SMAP_WPF.Updates;

public sealed record ContentBlock(long Offset, byte[] Bytes);
public static class ContentChunks
{
    public const string Algorithm = "gear64-v1-32k-128k-512k";
    public const int MinSize = 32 * 1024, MaxSize = 512 * 1024;
    static readonly ulong[] Gear = Enumerable.Range(0, 256).Select(i =>
    {
        unchecked { ulong v = (ulong)i + 0x9e3779b97f4a7c15UL; v = (v ^ (v >> 30)) * 0xbf58476d1ce4e5b9UL; v = (v ^ (v >> 27)) * 0x94d049bb133111ebUL; return v ^ (v >> 31); }
    }).ToArray();

    public static IEnumerable<ContentBlock> Read(Stream stream)
    {
        var block = new byte[MaxSize]; var input = new byte[81920]; int used = 0, read; long offset = 0; ulong fingerprint = 0;
        while ((read = stream.Read(input)) > 0)
            for (int i = 0; i < read; i++)
            {
                byte b = input[i]; block[used++] = b; fingerprint = unchecked((fingerprint << 1) + Gear[b]);
                if (used >= MinSize && ((fingerprint & 131071) == 0 || used == MaxSize))
                {
                    yield return new(offset, block.AsSpan(0, used).ToArray()); offset += used; used = 0; fingerprint = 0;
                }
            }
        if (used > 0) yield return new(offset, block.AsSpan(0, used).ToArray());
    }
}
