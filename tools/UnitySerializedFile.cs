// Investigation reader for this installation's stripped Unity serialized files.
// Reads data only. Deliberately rejects layouts it has not validated.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

public sealed class UnityObjectRecord
{
    public long Id, Position;
    public uint Size;
    public int ClassId;
}

public sealed class UnityResearchFile
{
    public readonly byte[] Data;
    public readonly List<UnityObjectRecord> Objects = new List<UnityObjectRecord>();
    readonly Dictionary<long, UnityObjectRecord> objectsById = new Dictionary<long, UnityObjectRecord>();
    public readonly List<string> ExternalFiles = new List<string>();
    public readonly string UnityVersion;
    public readonly uint FormatVersion;

    static uint Big32(BinaryReader r) { var b = r.ReadBytes(4); Array.Reverse(b); return BitConverter.ToUInt32(b, 0); }
    static long Big64(BinaryReader r) { var b = r.ReadBytes(8); Array.Reverse(b); return BitConverter.ToInt64(b, 0); }
    public static void Align(BinaryReader r) { r.BaseStream.Position = (r.BaseStream.Position + 3) & ~3L; }
    public static string ZeroString(BinaryReader r)
    {
        var bytes = new List<byte>(); byte c;
        while ((c = r.ReadByte()) != 0) { if (bytes.Count > 1000000) throw new InvalidDataException("Unterminated string"); bytes.Add(c); }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    public static int Count(BinaryReader r)
    {
        int n = r.ReadInt32();
        if (n < 0 || n > 10000000) throw new InvalidDataException("Invalid length: " + n);
        return n;
    }
    public static string String(BinaryReader r)
    {
        int n = Count(r); var bytes = r.ReadBytes(n);
        if (bytes.Length != n) throw new EndOfStreamException();
        string value = Encoding.UTF8.GetString(bytes); Align(r); return value;
    }
    public UnityResearchFile(string path)
    {
        Data = File.ReadAllBytes(path);
        var r = new BinaryReader(new MemoryStream(Data, false));
        Big32(r); Big32(r); FormatVersion = Big32(r); Big32(r);
        byte endian = r.ReadByte(); r.ReadBytes(3);
        if (FormatVersion != 22 || endian != 0) throw new InvalidDataException("Only little-endian Unity format 22 is supported");
        Big32(r); long fileSize = Big64(r); long dataOffset = Big64(r); Big64(r);
        if (fileSize != Data.LongLength) throw new InvalidDataException("File length mismatch");
        UnityVersion = ZeroString(r); r.ReadInt32();
        if (r.ReadBoolean()) throw new InvalidDataException("Type-tree layout needs a separate reader");
        int typeCount = Count(r); var types = new List<int>();
        for (int i = 0; i < typeCount; i++)
        {
            int classId = r.ReadInt32(); types.Add(classId); r.ReadBoolean(); r.ReadInt16();
            if (classId == 114) r.ReadBytes(16);
            r.ReadBytes(16);
        }
        int objectCount = Count(r);
        for (int i = 0; i < objectCount; i++)
        {
            Align(r); long id = r.ReadInt64(); long position = r.ReadInt64() + dataOffset;
            uint size = r.ReadUInt32(); int typeIndex = r.ReadInt32();
            if (typeIndex < 0 || typeIndex >= types.Count || position < dataOffset || position + size > fileSize)
                throw new InvalidDataException("Object table outside file");
            var record = new UnityObjectRecord { Id = id, Position = position, Size = size, ClassId = types[typeIndex] };
            Objects.Add(record); objectsById.Add(id, record);
        }
        int scriptCount = Count(r);
        for (int i = 0; i < scriptCount; i++) { r.ReadInt32(); Align(r); r.ReadInt64(); }
        int externalCount = Count(r);
        for (int i = 0; i < externalCount; i++)
        {
            ZeroString(r); r.ReadBytes(16); r.ReadInt32(); ExternalFiles.Add(ZeroString(r));
        }
        r.Dispose();
    }
    public BinaryReader Reader(UnityObjectRecord o)
    {
        var r = new BinaryReader(new MemoryStream(Data, false)); r.BaseStream.Position = o.Position; return r;
    }
    public List<UnityObjectRecord> FindScripts(long[] scriptIds)
    {
        var wanted = new HashSet<long>(scriptIds); var found = new List<UnityObjectRecord>();
        using (var reader = new BinaryReader(new MemoryStream(Data, false)))
        foreach (var o in Objects)
        {
            if (o.ClassId != 114 || o.Size < 28) continue;
            reader.BaseStream.Position = o.Position + 20;
            if (wanted.Contains(reader.ReadInt64())) found.Add(o);
        }
        return found;
    }
    public UnityObjectRecord Object(long id)
    {
        UnityObjectRecord found;
        if (!objectsById.TryGetValue(id, out found)) throw new InvalidDataException("Unknown Unity object " + id);
        return found;
    }
}

public static class ResearchIL
{
    static readonly Dictionary<short, OpCode> Codes = new Dictionary<short, OpCode>();
    static ResearchIL()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        { var op = (OpCode)field.GetValue(null); Codes[op.Value] = op; }
    }
    static string Resolve(Module module, int token, bool text)
    {
        try
        {
            if (text) return "\"" + module.ResolveString(token).Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
            var member = module.ResolveMember(token);
            return (member.DeclaringType == null ? "" : member.DeclaringType.FullName + "::") + member.ToString();
        }
        catch { return "token 0x" + token.ToString("X8"); }
    }
    public static string[] Read(MethodBase method)
    {
        var body = method.GetMethodBody(); if (body == null) return new string[0];
        var r = new BinaryReader(new MemoryStream(body.GetILAsByteArray())); var lines = new List<string>();
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            int at = (int)r.BaseStream.Position; byte first = r.ReadByte();
            short key = first == 0xFE ? (short)(0xFE00 | r.ReadByte()) : first;
            OpCode op = Codes[key]; string arg = "";
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineI: arg = r.ReadSByte().ToString(); break;
                case OperandType.InlineI: arg = r.ReadInt32().ToString(); break;
                case OperandType.InlineI8: arg = r.ReadInt64().ToString(); break;
                case OperandType.ShortInlineR: arg = r.ReadSingle().ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case OperandType.InlineR: arg = r.ReadDouble().ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case OperandType.ShortInlineVar: arg = r.ReadByte().ToString(); break;
                case OperandType.InlineVar: arg = r.ReadUInt16().ToString(); break;
                case OperandType.ShortInlineBrTarget: { int delta = r.ReadSByte(); arg = "IL_" + (r.BaseStream.Position + delta).ToString("X4"); break; }
                case OperandType.InlineBrTarget: { int delta = r.ReadInt32(); arg = "IL_" + (r.BaseStream.Position + delta).ToString("X4"); break; }
                case OperandType.InlineSwitch:
                    { int n = r.ReadInt32(); var deltas = new int[n]; for (int i = 0; i < n; i++) deltas[i] = r.ReadInt32();
                      long end = r.BaseStream.Position; var targets = new List<string>(); foreach (int d in deltas) targets.Add("IL_" + (end + d).ToString("X4")); arg = string.Join(", ", targets.ToArray()); break; }
                case OperandType.InlineString: arg = Resolve(method.Module, r.ReadInt32(), true); break;
                case OperandType.InlineField: case OperandType.InlineMethod: case OperandType.InlineType: case OperandType.InlineTok:
                    arg = Resolve(method.Module, r.ReadInt32(), false); break;
                case OperandType.InlineSig: arg = "signature 0x" + r.ReadInt32().ToString("X8"); break;
                default: throw new InvalidDataException("Unsupported IL operand " + op.OperandType);
            }
            lines.Add("IL_" + at.ToString("X4") + "  " + op.Name + (arg.Length == 0 ? "" : " " + arg));
        }
        return lines.ToArray();
    }
}
