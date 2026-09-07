using AddressablesTools.Binary;
using AddressablesTools.JSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Reflection;

namespace AddressablesTools.Catalog
{
    public class ContentCatalogData
    {
        // only used for binary format
        public int Version { get; set; } = 2;
        public bool BinaryHasBuildResultHash { get; set; } = true;
        public bool? BinaryReverseDynamicStrings { get; set; }

        public string LocatorId { get; set; }
        public string BuildResultHash { get; set; }
        public ObjectInitializationData InstanceProviderData { get; set; }
        public ObjectInitializationData SceneProviderData { get; set; }
        public ObjectInitializationData[] ResourceProviderData { get; set; } = [];
        public bool WriteCompact { get; set; } // use prefixes when writing?
        public int JsonEntrySize { get; private set; } = 7;
        internal JsonObject JsonTemplate { get; set; }
        private List<ResourceLocation> _legacyLocations;
        private Dictionary<object, List<ResourceLocation>> _legacyResources;
        private readonly Dictionary<object, SerializedType> _binaryKeyTypes = [];

        // used for resources for the json format, shouldn't be edited directly
        private string[] ProviderIds { get; set; }
        private string[] InternalIds { get; set; }
        private string[] Keys { get; set; } // for old versions
        private SerializedType[] ResourceTypes { get; set; }
        private string[] InternalIdPrefixes { get; set; }

        public Dictionary<object, List<ResourceLocation>> Resources { get; set; }

        internal void ReadLegacyJson(JsonObject data)
        {
            JsonTemplate = data;
            JsonEntrySize = 0;
            _legacyLocations = [];
            Resources = [];
            var addresses = new Dictionary<string, ResourceLocation>();
            JsonArray entries = data["locations"].AsArray();
            JsonArray labels = data["labels"]?.AsArray() ?? [];
            foreach (JsonNode entry in entries)
            {
                string address = (string)entry["m_address"];
                var location = new ResourceLocation
                {
                    PrimaryKey = address,
                    InternalId = (string)(entry["m_internalId"] ?? entry["m_id"]),
                    ProviderId = (string)entry["m_provider"],
                    Dependencies = []
                };
                if (address == null || location.InternalId == null || location.ProviderId == null || !addresses.TryAdd(address, location))
                    throw new InvalidDataException("Invalid or duplicate legacy resource location.");
                _legacyLocations.Add(location);
                if ((bool)(entry["m_isLoadable"] ?? false))
                {
                    AddLegacyKey(address, location);
                    string guid = (string)entry["m_guid"];
                    if (!string.IsNullOrEmpty(guid))
                        AddLegacyKey(guid, location);
                    long mask = (long)(entry["m_labelMask"] ?? 0L);
                    for (int i = 0; i < labels.Count; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                            AddLegacyKey((string)labels[i], location);
                    }
                }
            }
            for (int i = 0; i < entries.Count; i++)
            {
                foreach (JsonNode dependency in entries[i]["m_dependencies"]?.AsArray() ?? [])
                {
                    if (!addresses.TryGetValue((string)dependency, out ResourceLocation location))
                        throw new InvalidDataException("Legacy resource dependency does not exist.");
                    _legacyLocations[i].Dependencies.Add(location);
                }
            }
            _legacyResources = Resources.ToDictionary(pair => pair.Key, pair => new List<ResourceLocation>(pair.Value));
        }

        private void AddLegacyKey(string key, ResourceLocation location)
        {
            if (!Resources.TryGetValue(key, out List<ResourceLocation> locations))
                Resources.Add(key, locations = []);
            locations.Add(location);
        }

