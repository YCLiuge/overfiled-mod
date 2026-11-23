using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace DrawGuessModule.Core;

/// <summary>A managed, lossless PNG encoder for bottom-left-origin RGBA8 pixels.</summary>
public static class PngWriter
{
    // Bound allocation and compression work before doing any size arithmetic or copying.
    private const int MaximumDimension = 16384;
    private const long MaximumRgbaBytes = 256L * 1024 * 1024;
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    private static readonly byte[] HeaderType = { 73, 72, 68, 82 };
    private static readonly byte[] DataType = { 73, 68, 65, 84 };
    private static readonly byte[] EndType = { 73, 69, 78, 68 };
    private static readonly uint[] CrcTable = CreateCrcTable();

    /// <summary>
    /// Encodes tightly packed RGBA8 rows, starting at the bottom row, as a top-left-origin PNG.
    /// Dimensions must be 1..16384 and the exact input size must not exceed 256 MiB.
    /// </summary>
    public static byte[] EncodeRgba(int width, int height, byte[] bottomUpRgba)
    {
        if (bottomUpRgba == null) throw new ArgumentNullException(nameof(bottomUpRgba));
        if (width < 1 || width > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1 || height > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(height));
        long requiredBytes = (long)width * height * 4;
        if (requiredBytes > MaximumRgbaBytes)
            throw new ArgumentOutOfRangeException(nameof(height), "RGBA image size must not exceed 256 MiB.");
        if (bottomUpRgba.LongLength != requiredBytes)
            throw new ArgumentException("RGBA data must contain exactly width * height * 4 bytes.", nameof(bottomUpRgba));

        int rowBytes = checked(width * 4);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = height - 1; y >= 0; y--)
            {
                zlib.WriteByte(0); // PNG filter 0: the unmodified RGBA row.
                zlib.Write(bottomUpRgba, checked(y * rowBytes), rowBytes);
            }
        }

        using var png = new MemoryStream(checked((int)compressed.Length + 57));
        png.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4), (uint)height);
        header[8] = 8; // Bit depth.
        header[9] = 6; // RGBA.
        header[10] = 0; // Deflate compression.
        header[11] = 0; // Adaptive filtering (each row chooses filter 0).
        header[12] = 0; // No interlace.
        WriteChunk(png, HeaderType, header);
        WriteChunk(png, DataType, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        WriteChunk(png, EndType, ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, byte[] type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        uint crc = UpdateCrc(uint.MaxValue, type);
        crc = UpdateCrc(crc, data) ^ uint.MaxValue;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) crc = CrcTable[(int)((crc ^ value) & 255)] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (int i = 0; i < table.Length; i++)
        {
            uint value = (uint)i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320U ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
