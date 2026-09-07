using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AddressablesTools.Binary
{
    internal class CatalogBinaryReader : BinaryReader
    {
        public int Version { get; set; } = 1;
        public bool? ReverseDynamicStrings { get; set; }

        private readonly Dictionary<(uint, Type), object> _objCache = [];
        private readonly Dictionary<(uint, char, bool?), string> _stringCache = [];

        public CatalogBinaryReader(Stream input) : base(input) { }

        public T CacheAndReturn<T>(uint offset, T obj)
        {
            _objCache[(offset, typeof(T))] = obj;
            return obj;
        }

        public bool TryGetCachedObject<T>(uint offset, out T typedObj)
        {
            if (_objCache.TryGetValue((offset, typeof(T)), out object obj))
            {
                typedObj = (T)obj;
                return true;
            }

            typedObj = default;
            return false;
        }

        private string ReadBasicString(long offset, bool unicode)
        {
            ValidateRange(offset - 4, 4);
            BaseStream.Position = offset - 4;
            int length = ReadInt32();
            ValidateRange(offset, length);
            if (unicode && (length & 1) != 0)
                throw new InvalidDataException("Invalid UTF-16 string length.");
            byte[] data = ReadBytes(length);
            if (unicode)
            {
                return Encoding.Unicode.GetString(data);
            }
            else
            {
                return Encoding.ASCII.GetString(data);
            }
        }

        private string ReadDynamicString(long offset, bool unicode, char sep)
        {
            BaseStream.Position = offset;

            List<string> partStrs = new List<string>();
            HashSet<long> visited = [];
            while (true)
            {
                if (!visited.Add(BaseStream.Position))
                    throw new InvalidDataException("Cyclic dynamic string.");
                ValidateRange(BaseStream.Position, 8);
                long partStringOffset = ReadUInt32();
                long nextPartOffset = ReadUInt32();

                if ((partStringOffset & 0x40000000) != 0 && partStringOffset != uint.MaxValue)
                    throw new InvalidDataException("Nested dynamic string part.");
                partStrs.Add(ReadEncodedString((uint)partStringOffset));

                if (nextPartOffset == uint.MaxValue)
                {
                    break;
                }

                BaseStream.Position = nextPartOffset;
            }

            if (partStrs.Count == 1)
                return partStrs[0];

            if (ReverseDynamicStrings == null)
                throw new NotSupportedException("Ambiguous v1 dynamic-string order. Specify reverseDynamicStrings when opening this catalog.");
            if (ReverseDynamicStrings.Value)
                return string.Join(sep, partStrs.AsEnumerable().Reverse());
            else
                return string.Join(sep, partStrs);
        }

        public string ReadEncodedString(uint encodedOffset, char dynstrSep = '\0')
        {
            if (encodedOffset == uint.MaxValue)
            {
                return null;
            }

            if (_stringCache.TryGetValue((encodedOffset, dynstrSep, ReverseDynamicStrings), out string cachedStr))
            {
                return cachedStr;
            }

            bool unicode = (encodedOffset & 0x80000000) != 0;
            bool dynamicString = (encodedOffset & 0x40000000) != 0;
            if (dynamicString && dynstrSep == '\0')
                throw new InvalidDataException("Dynamic string requires a separator.");
            long offset = encodedOffset & 0x3fffffff;

            string result = dynamicString ? ReadDynamicString(offset, unicode, dynstrSep) : ReadBasicString(offset, unicode);
            _stringCache[(encodedOffset, dynstrSep, ReverseDynamicStrings)] = result;
            return result;
        }

        public uint[] ReadOffsetArray(uint encodedOffset)
        {
            if (encodedOffset == uint.MaxValue)
            {
                return [];
            }

            if (TryGetCachedObject(encodedOffset, out uint[] cachedArr))
            {
                return cachedArr;
            }

            ValidateRange((long)encodedOffset - 4, 4);
            BaseStream.Position = (long)encodedOffset - 4;
            int byteSize = ReadInt32();
            ValidateRange(encodedOffset, byteSize);
            if (byteSize % 4 != 0)
            {
                throw new InvalidDataException("Array size must be a multiple of 4");
            }

            int elemCount = byteSize / 4;
            uint[] result = new uint[elemCount];
            for (int i = 0; i < elemCount; i++)
            {
                result[i] = ReadUInt32();
            }

            return CacheAndReturn(encodedOffset, result);
        }

        public T ReadCustom<T>(uint offset, Func<T> fetchFunc)
        {
            if (!TryGetCachedObject(offset, out T v))
            {
                v = fetchFunc();
                _objCache[(offset, typeof(T))] = v;
            }

            return v;
        }

        public void ValidateRange(long offset, long size)
        {
            if (offset < 0 || size < 0 || offset > BaseStream.Length || size > BaseStream.Length - offset)
                throw new InvalidDataException($"Catalog range {offset}+{size} is outside the file.");
        }
    }
}