        internal JsonObject WriteLegacyJson()
        {
            if (Resources.Count != _legacyResources.Count || _legacyResources.Any(pair => !Resources.TryGetValue(pair.Key, out var locations) || !locations.SequenceEqual(pair.Value)))
                throw new NotSupportedException("Legacy ResourceLocationList supports editing existing locations, not replacing its address/GUID/label index.");
            JsonObject result = JsonTemplate.DeepClone().AsObject();
            JsonArray entries = result["locations"].AsArray();
            HashSet<string> addresses = [];
            foreach (ResourceLocation location in _legacyLocations)
            {
                if (location.PrimaryKey == null || !addresses.Add(location.PrimaryKey) || location.InternalId == null || location.ProviderId == null)
                    throw new InvalidDataException("Invalid or duplicate legacy resource location.");
                if (location.Data != null || location.Type != null || location.DependencyKey != null)
                    throw new NotSupportedException("Legacy ResourceLocationList cannot store compact-catalog data or type fields.");
            }
            for (int i = 0; i < entries.Count; i++)
            {
                ResourceLocation location = _legacyLocations[i];
                JsonObject entry = entries[i].AsObject();
                entry["m_address"] = location.PrimaryKey;
                entry[entry.ContainsKey("m_internalId") ? "m_internalId" : "m_id"] = location.InternalId;
                entry["m_provider"] = location.ProviderId;
                if (location.Dependencies?.Any(dependency => !_legacyLocations.Contains(dependency)) == true)
                    throw new NotSupportedException("Legacy dependency must reference an existing location.");
                if (location.Dependencies?.Count > 0 || entry["m_dependencies"] != null)
                    entry["m_dependencies"] = new JsonArray((location.Dependencies ?? []).Select(dependency => (JsonNode)JsonValue.Create(dependency.PrimaryKey)).ToArray());
            }
            return result;
        }

        internal void Read(ContentCatalogDataJson data)
        {
            LocatorId = data.m_LocatorId;
            BuildResultHash = data.m_BuildResultHash;

            if (data.m_InstanceProviderData != null)
            {
                InstanceProviderData = new ObjectInitializationData();
                InstanceProviderData.Read(data.m_InstanceProviderData);
            }

            if (data.m_SceneProviderData != null)
            {
                SceneProviderData = new ObjectInitializationData();
                SceneProviderData.Read(data.m_SceneProviderData);
            }

            ResourceProviderData = new ObjectInitializationData[data.m_ResourceProviderData?.Length ?? 0];
            for (int i = 0; i < ResourceProviderData.Length; i++)
            {
                ResourceProviderData[i] = new ObjectInitializationData();
                ResourceProviderData[i].Read(data.m_ResourceProviderData[i]);
            }

            if (data.m_ProviderIds == null || data.m_InternalIds == null || data.m_EntryDataString == null)
                throw new InvalidDataException("Not a compact Addressables content catalog.");
            ProviderIds = new string[data.m_ProviderIds.Length];
            for (int i = 0; i < ProviderIds.Length; i++)
            {
                ProviderIds[i] = data.m_ProviderIds[i];
            }

            InternalIds = new string[data.m_InternalIds.Length];
            for (int i = 0; i < InternalIds.Length; i++)
            {
                InternalIds[i] = data.m_InternalIds[i];
            }

            if (data.m_Keys != null)
            {
                Keys = new string[data.m_Keys.Length];
                for (int i = 0; i < Keys.Length; i++)
                {
                    Keys[i] = data.m_Keys[i];
                }
            }
            else
            {
                Keys = null;
            }

            ResourceTypes = new SerializedType[data.m_resourceTypes?.Length ?? 0];
            for (int i = 0; i < ResourceTypes.Length; i++)
            {
                ResourceTypes[i] = new SerializedType();
                ResourceTypes[i].Read(data.m_resourceTypes[i]);
            }

            if (data.m_InternalIdPrefixes != null)
            {
                InternalIdPrefixes = new string[data.m_InternalIdPrefixes.Length];
                for (int i = 0; i < InternalIdPrefixes.Length; i++)
                {
                    InternalIdPrefixes[i] = data.m_InternalIdPrefixes[i];
                }

                WriteCompact = InternalIdPrefixes.Length > 0;
            }
            else
            {
                InternalIdPrefixes = null;
                WriteCompact = false;
            }

            ReadResources(data);
        }

