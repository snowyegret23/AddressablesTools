namespace AddressablesTools.Catalog
{
    public class ClassJsonObject
    {
        public SerializedType Type { get; set; }
        public string JsonText { get; set; }
        internal byte JsonTag { get; set; } = 7;

        public ClassJsonObject(string assemblyName, string className, string jsonText)
        {
            Type = new SerializedType
            {
                AssemblyName = assemblyName,
                ClassName = className,
            };
            JsonText = jsonText;
        }
    }
}
