namespace AddressablesTools.Catalog
{
    public class WrappedSerializedObject
    {
        public SerializedType Type { get; set; }
        public object Object { get; set; }
        internal byte JsonTag { get; set; } = 7;

        public WrappedSerializedObject(SerializedType type, object obj)
        {
            Type = type;
            Object = obj;
        }
    }
}