        internal void Read(CatalogBinaryReader reader)
        {
            ContentCatalogDataBinaryHeader header = new ContentCatalogDataBinaryHeader();
            header.Read(reader);

            Version = reader.Version;
            BinaryHasBuildResultHash = header.HasBuildResultHash;
            reader.ReverseDynamicStrings = BinaryReverseDynamicStrings ?? (Version > 1 ? true : DetectV1StringOrder(reader, header));
            BinaryReverseDynamicStrings = reader.ReverseDynamicStrings;

            LocatorId = reader.ReadEncodedString(header.IdOffset);
            BuildResultHash = reader.ReadEncodedString(header.BuildResultHashOffset);

            if (header.InstanceProviderOffset != uint.MaxValue)
            {
                InstanceProviderData = new ObjectInitializationData();
                InstanceProviderData.Read(reader, header.InstanceProviderOffset);
            }

            if (header.SceneProviderOffset != uint.MaxValue)
            {
                SceneProviderData = new ObjectInitializationData();
                SceneProviderData.Read(reader, header.SceneProviderOffset);
            }

            uint[] resourceProviderDataOffsets = reader.ReadOffsetArray(header.InitObjectsArrayOffset);
            ResourceProviderData = new ObjectInitializationData[resourceProviderDataOffsets.Length];
            for (int i = 0; i < ResourceProviderData.Length; i++)
            {
                ResourceProviderData[i] = new ObjectInitializationData();
                ResourceProviderData[i].Read(reader, resourceProviderDataOffsets[i]);
            }

            ReadResources(reader, header);
        }

        private static bool? DetectV1StringOrder(CatalogBinaryReader reader, ContentCatalogDataBinaryHeader header)
        {
            uint[] keys = reader.ReadOffsetArray(header.KeysOffset);
            if ((keys.Length & 1) != 0)
                throw new InvalidDataException("Catalog key table must contain key/location pairs.");
            HashSet<uint> typeOffsets = [];
            for (int i = 0; i < keys.Length; i += 2)
            {
                reader.ValidateRange(keys[i], 8);
                reader.BaseStream.Position = keys[i];
                typeOffsets.Add(reader.ReadUInt32());
            }
            foreach (uint provider in reader.ReadOffsetArray(header.InitObjectsArrayOffset).Concat([header.InstanceProviderOffset, header.SceneProviderOffset]))
            {
                if (provider == uint.MaxValue)
                    continue;
                reader.ValidateRange(provider, 12);
                reader.BaseStream.Position = provider + 4;
                typeOffsets.Add(reader.ReadUInt32());
            }
            bool? result = null;
            foreach (uint typeOffset in typeOffsets)
            {
                if (typeOffset == uint.MaxValue)
                    continue;
                reader.ReverseDynamicStrings = false;
                var forward = new SerializedType();
                forward.Read(reader, typeOffset);
                reader.ReverseDynamicStrings = true;
                var reverse = new SerializedType();
                reverse.Read(reader, typeOffset);
                if (forward.Equals(reverse))
                    continue;
                bool forwardValid = IsValidAssemblyIdentity(forward.AssemblyName);
                bool reverseValid = IsValidAssemblyIdentity(reverse.AssemblyName);
                if (forwardValid == reverseValid)
                {
                    forwardValid = IsKnownTypeName(forward.ClassName);
                    reverseValid = IsKnownTypeName(reverse.ClassName);
                }
                if (forwardValid == reverseValid)
                    continue;
                if (result != null && result != reverseValid)
                    throw new InvalidDataException("Conflicting v1 dynamic-string orders in catalog type identities.");
                result = reverseValid;
            }
            return result;
        }

        private static bool IsValidAssemblyIdentity(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            try { return new AssemblyName(name).Name != null; }
            catch (ArgumentException) { return false; }
            catch (FileLoadException) { return false; }
        }

        private static bool IsKnownTypeName(string name)
        {
            return name != null && (name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("UnityEngine.", StringComparison.Ordinal));
        }

