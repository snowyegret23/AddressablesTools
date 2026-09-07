using AddressablesTools.Binary;
using AddressablesTools.Classes;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace AddressablesTools.Catalog
{
    internal static class SerializedObjectDecoder
    {
        private const string INT_TYPENAME = "System.Int32";
        private const string LONG_TYPENAME = "System.Int64";
        private const string BOOL_TYPENAME = "System.Boolean";
        private const string STRING_TYPENAME = "System.String";
        private const string HASH128_TYPENAME = "UnityEngine.Hash128";
        private const string ABRO_TYPENAME = "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleRequestOptions";

        private const string INT_MATCHNAME = "mscorlib; " + INT_TYPENAME;
        private const string LONG_MATCHNAME = "mscorlib; " + LONG_TYPENAME;
        private const string BOOL_MATCHNAME = "mscorlib; " + BOOL_TYPENAME;
        private const string STRING_MATCHNAME = "mscorlib; " + STRING_TYPENAME;
        private const string HASH128_MATCHNAME = "UnityEngine.CoreModule; " + HASH128_TYPENAME;
        private const string ABRO_MATCHNAME = "Unity.ResourceManager; " + ABRO_TYPENAME;

        private const string INT_V3_MATCHNAME = INT_TYPENAME;
        private const string LONG_V3_MATCHNAME = LONG_TYPENAME;
        private const string BOOL_V3_MATCHNAME = BOOL_TYPENAME;
        private const string STRING_V3_MATCHNAME = STRING_TYPENAME;

        internal enum ObjectType
        {
            AsciiString,
            UnicodeString,
            UInt16,
            UInt32,
            Int32,
            Hash128,
            Type,
            JsonObject
        }

        internal static object DecodeV1(BinaryReader br)
        {
            ObjectType type = (ObjectType)br.ReadByte();

            switch (type)
            {
                case ObjectType.AsciiString:
                {
                    string str = ReadString4(br);
                    return str;
                }

                case ObjectType.UnicodeString:
                {
                    string str = ReadString4Unicode(br);
                    return str;
                }

                case ObjectType.UInt16:
                {
                    return br.ReadUInt16();
                }

                case ObjectType.UInt32:
                {
                    return br.ReadUInt32();
                }

                case ObjectType.Int32:
                {
                    return br.ReadInt32();
                }

                case ObjectType.Hash128:
                {
                    string str = ReadString1(br);
                    Hash128 hash = new Hash128(str);
                    return hash;
                }

                case ObjectType.Type:
                {
                    long position = br.BaseStream.Position;
                    string str = ReadString1(br);
                    if (!Guid.TryParse(str, out _))
                    {
                        br.BaseStream.Position = position;
                        goto case ObjectType.JsonObject;
                    }
                    TypeReference typeReference = new TypeReference(str);
                    return typeReference;
                }

                case ObjectType.JsonObject:
                {
                    string assemblyName = ReadString1(br);
                    string className = ReadString1(br);
                    string jsonText = ReadString4Unicode(br);

                    ClassJsonObject jsonObj = new ClassJsonObject(assemblyName, className, jsonText);
                    jsonObj.JsonTag = (byte)type;
                    string matchName = jsonObj.Type.GetMatchName(1);
                    switch (matchName)
                    {
                        case ABRO_MATCHNAME:
                        {
                            AssetBundleRequestOptions obj = new AssetBundleRequestOptions();
                            obj.Read(jsonText);
                            return new WrappedSerializedObject(jsonObj.Type, obj) { JsonTag = (byte)type };
                        }
                    }

                    // fallback to ClassJsonObject
                    return jsonObj;
                }

                default:
                {
                    throw new InvalidDataException($"Unsupported JSON object tag {(byte)type}.");
                }
            }
        }

        internal static object DecodeV2(CatalogBinaryReader reader, uint offset, int version, out SerializedType serializedType)
        {
            serializedType = null;
            if (offset == uint.MaxValue)
            {
                return null;
            }

            reader.ValidateRange(offset, 8);
            reader.BaseStream.Position = offset;
            uint typeNameOffset = reader.ReadUInt32();
            uint objectOffset = reader.ReadUInt32();

            bool isDefaultObject = objectOffset == uint.MaxValue;

            serializedType = new SerializedType();
            serializedType.Read(reader, typeNameOffset);
            string matchName = serializedType.GetMatchName(version);
            switch (matchName)
            {
                case INT_MATCHNAME:
                case INT_V3_MATCHNAME:
                {
                    if (isDefaultObject)
                    {
                        return default(int);
                    }

                    reader.BaseStream.Position = objectOffset;
                    return reader.ReadInt32();
                }

                case LONG_MATCHNAME:
                case LONG_V3_MATCHNAME:
                {
                    if (isDefaultObject)
                    {
                        return default(long);
                    }

                    reader.BaseStream.Position = objectOffset;
                    return reader.ReadInt64();
                }

                case BOOL_MATCHNAME:
                case BOOL_V3_MATCHNAME:
                {
                    if (isDefaultObject)
                    {
                        return default(bool);
                    }

                    reader.BaseStream.Position = objectOffset;
                    return reader.ReadBoolean();
                }

                case STRING_MATCHNAME:
                case STRING_V3_MATCHNAME:
                {
                    if (isDefaultObject)
                    {
                        return default(string);
                    }

                    reader.BaseStream.Position = objectOffset;
                    uint stringOffset = reader.ReadUInt32();
                    char separator = (char)reader.ReadUInt16();
                    return reader.ReadEncodedString(stringOffset, separator);
                }

                case HASH128_MATCHNAME:
                {
                    if (isDefaultObject)
                    {
                        return default(Hash128);
                    }

                    reader.BaseStream.Position = objectOffset;
                    uint v0 = reader.ReadUInt32();
                    uint v1 = reader.ReadUInt32();
                    uint v2 = reader.ReadUInt32();
                    uint v3 = reader.ReadUInt32();
                    return new Hash128(v0, v1, v2, v3);
                }

                case ABRO_MATCHNAME:
                {
                    if (isDefaultObject)
                    {
                        // loses type info, but we can't really do anything about it
                        return null;
                    }

                    var obj = reader.ReadCustom(objectOffset, () =>
                    {
                        var newobj = new AssetBundleRequestOptions();
                        newobj.Read(reader, objectOffset);
                        return newobj;
                    });

                    return new WrappedSerializedObject(serializedType, obj);
                }

                default:
                {
                    throw new NotImplementedException("Unsupported type for deserialization " + matchName);
                }
            }
        }

        internal static void EncodeV1(BinaryWriter bw, object ob)
        {
            switch (ob)
            {
                case string str:
                {
                    byte[] asciiEncoding = Encoding.ASCII.GetBytes(str);
                    string asciiText = Encoding.ASCII.GetString(asciiEncoding);
                    if (str != asciiText)
                    {
                        bw.Write((byte)ObjectType.UnicodeString);
                        WriteString4Unicode(bw, str);
                    }
                    else
                    {
                        bw.Write((byte)ObjectType.AsciiString);
                        WriteString4(bw, str);
                    }
                    break;
                }

                case ushort ush:
                {
                    bw.Write((byte)ObjectType.UInt16);
                    bw.Write(ush);
                    break;
                }

                case uint uin:
                {
                    bw.Write((byte)ObjectType.UInt32);
                    bw.Write(uin);
                    break;
                }

                case int i:
                {
                    bw.Write((byte)ObjectType.Int32);
                    bw.Write(i);
                    break;
                }

                case Hash128 hash:
                {
                    bw.Write((byte)ObjectType.Hash128);
                    WriteString1(bw, hash.Value);
                    break;
                }

                case TypeReference type:
                {
                    bw.Write((byte)ObjectType.Type);
                    WriteString1(bw, type.Clsid);
                    break;
                }

                case ClassJsonObject jsonObject:
                {
                    // fallback class, shouldn't be used but here just in case
                    // use WrappedSerializedObject if possible
                    bw.Write(jsonObject.JsonTag);
                    WriteString1(bw, jsonObject.Type.AssemblyName);
                    WriteString1(bw, jsonObject.Type.ClassName);
                    WriteString4Unicode(bw, jsonObject.JsonText);
                    break;
                }

                case WrappedSerializedObject wso:
                {
                    string matchName = wso.Type.GetMatchName(1);
                    string jsonText;
                    switch (matchName)
                    {
                        case ABRO_MATCHNAME:
                        {
                            AssetBundleRequestOptions abro = (AssetBundleRequestOptions)wso.Object;
                            jsonText = abro.WriteJson();
                            break;
                        }
                        default:
                        {
                            throw new Exception($"Serialized type {wso.Type.AssemblyName}; {wso.Type.ClassName} not supported");
                        }
                    }

                    bw.Write(wso.JsonTag);
                    WriteString1(bw, wso.Type.AssemblyName);
                    WriteString1(bw, wso.Type.ClassName);
                    WriteString4Unicode(bw, jsonText);
                    break;
                }

                default:
                {
                    throw new Exception($"Type {ob.GetType().FullName} not supported");
                }
            }
        }

        private static char GetSeparatorWithMostOccurrences(string str, char[] options)
        {
            // no unicode separators pls :)
            Span<byte> mapping = stackalloc byte[256];
            mapping.Clear();
            for (int i = 0; i < options.Length; i++)
            {
                mapping[options[i]] = (byte)(i + 1);
            }

            int[] occurrences = new int[options.Length];
            int maxOccurrenceCount = int.MinValue;
            char maxOccurrenceChar = '\0';
            foreach (char c in str)
            {
                if (c > 255)
                    continue;

                int charIdx = mapping[c];
                if (charIdx != 0)
                {
                    int newOccurrenceCount = occurrences[charIdx - 1] + 1;
                    if (newOccurrenceCount > maxOccurrenceCount)
                    {
                        maxOccurrenceCount = newOccurrenceCount;
                        maxOccurrenceChar = c;
                    }
                    occurrences[charIdx - 1] = newOccurrenceCount;
                }
            }

            string[] splits = str.Split(maxOccurrenceChar);
            int largeSplits = 0;
            foreach (string split in splits)
            {
                if (split.Length >= 5)
                {
                    largeSplits++;
                    if (largeSplits >= 2)
                    {
                        return maxOccurrenceChar;
                    }
                }
            }

            return '\0';
        }

        internal static uint EncodeV2(CatalogBinaryWriter writer, SerializedTypeAsmContainer staCont, object ob, int version, SerializedType originalType = null)
        {
            if (ob == null)
            {
                return uint.MaxValue;
            }

            uint objectOffset = uint.MaxValue;
            SerializedType serializedType;
            switch (ob)
            {
                case int i:
                {
                    Span<byte> valBytes = stackalloc byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(valBytes, i);
                    objectOffset = writer.WriteWithCache(valBytes);

                    serializedType = new SerializedType()
                    {
                        AssemblyName = staCont.StandardLibAsm,
                        ClassName = INT_TYPENAME,
                    };
                    break;
                }

                case long lon:
                {
                    Span<byte> valBytes = stackalloc byte[8];
                    BinaryPrimitives.WriteInt64LittleEndian(valBytes, lon);
                    objectOffset = writer.WriteWithCache(valBytes);

                    serializedType = new SerializedType()
                    {
                        AssemblyName = staCont.StandardLibAsm,
                        ClassName = LONG_TYPENAME,
                    };
                    break;
                }

                case bool boo:
                {
                    Span<byte> valBytes = [boo ? (byte)1 : (byte)0];
                    objectOffset = writer.WriteWithCache(valBytes);

                    serializedType = new SerializedType()
                    {
                        AssemblyName = staCont.StandardLibAsm,
                        ClassName = BOOL_TYPENAME,
                    };
                    break;
                }

                case string str:
                {
                    char dynstrSep = GetSeparatorWithMostOccurrences(str, ['/', '\\', '.', '-', '_', ',']);
                    uint stringOffset = writer.WriteEncodedString(str, dynstrSep);

                    Span<byte> bytes = stackalloc byte[8];
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, stringOffset);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], dynstrSep);
                    objectOffset = writer.WriteWithCache(bytes);

                    serializedType = new SerializedType()
                    {
                        AssemblyName = staCont.StandardLibAsm,
                        ClassName = STRING_TYPENAME,
                    };
                    break;
                }

                case Hash128 hash:
                {
                    objectOffset = hash.Write(writer);

                    serializedType = new SerializedType()
                    {
                        AssemblyName = staCont.Hash128Asm,
                        ClassName = HASH128_TYPENAME,
                    };
                    break;
                }

                case WrappedSerializedObject wso:
                {
                    string matchName = wso.Type.GetMatchName(version);
                    switch (matchName)
                    {
                        case ABRO_MATCHNAME:
                        {
                            AssetBundleRequestOptions abro = (AssetBundleRequestOptions)wso.Object;
                            objectOffset = abro.WriteBinary(writer);
                            break;
                        }
                        default:
                        {
                            throw new Exception($"Serialized type {wso.Type.AssemblyName}; {wso.Type.ClassName} not supported");
                        }
                    }

                    serializedType = wso.Type;
                    break;
                }

                default:
                {
                    throw new Exception($"Type {ob.GetType().FullName} not supported");
                }
            }

            if (ob is not WrappedSerializedObject && originalType?.ClassName == serializedType.ClassName)
                serializedType = originalType;
            Span<byte> finalBytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(finalBytes, serializedType.Write(writer));
            BinaryPrimitives.WriteUInt32LittleEndian(finalBytes[4..], objectOffset);

            return writer.WriteWithCache(finalBytes);
        }

        internal static object DecodeRawKey(BinaryReader reader, int length)
        {
            ObjectType type = (ObjectType)reader.ReadByte();
            if (type == ObjectType.AsciiString || type == ObjectType.UnicodeString || type == ObjectType.Hash128)
            {
                string value = (type == ObjectType.UnicodeString ? Encoding.Unicode : Encoding.ASCII).GetString(ReadExactBytes(reader, length - 1));
                return type == ObjectType.Hash128 ? new Hash128(value) : value;
            }
            reader.BaseStream.Position--;
            if (type >= ObjectType.UInt16 && type <= ObjectType.Int32)
                return DecodeV1(reader);
            throw new NotSupportedException($"Unsupported early-preview key tag {(byte)type}.");
        }

        internal static void EncodeRawKey(BinaryWriter writer, object value)
        {
            if (value is string str)
            {
                bool unicode = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(str)) != str;
                writer.Write((byte)(unicode ? ObjectType.UnicodeString : ObjectType.AsciiString));
                writer.Write((unicode ? Encoding.Unicode : Encoding.ASCII).GetBytes(str));
            }
            else if (value is Hash128 hash)
            {
                writer.Write((byte)ObjectType.Hash128);
                writer.Write(Encoding.ASCII.GetBytes(hash.Value));
            }
            else if (value is ushort || value is uint || value is int)
                EncodeV1(writer, value);
            else
                throw new NotSupportedException("Early-preview catalogs only support string, integer and Hash128 keys.");
        }

        private static byte[] ReadExactBytes(BinaryReader reader, int length)
        {
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Serialized string extends beyond the catalog data.");
            return reader.ReadBytes(length);
        }

        private static string ReadString1(BinaryReader br)
        {
            int length = br.ReadByte();
            string str = Encoding.ASCII.GetString(ReadExactBytes(br, length));
            return str;
        }

        private static string ReadString4(BinaryReader br)
        {
            int length = br.ReadInt32();
            string str = Encoding.ASCII.GetString(ReadExactBytes(br, length));
            return str;
        }

        private static string ReadString4Unicode(BinaryReader br)
        {
            int length = br.ReadInt32();
            if ((length & 1) != 0)
                throw new InvalidDataException("Invalid UTF-16 string length.");
            string str = Encoding.Unicode.GetString(ReadExactBytes(br, length));
            return str;
        }

        private static void WriteString1(BinaryWriter bw, string str)
        {
            if (str.Length > 255)
                throw new ArgumentException("String length cannot be greater than 255");

            byte[] bytes = Encoding.ASCII.GetBytes(str);
            bw.Write((byte)bytes.Length);
            bw.Write(bytes);
        }

        private static void WriteString4(BinaryWriter bw, string str)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(str);
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }

        private static void WriteString4Unicode(BinaryWriter bw, string str)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(str);
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }
    }
}
