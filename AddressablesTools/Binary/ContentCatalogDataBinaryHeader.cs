using System;

using System.IO;

namespace AddressablesTools.Binary
{
    internal class ContentCatalogDataBinaryHeader
    {
        public int Magic { get; set; }
        public int Version { get; set; }
        public uint KeysOffset { get; set; }
        public uint IdOffset { get; set; }
        public uint InstanceProviderOffset { get; set; }
        public uint SceneProviderOffset { get; set; }
        public uint InitObjectsArrayOffset { get; set; }
        public uint BuildResultHashOffset { get; set; }
        public bool HasBuildResultHash { get; set; } = true;

        internal void Read(CatalogBinaryReader reader)
        {
            Magic = reader.ReadInt32();
            if (Magic == 0x4289e30d)
                throw new NotSupportedException("Big-endian catalogs are not supported.");
            if (Magic != 0x0de38942)
                throw new InvalidDataException("Invalid catalog magic.");
            Version = reader.ReadInt32();
            if (Version < 1 || Version > 3)
            {
                throw new NotSupportedException("Only versions 1-3 are supported");
            }
            reader.Version = Version;

            KeysOffset = reader.ReadUInt32();
            IdOffset = reader.ReadUInt32();
            InstanceProviderOffset = reader.ReadUInt32();
            SceneProviderOffset = reader.ReadUInt32();
            InitObjectsArrayOffset = reader.ReadUInt32();

            // Unity reserves the header immediately before the key-array length.
            // Addressables 1.21.3-1.21.19 and 2.0.3-2.0.6 have a 28-byte header.
            HasBuildResultHash = Version > 2 || KeysOffset != 0x20;
            if (!HasBuildResultHash)
                BuildResultHashOffset = uint.MaxValue;
            else
                BuildResultHashOffset = reader.ReadUInt32();
        }

        internal void Write(CatalogBinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(KeysOffset);
            writer.Write(IdOffset);
            writer.Write(InstanceProviderOffset);
            writer.Write(SceneProviderOffset);
            writer.Write(InitObjectsArrayOffset);
            if (HasBuildResultHash)
                writer.Write(BuildResultHashOffset);
        }
    }
}