        private void ReadResources(ContentCatalogDataJson data)
        {
            byte[] entryBytes = Convert.FromBase64String(data.m_EntryDataString);
            if (entryBytes.Length < 4)
                throw new InvalidDataException("Truncated catalog entry table.");
            int locationCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(entryBytes);
            if (locationCount < 0 || (locationCount == 0 ? entryBytes.Length != 4 : (entryBytes.Length - 4L) % (locationCount * 4L) != 0))
                throw new InvalidDataException("Invalid catalog entry table length.");
            JsonEntrySize = locationCount == 0 ? (data.m_resourceTypes != null ? 7 : 4) : (entryBytes.Length - 4) / locationCount / 4;
            if (JsonEntrySize != 3 && JsonEntrySize != 4 && JsonEntrySize != 5 && JsonEntrySize != 7)
                throw new NotSupportedException($"Unsupported JSON catalog entry size: {JsonEntrySize} integers.");
            List<Bucket> buckets;

            MemoryStream bucketStream = new MemoryStream(Convert.FromBase64String(data.m_BucketDataString));
            using (BinaryReader bucketReader = new BinaryReader(bucketStream))
            {
                int bucketCount = bucketReader.ReadInt32();
                if (bucketCount < 0 || bucketCount > (bucketStream.Length - 4) / 8)
                    throw new InvalidDataException("Invalid catalog bucket count.");
                buckets = new List<Bucket>(bucketCount);

                for (int i = 0; i < bucketCount; i++)
                {
                    int offset = bucketReader.ReadInt32();

                    int entryCount = bucketReader.ReadInt32();
                    if (entryCount < 0 || entryCount > (bucketStream.Length - bucketStream.Position) / 4)
                        throw new InvalidDataException("Invalid catalog bucket entry count.");
                    int[] entries = new int[entryCount];
                    for (int j = 0; j < entryCount; j++)
                    {
                        entries[j] = bucketReader.ReadInt32();
                    }

                    buckets.Add(new Bucket(offset, entries));
                }
            }

            List<object> keys;

            MemoryStream keyDataStream = new MemoryStream(Convert.FromBase64String(data.m_KeyDataString));
            using (BinaryReader keyReader = new BinaryReader(keyDataStream))
            {
                int keyCount = keyReader.ReadInt32();
                if (keyCount != buckets.Count)
                    throw new InvalidDataException("Catalog key and bucket counts differ.");
                keys = new List<object>(keyCount);

                for (int i = 0; i < keyCount; i++)
                {
                    int start = buckets[i].offset;
                    int end = i + 1 < keyCount ? buckets[i + 1].offset : checked((int)keyDataStream.Length);
                    if (start < 4 || end <= start || end > keyDataStream.Length)
                        throw new InvalidDataException("Invalid catalog key range.");
                    keyDataStream.Position = start;
                    keys.Add(JsonEntrySize == 3
                        ? SerializedObjectDecoder.DecodeRawKey(keyReader, end - start)
                        : SerializedObjectDecoder.DecodeV1(keyReader));
                    if (keyDataStream.Position != end)
                        throw new InvalidDataException("Catalog key does not match its bucket range.");
                }
            }

            List<ResourceLocation> locations;

            MemoryStream entryDataStream = new MemoryStream(entryBytes);
            MemoryStream extraDataStream = new MemoryStream(Convert.FromBase64String(data.m_ExtraDataString ?? ""));
            using (BinaryReader entryReader = new BinaryReader(entryDataStream))
            using (BinaryReader extraReader = new BinaryReader(extraDataStream))
            {
                int entryCount = entryReader.ReadInt32();
                locations = new List<ResourceLocation>(entryCount);

                for (int i = 0; i < entryCount; i++)
                {
                    int internalIdIndex = entryReader.ReadInt32();
                    int providerIndex = entryReader.ReadInt32();
                    int dependencyKeyIndex = entryReader.ReadInt32();
                    int depHash = JsonEntrySize >= 5 ? entryReader.ReadInt32() : 0;
                    int dataIndex = JsonEntrySize >= 4 ? entryReader.ReadInt32() : -1;
                    int primaryKeyIndex = JsonEntrySize == 7 ? entryReader.ReadInt32() : -1;
                    int resourceTypeIndex = JsonEntrySize == 7 ? entryReader.ReadInt32() : -1;

                    string internalId = InternalIds[internalIdIndex];
                    if (InternalIdPrefixes != null && InternalIdPrefixes.Length > 0)
                    {
                        // the real code uses LastIndexOf, but this completely breaks if
                        // a string has a # in it already (e.g., #12/some/path/thathas#init.prefab)
                        // this does not technically meet the reference behavior,
                        // but as this is the _expected_ behavior, we'll use that instead.
                        int splitIndex = internalId.IndexOf('#');
                        if (splitIndex != -1 && int.TryParse(internalId[..splitIndex], out int prefixIndex))
                        {
                            internalId = string.Concat(InternalIdPrefixes[prefixIndex], internalId.AsSpan(splitIndex + 1));
                        }
                    }

                    string providerId = ProviderIds[providerIndex];

                    object dependencyKey = null;
                    if (dependencyKeyIndex >= 0)
                    {
                        dependencyKey = keys[dependencyKeyIndex];
                    }

                    object objData = null;
                    if (dataIndex >= 0)
                    {
                        if (dataIndex >= extraDataStream.Length)
                            throw new InvalidDataException("Catalog extra-data offset is outside the table.");
                        extraDataStream.Position = dataIndex;
                        objData = SerializedObjectDecoder.DecodeV1(extraReader);
                    }

                    object primaryKey;
                    if (JsonEntrySize != 7)
                    {
                        primaryKey = internalId;
                    }
                    else if (Keys == null)
                    {
                        primaryKey = keys[primaryKeyIndex];
                    }
                    else
                    {
                        // unity moment
                        primaryKey = Keys[primaryKeyIndex];
                    }

                    SerializedType resourceType = JsonEntrySize == 7 ? ResourceTypes[resourceTypeIndex] : null;

                    var loc = new ResourceLocation();
                    loc.Read(internalId, providerId, dependencyKey, objData, depHash, primaryKey, resourceType);
                    locations.Add(loc);
                }
            }

            Resources = new Dictionary<object, List<ResourceLocation>>(buckets.Count);
            for (int i = 0; i < buckets.Count; i++)
            {
                int[] bucketEntries = buckets[i].entries;
                List<ResourceLocation> locs = new List<ResourceLocation>(bucketEntries.Length);
                for (int j = 0; j < bucketEntries.Length; j++)
                {
                    locs.Add(locations[bucketEntries[j]]);
                }
                Resources[keys[i]] = locs;
            }
            foreach (var location in locations)
                location.Dependencies = location.DependencyKey == null ? [] : Resources[location.DependencyKey];
        }

