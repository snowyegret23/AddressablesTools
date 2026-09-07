using AddressablesTools.Binary;
using AddressablesTools.Catalog;
using AddressablesTools.JSON;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;

namespace AddressablesTools
{
    public static class AddressablesCatalogFileParser
    {
        internal static ContentCatalogDataJson CCDJsonFromString(string data)
        {
            return JsonSerializer.Deserialize<ContentCatalogDataJson>(data.TrimStart('\uFEFF'), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public static ContentCatalogData FromBinaryData(byte[] data)
        {
            return FromBinaryData(data, null);
        }

        public static ContentCatalogData FromBinaryData(byte[] data, bool? reverseDynamicStrings)
        {
            using MemoryStream ms = new MemoryStream(data);
            using CatalogBinaryReader reader = new CatalogBinaryReader(ms);

            ContentCatalogData catalogData = new ContentCatalogData { BinaryReverseDynamicStrings = reverseDynamicStrings };
            catalogData.Read(reader);

            return catalogData;
        }

        public static ContentCatalogData FromJsonString(string data)
        {
            JsonObject source = JsonNode.Parse(data.TrimStart('\uFEFF')).AsObject();
            if (source["locations"] is JsonArray)
            {
                ContentCatalogData legacyCatalog = new ContentCatalogData();
                legacyCatalog.ReadLegacyJson(source);
                return legacyCatalog;
            }
            ContentCatalogDataJson ccdJson = CCDJsonFromString(data);

            ContentCatalogData catalogData = new ContentCatalogData();
            catalogData.Read(ccdJson);
            catalogData.JsonTemplate = source;

            return catalogData;
        }

        public static CatalogFileType GetCatalogFileType(Stream stream)
        {
            if (!stream.CanSeek)
                throw new ArgumentException("Catalog detection requires a seekable stream.", nameof(stream));
            long start = stream.Position;
            try
            {
                Span<byte> data = stackalloc byte[4];
                int count = stream.ReadAtLeast(data, 4, false);
                if (count == 4)
                {
                    int magic = BinaryPrimitives.ReadInt32LittleEndian(data);
                    if (magic == 0x0de38942 || magic == 0x4289e30d)
                        return CatalogFileType.Binary;
                }
                stream.Position = start;
                if (count >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                    stream.Position += 3;
                while (true)
                {
                    int v = stream.ReadByte();
                    if (v == '\t' || v == ' ' || v == '\r' || v == '\n')
                        continue;
                    return v == '{' ? CatalogFileType.Json : CatalogFileType.None;
                }
            }
            finally
            {
                stream.Position = start;
            }
        }

        internal static byte[] GetBundleTextAssetData(AssetsManager manager, BundleFileInstance bundleInst)
        {
            return FindBundleCatalog(manager, bundleInst).Data;
        }

        private static (AssetsFile File, AssetFileInfo Info, int FileIndex, string Name, byte[] Data) FindBundleCatalog(AssetsManager manager, BundleFileInstance bundleInst)
        {
            (AssetsFile File, AssetFileInfo Info, int FileIndex, string Name, byte[] Data) result = default;
            var directories = bundleInst.file.BlockAndDirInfo.DirectoryInfos;
            for (int i = 0; i < directories.Count; i++)
            {
                if (!directories[i].IsSerialized)
                    continue;
                AssetsFile file = manager.LoadAssetsFileFromBundle(bundleInst, i).file;
                foreach (AssetFileInfo info in file.GetAssetsOfType(AssetClassID.TextAsset))
                {
                    AssetsFileReader reader = file.Reader;
                    reader.Position = info.GetAbsoluteByteOffset(file);
                    string name = reader.ReadCountStringInt32();
                    reader.Align();
                    int size = reader.ReadInt32();
                    long end = info.GetAbsoluteByteOffset(file) + info.ByteSize;
                    if (size < 0 || size > end - reader.Position)
                        throw new InvalidDataException("Invalid TextAsset data length.");
                    byte[] bytes = reader.ReadBytes(size);
                    using MemoryStream data = new MemoryStream(bytes);
                    CatalogFileType type = GetCatalogFileType(data);
                    if (type == CatalogFileType.None)
                        continue;
                    if (type == CatalogFileType.Json)
                    {
                        try
                        {
                            using JsonDocument json = JsonDocument.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                            if (!json.RootElement.EnumerateObject().Any(property => string.Equals(property.Name, "m_BucketDataString", StringComparison.OrdinalIgnoreCase) || property.Name == "locations"))
                                continue;
                        }
                        catch (JsonException) { continue; }
                    }
                    if (result.Info != null)
                        throw new InvalidDataException("Bundle contains multiple catalogs; select a catalog before writing.");
                    result = (file, info, i, name, bytes);
                }
            }
            if (result.Info == null)
                throw new InvalidDataException("Bundle does not contain an Addressables catalog TextAsset.");
            return result;
        }

        internal static ContentCatalogData FromBundle(AssetsManager manager, BundleFileInstance bundleInst)
        {
            byte[] data = GetBundleTextAssetData(manager, bundleInst);
            if (data.Length < 4)
            {
                throw new InvalidDataException("Catalog data too small");
            }

            int possibleMagic;
            possibleMagic = BinaryPrimitives.ReadInt32LittleEndian(data);
            if (possibleMagic == 0x0de38942)
            {
                return FromBinaryData(data);
            }
            else if (possibleMagic == 0x4289e30d)
            {
                // different hash code on big endian maybe?
                throw new NotSupportedException("Big endian catalogs are not supported");
            }
            else
            {
                return FromJsonString(Encoding.UTF8.GetString(data));
            }
        }

        public static ContentCatalogData FromBundle(Stream stream)
        {
            AssetsManager manager = new AssetsManager();
            // name doesn't matter since we don't have dependencies
            try
            {
                BundleFileInstance bundleInst = manager.LoadBundleFile(stream, "catalog.bundle");
                return FromBundle(manager, bundleInst);
            }
            finally { manager.UnloadAll(); }
        }

        public static ContentCatalogData FromBundle(string path)
        {
            AssetsManager manager = new AssetsManager();
            try
            {
                BundleFileInstance bundleInst = manager.LoadBundleFile(path);
                return FromBundle(manager, bundleInst);
            }
            finally { manager.UnloadAll(); }
        }

        public static byte[] ToBinaryData(ContentCatalogData ccd)
        {
            if (ccd.JsonEntrySize == 0)
                throw new NotSupportedException("Legacy ResourceLocationList must be saved as JSON.");
            using MemoryStream ms = new MemoryStream();
            using CatalogBinaryWriter writer = new CatalogBinaryWriter(ms);

            var assemblies = SerializedTypeAsmContainer.ForNet40();
            if (ccd.Version >= 3)
            {
                assemblies.StandardLibAsm = null;
                assemblies.Hash128Asm = "UnityEngine.CoreModule";
                assemblies.AbroAsm = "Unity.ResourceManager";
            }
            ccd.Write(writer, assemblies);

            return ms.ToArray();
        }

        public static string ToJsonString(ContentCatalogData ccd)
        {
            if (ccd.JsonEntrySize == 0)
                return ccd.WriteLegacyJson().ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            ContentCatalogDataJson ccdJson = new ContentCatalogDataJson();

            ccd.Write(ccdJson);

            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            if (ccd.JsonTemplate == null)
                return JsonSerializer.Serialize(ccdJson, options);

            JsonObject result = ccd.JsonTemplate.DeepClone().AsObject();
            JsonObject updated = JsonSerializer.SerializeToNode(ccdJson, options).AsObject();
            string[] knownNames = typeof(ContentCatalogDataJson).GetProperties().Select(property => property.Name).ToArray();
            foreach (string name in result.Select(pair => pair.Key).ToArray())
            {
                string match = knownNames.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    result[name] = updated[match]?.DeepClone();
            }
            return result.ToJsonString(options);
        }

        internal static void ToBundle(ContentCatalogData ccd, AssetsManager manager, BundleFileInstance bundleInst, Stream stream)
        {
            var catalog = FindBundleCatalog(manager, bundleInst);
            using MemoryStream originalStream = new MemoryStream(catalog.Data);
            CatalogFileType type = GetCatalogFileType(originalStream);
            byte[] catalogBytes = type switch
            {
                CatalogFileType.Binary => ToBinaryData(ccd),
                CatalogFileType.Json => Encoding.UTF8.GetBytes(ToJsonString(ccd)),
                _ => throw new InvalidDataException("Bundle TextAsset is not an Addressables catalog.")
            };

            using MemoryStream newTextAssetMem = new MemoryStream();
            using AssetsFileWriter newTextAssetWriter = new AssetsFileWriter(newTextAssetMem);
            newTextAssetWriter.BigEndian = catalog.File.Header.Endianness;
            newTextAssetWriter.WriteCountStringInt32(catalog.Name);
            newTextAssetWriter.Align();
            newTextAssetWriter.Write(catalogBytes.Length);
            newTextAssetWriter.Write(catalogBytes);
            newTextAssetWriter.Align();

            catalog.Info.SetNewData(newTextAssetMem.ToArray());

            bundleInst.file.BlockAndDirInfo.DirectoryInfos[catalog.FileIndex].SetNewData(catalog.File);

            stream.Position = 0;
            stream.SetLength(0);
            AssetsFileWriter bundleWriter = new AssetsFileWriter(stream);
            bundleInst.file.Write(bundleWriter);

        }

        public static void ToBundle(ContentCatalogData ccd, Stream inStream, Stream outStream)
        {
            if (ReferenceEquals(inStream, outStream))
                throw new ArgumentException("Input and output streams must be different.");
            AssetsManager manager = new AssetsManager();
            try
            {
                BundleFileInstance bundleInst = manager.LoadBundleFile(inStream, "catalog.bundle");
                ToBundle(ccd, manager, bundleInst, outStream);
            }
            finally { manager.UnloadAll(); }
        }

        public static void ToBundle(ContentCatalogData ccd, string inPath, string outPath)
        {
            if (string.Equals(Path.GetFullPath(inPath), Path.GetFullPath(outPath), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Input and output bundle paths must be different.");
            AssetsManager manager = new AssetsManager();
            try
            {
                BundleFileInstance bundleInst = manager.LoadBundleFile(inPath);
                using FileStream fs = File.Create(outPath);
                ToBundle(ccd, manager, bundleInst, fs);
            }
            finally { manager.UnloadAll(); }
        }
    }
}
