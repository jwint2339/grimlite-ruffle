using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Grimoire.Runtime
{
    /// <summary>Adapts only Externalizer.call in the bundled loader to Mugetsu's event ABI.</summary>
    internal static class RuffleLoader
    {
        // Offsets and bytecode below are valid ONLY for this exact, checked-in SWF.
        // A different loader must be inspected and supported explicitly, never patched by guessing.
        private const string SourceHash = "BB3CF5ABAA939B96F62B57CAFAACF03E70267B4976F099A6525EF88D1A06120E";
        private static readonly byte[] OriginalMethod = Hex("86010303040517d030ef0187040000ef01bb03010060ff0ad1d246120248");
        private static readonly byte[] AdaptedMethod = Hex("8601040304051ad030ef0187040000ef01bb03010060ff0a2caa08d1d246120348");

        public static byte[] Adapt(byte[] source)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "");
                if (hash != SourceHash)
                    throw new InvalidDataException("This game loader is not supported by the Ruffle adapter. Use the bundled grimoire.swf or select Flash.");
            }
            byte[] movie;
            using (var uncompressed = new MemoryStream())
            {
                uncompressed.Write(source, 0, 8);
                // CWS uses zlib framing; DeflateStream on .NET Framework reads the raw deflate payload.
                using (var compressed = new MemoryStream(source, 10, source.Length - 14))
                using (var deflate = new DeflateStream(compressed, CompressionMode.Decompress))
                    deflate.CopyTo(uncompressed);
                movie = uncompressed.ToArray();
            }
            if (movie.Length != BitConverter.ToUInt32(source, 4))
                throw new InvalidDataException("Invalid loader length.");

            int position = 8 + (5 + (movie[8] >> 3) * 4 + 7) / 8 + 4;
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(movie, 0, position);
                int adaptedTags = 0;
                while (position < movie.Length)
                {
                    int start = position;
                    int header = BitConverter.ToUInt16(movie, position);
                    position += 2;
                    int length = header & 63;
                    if (length == 63) { length = BitConverter.ToInt32(movie, position); position += 4; }
                    if ((header >> 6) == 82)
                    {
                        // DoABC: four flag bytes, null-terminated name, then the ABC payload.
                        int abcStart = Array.IndexOf(movie, (byte)0, position + 4) + 1;
                        if (abcStart <= position + 4 || abcStart >= position + length)
                            throw new InvalidDataException("Invalid loader ABC tag.");
                        byte[] abc = movie.Skip(abcStart).Take(position + length - abcStart).ToArray();
                        byte[] adapted = AdaptAbc(abc);
                        writer.Write((ushort)((82 << 6) | 63));
                        writer.Write(abcStart - position + adapted.Length);
                        writer.Write(movie, position, abcStart - position);
                        writer.Write(adapted);
                        adaptedTags++;
                    }
                    else writer.Write(movie, start, position + length - start);
                    position += length;
                }
                if (adaptedTags != 1) throw new InvalidDataException("Expected one loader ABC tag.");
                byte[] result = output.ToArray();
                result[0] = (byte)'F'; // Emit uncompressed FWS; the original file is never overwritten.
                Array.Copy(BitConverter.GetBytes(result.Length), 0, result, 4, 4);
                return result;
            }
        }

        private static byte[] AdaptAbc(byte[] abc)
        {
            const int stringCountOffset = 43, stringPoolEnd = 15183, methodOffset = 37448;
            if (abc.Length != 48919 || abc[stringCountOffset] != 0xAA || abc[stringCountOffset + 1] != 0x08 ||
                !abc.Skip(methodOffset).Take(OriginalMethod.Length).SequenceEqual(OriginalMethod))
                throw new InvalidDataException("The loader bridge bytecode does not match the supported version.");
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(abc, 0, stringCountOffset);
                writer.Write(new byte[] { 0xAB, 0x08 }); // String count: 1066 -> 1067.
                writer.Write(abc, stringCountOffset + 2, stringPoolEnd - stringCountOffset - 2);
                writer.Write((byte)8);
                writer.Write(Encoding.UTF8.GetBytes("skuaCall")); // New string index 1066 (AA 08).
                writer.Write(abc, stringPoolEnd, methodOffset - stringPoolEnd);
                // max_stack 3 -> 4, code_length 23 -> 26; add pushstring and call with 3 args.
                // ExternalInterface.call(name, rest) -> ExternalInterface.call("skuaCall", name, rest).
                writer.Write(AdaptedMethod);
                writer.Write(abc, methodOffset + OriginalMethod.Length, abc.Length - methodOffset - OriginalMethod.Length);
                return output.ToArray();
            }
        }

        private static byte[] Hex(string text)
        {
            return Enumerable.Range(0, text.Length / 2).Select(i => Convert.ToByte(text.Substring(i * 2, 2), 16)).ToArray();
        }
    }
}