        private void ReadResources(CatalogBinaryReader reader, ContentCatalogDataBinaryHeader header)
        {
            uint[] keyLocationOffsets = reader.ReadOffsetArray(header.KeysOffset);
            if ((keyLocationOffsets.Length & 1) != 0)
                throw new InvalidDataException("Catalog key table must contain key/location pairs.");
            Resources = new Dictionary<object, List<ResourceLocation>>(keyLocationOffsets.Length / 2);
            for (int i = 0; i < keyLocationOffsets.Length; i += 2)
            {
                uint keyOffset = keyLocationOffsets[i];
                uint locationListOffset = keyLocationOffsets[i + 1];
                object key = SerializedObjectDecoder.DecodeV2(reader, keyOffset, Version, out SerializedType keyType);
                _binaryKeyTypes.Add(key, keyType);

                uint[] locationOffsets = reader.ReadOffsetArray(locationListOffset);
                List<ResourceLocation> locations = new List<ResourceLocation>(locationOffsets.Length);
                for (int j = 0; j < locationOffsets.Length; j++)
                {
                    ResourceLocation location = ResourceLocation.ReadReference(reader, locationOffsets[j], Version);
                    locations.Add(location);
                }

                Resources[key] = locations;
            }
        }

        internal void Write(ContentCatalogDataJson data)
        {
            data.m_LocatorId = LocatorId;
            data.m_BuildResultHash = BuildResultHash;

            if (InstanceProviderData != null)
            {
                data.m_InstanceProviderData = new ObjectInitializationDataJson();
                InstanceProviderData.Write(data.m_InstanceProviderData);
            }

            if (SceneProviderData != null)
            {
                data.m_SceneProviderData = new ObjectInitializationDataJson();
                SceneProviderData.Write(data.m_SceneProviderData);
            }

            data.m_ResourceProviderData = new ObjectInitializationDataJson[ResourceProviderData.Length];
            for (int i = 0; i < data.m_ResourceProviderData.Length; i++)
            {
                data.m_ResourceProviderData[i] = new ObjectInitializationDataJson();
                ResourceProviderData[i].Write(data.m_ResourceProviderData[i]);
            }

            WriteResources(data);

            data.m_ProviderIds = new string[ProviderIds.Length];
            for (int i = 0; i < data.m_ProviderIds.Length; i++)
            {
                data.m_ProviderIds[i] = ProviderIds[i];
            }

            data.m_InternalIds = new string[InternalIds.Length];
            if (InternalIdPrefixes != null && InternalIdPrefixes.Length > 0 && WriteCompact)
            {
                Dictionary<string, int> newPrefixesToIndex = MakeDictionaryList(InternalIdPrefixes.ToList());
                for (int i = 0; i < data.m_InternalIds.Length; i++)
                {
                    string internalId = InternalIds[i];
                    int splitIndex = internalId.LastIndexOf('/');
                    // skip if # in string since this seems broken in addressables' implementation
                    if (splitIndex != -1 && !internalId.Contains('#'))
                    {
                        int prefixIndex = newPrefixesToIndex[internalId[..splitIndex]];
                        data.m_InternalIds[i] = $"{prefixIndex}#{internalId[splitIndex..]}";
                    }
                    else
                    {
                        data.m_InternalIds[i] = InternalIds[i];
                    }
                }
            }
            else
            {
                for (int i = 0; i < data.m_InternalIds.Length; i++)
                {
                    data.m_InternalIds[i] = InternalIds[i];
                }
            }

            if (Keys != null)
            {
                data.m_Keys = new string[Keys.Length];
                for (int i = 0; i < data.m_Keys.Length; i++)
                {
                    data.m_Keys[i] = Keys[i];
                }
            }
            else
            {
                data.m_Keys = null;
            }

            data.m_resourceTypes = new SerializedTypeJson[ResourceTypes.Length];
            for (int i = 0; i < data.m_resourceTypes.Length; i++)
            {
                data.m_resourceTypes[i] = new SerializedTypeJson();
                ResourceTypes[i].Write(data.m_resourceTypes[i]);
            }

            if (InternalIdPrefixes != null)
            {
                data.m_InternalIdPrefixes = new string[InternalIdPrefixes.Length];
                for (int i = 0; i < data.m_InternalIdPrefixes.Length; i++)
                {
                    data.m_InternalIdPrefixes[i] = InternalIdPrefixes[i];
                }
            }
            else
            {
                data.m_InternalIdPrefixes = null;
            }
        }

