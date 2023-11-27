using System.IO.Compression;

namespace UmamusumeResponseAnalyzer
{
    public static class Brotli
    {
        public static byte[] Decompress(byte[] input)
        {
            using var source = new MemoryStream(input);
            using var dest = new MemoryStream();
            using (var brotli = new BrotliStream(source, CompressionMode.Decompress))
                brotli.CopyTo(dest);
            return dest.ToArray();
        }
    }
}
