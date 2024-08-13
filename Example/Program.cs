﻿using AddressablesTools;
using AddressablesTools.Catalog;
using AddressablesTools.Classes;
using AssetsTools.NET;

static void SearchExample(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("need path to catalog.json");
        return;
    }

    bool fromBundle = IsUnityFS(args[1]);

    ContentCatalogData ccd;
    if (fromBundle)
    {
        ccd = AddressablesCatalogFileParser.FromBundle(args[1]);
    }
    else
    {
        CatalogFileType fileType;
        using (FileStream fs = File.OpenRead(args[1]))
        {
            fileType = AddressablesCatalogFileParser.GetCatalogFileType(fs);
        }

        if (fileType == CatalogFileType.Json)
        {
            ccd = AddressablesCatalogFileParser.FromJsonString(File.ReadAllText(args[1]));
        }
        else if (fileType == CatalogFileType.Binary)
        {
            ccd = AddressablesCatalogFileParser.FromBinaryData(File.ReadAllBytes(args[1]));
        }
        else
        {
            Console.WriteLine("not a valid catalog file");
            return;
        }
    }

    Console.Write("search key to find bundles of: ");
    string? search = Console.ReadLine();

    if (search == null)
    {
        return;
    }

    search = search.ToLower();

    foreach (object k in ccd.Resources.Keys)
    {
        if (k is string s && s.ToLower().Contains(search))
        {
            Console.Write(s);
            foreach (var rsrc in ccd.Resources[s])
            {
                Console.WriteLine($" ({rsrc.ProviderId})");
                if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
                {
                    var data = rsrc.Data;
                    if (data is WrappedSerializedObject { Object: AssetBundleRequestOptions abro })
                    {
                        uint crc = abro.Crc;
                        Console.WriteLine($"  crc = {crc:x8}");
                    }
                }
                else if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider")
                {
                    List<ResourceLocation> locs;
                    if (rsrc.Dependencies != null)
                    {
                        // new version
                        locs = rsrc.Dependencies;
                    }
                    else if (rsrc.DependencyKey != null)
                    {
                        // old version
                        locs = ccd.Resources[rsrc.DependencyKey];
                    }
                    else
                    {
                        continue;
                    }

                    Console.WriteLine($"  {locs[0].InternalId}");
                    if (locs.Count > 1)
                    {
                        for (int i = 1; i < locs.Count; i++)
                        {
                            Console.WriteLine($"    {locs[i].InternalId}");
                        }
                    }
                }
            }
        }
    }
}

static bool IsUnityFS(string path)
{
    const string unityFs = "UnityFS";
    using AssetsFileReader reader = new AssetsFileReader(path);
    if (reader.BaseStream.Length < unityFs.Length)
    {
        return false;
    }

    return reader.ReadStringLength(unityFs.Length) == unityFs;
}

static void PatchCrcExample(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("need path to catalog.json");
        return;
    }

    bool fromBundle = IsUnityFS(args[1]);

    ContentCatalogData ccd;
    if (fromBundle)
        ccd = AddressablesCatalogFileParser.FromBundle(args[1]);
    else
        ccd = AddressablesCatalogFileParser.FromJsonString(File.ReadAllText(args[1]));

    Console.WriteLine("patching...");

    foreach (var resourceList in ccd.Resources.Values)
    {
        foreach (var rsrc in resourceList)
        {
            if (rsrc.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
            {
                var data = rsrc.Data;
                if (data is WrappedSerializedObject { Object: AssetBundleRequestOptions abro })
                {
                    abro.Crc = 0;
                }
            }
        }
    }

    if (fromBundle)
        AddressablesCatalogFileParser.ToBundle(ccd, args[1], args[1] + ".patched");
    else
        File.WriteAllText(args[1] + ".patched", AddressablesCatalogFileParser.ToJson(ccd));

    File.Move(args[1], args[1] + ".old");
    File.Move(args[1] + ".patched", args[1]);
}

if (args.Length < 1)
{
    Console.WriteLine("need args: <mode> <file>");
    Console.WriteLine("modes: searchasset, patchcrc");
}
else if (args[0] == "searchasset")
{
    SearchExample(args);
}
else if (args[0] == "patchcrc")
{
    PatchCrcExample(args);
}
else
{
    Console.WriteLine("mode not supported");
}