        internal void Write(CatalogBinaryWriter writer, SerializedTypeAsmContainer staCont)
        {
            if (Version < 1 || Version > 3)
                throw new NotSupportedException($"Binary catalog version {Version} is not supported.");
            if (!BinaryHasBuildResultHash && (Version > 2 || BuildResultHash != null))
                throw new InvalidOperationException("Only early v1/v2 catalogs can omit the build-result hash field.");
            writer.Version = Version;
            writer.ReverseDynamicStrings = BinaryReverseDynamicStrings ?? (Version > 1 ? true : null);
            var coreType = _binaryKeyTypes.Values.FirstOrDefault(type => type.ClassName == "System.String" || type.ClassName == "System.Int32" || type.ClassName == "System.Int64" || type.ClassName == "System.Boolean");
            if (coreType != null)
                staCont.StandardLibAsm = coreType.AssemblyName;

            ContentCatalogDataBinaryHeader header = new ContentCatalogDataBinaryHeader { HasBuildResultHash = BinaryHasBuildResultHash };
            header.Write(writer); // empty header

            header.Magic = 0x0de38942;
            header.Version = Version;
            header.KeysOffset = (uint)writer.BaseStream.Position + 4;
            writer.Reserve(4 + Resources.Count * 4 * 2); // empty key list + length

            header.IdOffset = writer.WriteEncodedString(LocatorId);
            header.InstanceProviderOffset = InstanceProviderData?.Write(writer) ?? uint.MaxValue;
            header.SceneProviderOffset = SceneProviderData?.Write(writer) ?? uint.MaxValue;

            uint[] initObjectsOffsets = new uint[ResourceProviderData.Length];
            for (int i = 0; i < ResourceProviderData.Length; i++)
            {
                initObjectsOffsets[i] = ResourceProviderData[i].Write(writer);
            }

            header.InitObjectsArrayOffset = writer.WriteOffsetArray(initObjectsOffsets);
            header.BuildResultHashOffset = writer.WriteEncodedString(BuildResultHash);

            WriteResources(writer, header, staCont);

            writer.BaseStream.Position = 0;
            header.Write(writer);
        }

