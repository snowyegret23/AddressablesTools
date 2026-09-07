using System;

namespace AddressablesTools.Classes
{
    public class TypeReference
    {
        public string Clsid { get; set; }

        public override string ToString() => Clsid;
        public override bool Equals(object obj) => obj is TypeReference type && StringComparer.OrdinalIgnoreCase.Equals(Clsid, type.Clsid);
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Clsid ?? "");

        public TypeReference(string clsid)
        {
            Clsid = clsid;
        }
    }
}
