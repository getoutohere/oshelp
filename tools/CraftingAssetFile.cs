using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Metadata and selected object reads only: scene textures/meshes never enter memory.
// All objects of a serialized MonoBehaviour type share its script layout. The
// exporter additionally checks the script pointer of every selected object.
public sealed class CraftingAssetObject
{
    public long Id, Position;
    public uint Size;
    public int ClassId, TypeIndex;
}

public sealed class CraftingAssetFile : IDisposable
{
    readonly string path;
    readonly FileStream stream;
    readonly BinaryReader reader;
    readonly Dictionary<long, CraftingAssetObject> byId = new Dictionary<long, CraftingAssetObject>();
    public readonly List<CraftingAssetObject> Objects = new List<CraftingAssetObject>();
    public readonly List<string> ExternalFiles = new List<string>();
    static int Big32(BinaryReader r) { var b = r.ReadBytes(4); Array.Reverse(b); return BitConverter.ToInt32(b, 0); }
    static long Big64(BinaryReader r) { var b = r.ReadBytes(8); Array.Reverse(b); return BitConverter.ToInt64(b, 0); }
    static void Align(BinaryReader r) { r.BaseStream.Position = (r.BaseStream.Position + 3) & ~3L; }
    static int Count(BinaryReader r) { int n = r.ReadInt32(); if (n < 0 || n > 10000000) throw new InvalidDataException("Invalid asset length"); return n; }
    static string ZeroString(BinaryReader r)
    {
        var b = new List<byte>(); byte c;
        while ((c = r.ReadByte()) != 0) { if (b.Count > 1000000) throw new InvalidDataException("Unterminated asset string"); b.Add(c); }
        return Encoding.UTF8.GetString(b.ToArray());
    }
    public CraftingAssetFile(string path)
    {
        this.path = path;
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
        reader = new BinaryReader(stream);
        try
        {
            Big32(reader); Big32(reader); int format = Big32(reader); Big32(reader);
            byte endian = reader.ReadByte(); reader.ReadBytes(3);
            if (format != 22 || endian != 0) throw new InvalidDataException("Unsupported crafting asset format");
            Big32(reader); long fileSize = Big64(reader), dataOffset = Big64(reader); Big64(reader);
            if (fileSize != stream.Length) throw new InvalidDataException("Crafting asset length mismatch");
            ZeroString(reader); reader.ReadInt32();
            if (reader.ReadBoolean()) throw new InvalidDataException("Unsupported crafting asset type tree");
            int typeCount = Count(reader); var types = new List<int>();
            for (int i = 0; i < typeCount; i++)
            {
                int type = reader.ReadInt32(); types.Add(type); reader.ReadBoolean(); reader.ReadInt16();
                if (type == 114) reader.ReadBytes(16);
                reader.ReadBytes(16);
            }
            int objectCount = Count(reader);
            for (int i = 0; i < objectCount; i++)
            {
                Align(reader); long id = reader.ReadInt64(), position = reader.ReadInt64() + dataOffset;
                uint size = reader.ReadUInt32(); int typeIndex = reader.ReadInt32();
                if (typeIndex < 0 || typeIndex >= types.Count || position < dataOffset || position + size > fileSize)
                    throw new InvalidDataException("Crafting object outside file");
                var item = new CraftingAssetObject { Id = id, Position = position, Size = size, TypeIndex = typeIndex, ClassId = types[typeIndex] };
                Objects.Add(item); byId.Add(id, item);
            }
            int scriptCount = Count(reader);
            for (int i = 0; i < scriptCount; i++) { reader.ReadInt32(); Align(reader); reader.ReadInt64(); }
            int externalCount = Count(reader);
            for (int i = 0; i < externalCount; i++) { ZeroString(reader); reader.ReadBytes(16); reader.ReadInt32(); ExternalFiles.Add(ZeroString(reader)); }
        }
        catch { Dispose(); throw; }
    }
    public CraftingAssetObject Object(long id)
    {
        CraftingAssetObject item;
        if (!byId.TryGetValue(id, out item)) throw new InvalidDataException("Unknown crafting object " + id);
        return item;
    }
    public BinaryReader Reader(CraftingAssetObject item)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
        file.Position = item.Position;
        return new BinaryReader(file);
    }
    public List<CraftingAssetObject> FindScripts(long[] scripts)
    {
        var wanted = new HashSet<long>(scripts); var result = new List<CraftingAssetObject>();
        foreach (var group in Objects.Where(o => o.ClassId == 114 && o.Size >= 28).GroupBy(o => o.TypeIndex))
        {
            stream.Position = group.First().Position + 20;
            if (wanted.Contains(reader.ReadInt64())) result.AddRange(group);
        }
        return result;
    }
    public void Dispose() { reader.Dispose(); }
}