        private void WriteResources(CatalogBinaryWriter writer, ContentCatalogDataBinaryHeader header, SerializedTypeAsmContainer staCont)
        {
            List<uint[]> tmpLocationOffsetArray = new List<uint[]>();
            uint[] keyLocationOffsets = new uint[Resources.Count * 2];
            foreach (var kvp in Resources)
            {
                // resource locations are written first
                uint[] locationOffsets = new uint[kvp.Value.Count];
                for (int j = 0; j < kvp.Value.Count; j++)
                {
                    ResourceLocation location = kvp.Value[j];
                    locationOffsets[j] = location.Write(writer, staCont, Version);
                }

                tmpLocationOffsetArray.Add(locationOffsets);
            }

            int i = 0;
            int i2 = 0;
            foreach (var kvp in Resources)
            {
                _binaryKeyTypes.TryGetValue(kvp.Key, out SerializedType keyType);
                uint keyOffset = SerializedObjectDecoder.EncodeV2(writer, staCont, kvp.Key, Version, keyType);
                keyLocationOffsets[i++] = keyOffset;

                uint locationListOffset = writer.WriteOffsetArray(tmpLocationOffsetArray[i2++]);
                keyLocationOffsets[i++] = locationListOffset;
            }

            // don't write with cache since we already reserved space for it
            writer.BaseStream.Position = header.KeysOffset - 4;
            header.KeysOffset = writer.WriteOffsetArray(keyLocationOffsets, false);
        }

