using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace EndlessMode;

internal static class GeneratedInteropIdentity
{
    // The pinned loader writes a fresh module GUID on every generation. Ignore
    // only that metadata-selected entry; all IL, signatures, layouts, names,
    // references, headers and every other image byte remain part of the hash.
    internal static string Hash(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, false));
        var reader = pe.GetMetadataReader();
        if (reader.GetTableRowCount(TableIndex.Module) != 1)
            throw new InvalidDataException("Generated interop must contain one module.");
        var mvid = reader.GetModuleDefinition().Mvid;
        if (mvid.IsNil || reader.GetGuid(mvid) == Guid.Empty)
            throw new InvalidDataException("Generated interop has no module identity.");
        // GUID handles are one-based entry indexes, not byte offsets.
        int entryOffset = checked((MetadataTokens.GetHeapOffset(mvid) - 1) * 16);
        int heapSize = reader.GetHeapSize(HeapIndex.Guid);
        if (entryOffset < 0 || entryOffset > heapSize - 16)
            throw new InvalidDataException("Generated interop module identity is outside the GUID heap.");
        int metadataOffset = checked(reader.GetHeapMetadataOffset(HeapIndex.Guid) + entryOffset);
        int offset = checked(pe.PEHeaders.MetadataStartOffset + metadataOffset);
        if (metadataOffset < 0 || metadataOffset > pe.PEHeaders.MetadataSize - 16
            || offset < 0 || offset > image.Length - 16
            || !image.AsSpan(offset, 16).SequenceEqual(reader.GetGuid(mvid).ToByteArray()))
            throw new InvalidDataException("Generated interop module identity has invalid bounds.");
        var normalized = (byte[])image.Clone();
        Array.Clear(normalized, offset, 16);
        return Convert.ToHexString(SHA256.HashData(normalized));
    }
}
