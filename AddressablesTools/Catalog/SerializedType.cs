using AddressablesTools.Binary;
using AddressablesTools.JSON;
using System;
using System.Buffers.Binary;
using System.IO;

namespace AddressablesTools.Catalog
{
    public class SerializedType
    {
        public string AssemblyName { get; set; }
        public string ClassName { get; set; }

        public override bool Equals(object obj)
        {
            return obj is SerializedType type &&
                   AssemblyName == type.AssemblyName &&
                   ClassName == type.ClassName;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(AssemblyName, ClassName);
        }

        internal void Read(SerializedTypeJson type)
        {
            AssemblyName = type.m_AssemblyName;
            ClassName = type.m_ClassName;
        }

        internal void Read(CatalogBinaryReader reader, uint offset)
        {
            reader.BaseStream.Position = offset;

            uint assemblyNameOffset = reader.ReadUInt32();
            uint classNameOffset = reader.ReadUInt32();

            AssemblyName = reader.ReadEncodedString(assemblyNameOffset, '.');
            ClassName = reader.ReadEncodedString(classNameOffset, '.');
        }

        internal void Write(SerializedTypeJson type)
        {
            type.m_AssemblyName = AssemblyName;
            type.m_ClassName = ClassName;
        }

        internal uint Write(CatalogBinaryWriter writer)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, writer.WriteEncodedString(AssemblyName, '.'));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], writer.WriteEncodedString(ClassName, '.'));
            return writer.WriteWithCache(bytes);
        }

        internal string GetMatchName(int version)
        {
            if (version <= 2)
            {
                // always has assembly name
                return GetAssemblyShortName(version) + "; " + ClassName;
            }
            else // if (version >= 3)
            {
                // may or may not have assembly name
                if (AssemblyName is null)
                {
                    return ClassName;
                }
                else
                {
                    return GetAssemblyShortName(version) + "; " + ClassName;
                }
            }
        }

        internal string GetAssemblyShortName(int version)
        {
            if (version <= 2)
            {
                if (!AssemblyName.Contains(','))
                {
                    throw new InvalidDataException("Assembly name must have commas");
                }

                // strip assembly version info
                return AssemblyName.Split(',')[0];
            }
            else // if (version >= 3)
            {
                // nothing to strip since version info is not provided
                return AssemblyName;
            }
        }
    }
}