        private void WriteResources(ContentCatalogDataJson data)
        {
            HashSet<string> newInternalIdHs = new HashSet<string>();
            HashSet<string> newProviderIdHs = new HashSet<string>();
            HashSet<SerializedType> newResourceTypeHs = new HashSet<SerializedType>();
            HashSet<string> newInternalIdPrefixes = new HashSet<string>();

            HashSet<ResourceLocation> newLocationHs = new HashSet<ResourceLocation>();

            var resources = Resources.ToDictionary(pair => pair.Key, pair => new List<ResourceLocation>(pair.Value));

            foreach (var value in Resources.Values)
            {
                foreach (var location in value)
                {
                    newLocationHs.Add(location);

                    if (location.InternalId == null)
                        throw new Exception("Location's internal ID cannot be null");

                    if (location.ProviderId == null)
                        throw new Exception("Location's provider ID cannot be null");

                    if (JsonEntrySize == 3 && location.Data != null)
                        throw new NotSupportedException("Three-integer JSON entries cannot store extra data.");
                    if (JsonEntrySize < 7 && location.Type != null)
                        throw new NotSupportedException("This JSON entry layout cannot store resource types.");
                    if (JsonEntrySize == 7 && location.Type == null)
                        throw new InvalidDataException("This JSON entry layout requires a resource type.");

                    if (InternalIdPrefixes != null && WriteCompact)
                    {
                        int splitIndex = location.InternalId.LastIndexOf('/');
                        if (splitIndex != -1)
                        {
                            newInternalIdPrefixes.Add(location.InternalId[..splitIndex]);
                        }
                    }

                    newInternalIdHs.Add(location.InternalId);
                    newProviderIdHs.Add(location.ProviderId);

                    if (location.Type != null)
                    {
                        newResourceTypeHs.Add(location.Type);
                    }
                }
            }

            List<string> newInternalIds = newInternalIdHs.ToList();
            List<string> newProviderIds = newProviderIdHs.ToList();
            List<SerializedType> newResourceTypes = newResourceTypeHs.ToList();
            List<ResourceLocation> newLocations = newLocationHs.ToList();

            if (JsonEntrySize == 7 && Keys == null)
            {
                foreach (var location in newLocations)
                {
                    object key = location.JsonPrimaryKey?.ToString() == location.PrimaryKey ? location.JsonPrimaryKey : location.PrimaryKey;
                    if (key == null)
                        throw new InvalidDataException("Location primary key cannot be null.");
                    if (!resources.ContainsKey(key))
                        resources.Add(key, [location]);
                }
            }
            if (Keys != null)
                Keys = newLocations.Select(location => location.PrimaryKey).Distinct().ToArray();
            List<object> newKeys = resources.Keys.ToList();

            Dictionary<object, int> newKeyToIndex = MakeDictionaryList(newKeys);
            Dictionary<string, int> newInternalIdsToIndex = MakeDictionaryList(newInternalIds);
            Dictionary<string, int> newProviderIdsToIndex = MakeDictionaryList(newProviderIds);
            Dictionary<SerializedType, int> newResourceTypesToIndex = MakeDictionaryList(newResourceTypes);
            Dictionary<ResourceLocation, int> newLocationsToIndex = MakeDictionaryList(newLocations);

            MemoryStream entryDataStream = new MemoryStream();
            MemoryStream extraDataStream = new MemoryStream();
            using (BinaryWriter entryWriter = new BinaryWriter(entryDataStream))
            using (BinaryWriter extraWriter = new BinaryWriter(extraDataStream))
            {
                entryWriter.Write(newLocationHs.Count);

                foreach (var location in newLocationHs)
                {
                    int internalIdIndex = newInternalIdsToIndex[location.InternalId];
                    int providerIndex = newProviderIdsToIndex[location.ProviderId];
                    int dependencyKeyIndex = (location.DependencyKey == null) ? -1 : newKeyToIndex[location.DependencyKey];
                    int depHash = location.DependencyHashCode; // todo calculate this
                    int dataIndex = -1;
                    if (location.Data != null && JsonEntrySize >= 4)
                    {
                        dataIndex = (int)extraDataStream.Position;
                        SerializedObjectDecoder.EncodeV1(extraWriter, location.Data);
                    }
                    int primaryKeyIndex = -1;
                    int resourceTypeIndex = -1;
                    if (JsonEntrySize == 7)
                    {
                        object primaryKey = location.JsonPrimaryKey?.ToString() == location.PrimaryKey ? location.JsonPrimaryKey : location.PrimaryKey;
                        primaryKeyIndex = Keys == null ? newKeyToIndex[primaryKey] : Array.IndexOf(Keys, location.PrimaryKey);
                        resourceTypeIndex = newResourceTypesToIndex[location.Type];
                    }

                    entryWriter.Write(internalIdIndex);
                    entryWriter.Write(providerIndex);
                    entryWriter.Write(dependencyKeyIndex);
                    if (JsonEntrySize >= 5)
                        entryWriter.Write(depHash);
                    if (JsonEntrySize >= 4)
                        entryWriter.Write(dataIndex);
                    if (JsonEntrySize == 7)
                    {
                        entryWriter.Write(primaryKeyIndex);
                        entryWriter.Write(resourceTypeIndex);
                    }
                }
            }

            MemoryStream keyDataStream = new MemoryStream();
            MemoryStream bucketStream = new MemoryStream();
            using (BinaryWriter keyWriter = new BinaryWriter(keyDataStream))
            using (BinaryWriter bucketWriter = new BinaryWriter(bucketStream))
            {
                keyWriter.Write(newKeys.Count); // same as Resources.Count
                bucketWriter.Write(newKeys.Count);

                foreach (var resourceKvp in resources)
                {
                    object resourceKey = resourceKvp.Key;
                    List<ResourceLocation> resourceValue = resourceKvp.Value;

                    Bucket bucket = new Bucket
                    {
                        offset = (int)keyDataStream.Position,
                        entries = new int[resourceValue.Count]
                    };

                    // write key
                    if (JsonEntrySize == 3)
                        SerializedObjectDecoder.EncodeRawKey(keyWriter, resourceKey);
                    else
                        SerializedObjectDecoder.EncodeV1(keyWriter, resourceKey);

                    for (int i = 0; i < resourceValue.Count; i++)
                    {
                        bucket.entries[i] = newLocationsToIndex[resourceValue[i]];
                    }

                    // write bucket
                    bucketWriter.Write(bucket.offset);
                    bucketWriter.Write(bucket.entries.Length);
                    for (int i = 0; i < bucket.entries.Length; i++)
                    {
                        bucketWriter.Write(bucket.entries[i]);
                    }
                }
            }

            ProviderIds = newProviderIds.ToArray();
            InternalIds = newInternalIds.ToArray();
            if (InternalIdPrefixes != null)
            {
                if (WriteCompact)
                    InternalIdPrefixes = newInternalIdPrefixes.ToArray();
                else
                    InternalIdPrefixes = Array.Empty<string>();
            }
            ResourceTypes = newResourceTypes.ToArray();

            data.m_BucketDataString = Convert.ToBase64String(bucketStream.ToArray());
            data.m_KeyDataString = Convert.ToBase64String(keyDataStream.ToArray());
            data.m_EntryDataString = Convert.ToBase64String(entryDataStream.ToArray());
            data.m_ExtraDataString = Convert.ToBase64String(extraDataStream.ToArray());
        }

        private static Dictionary<T, int> MakeDictionaryList<T>(List<T> list)
        {
            return list
                .Select((item, index) => new { Item = item, Index = index })
                .ToDictionary(x => x.Item, x => x.Index);
        }

        private struct Bucket
        {
            public int offset;
            public int[] entries;

            public Bucket(int offset, int[] entries)
            {
                this.offset = offset;
                this.entries = entries;
            }
        }
    }
}
