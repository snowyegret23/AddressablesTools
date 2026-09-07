A Unityless way to read and write addressables.

Work in progress.

Downloads:

- https://github.com/snowyegret23/AddressablesTools/releases

Each push to master publishes a release tagged with the first 12 characters of
the triggering commit hash, after Windows x64 and Linux x64 builds and CLI smoke
tests pass. The "Release commit" workflow can also be run manually on master.
Already published releases are left unchanged when a workflow is rerun.

Both platforms provide framework-dependent and self-contained .NET 8 packages.
Choose self-contained to run without installing .NET. Extract the entire archive
and run Example.exe on Windows or ./Example on Linux. Framework-dependent builds
require the .NET 8 x64 runtime. Every package also includes AddressablesTools.dll
and its dependencies. Linux packages use tar.gz to preserve executable permissions.

Upstream NuGet package: https://www.nuget.org/packages/AssetsTools.NET.Addressables

Supported catalog formats:

The following layouts were checked against all 152 package versions available
from Unity's package registry on 2026-09-06 (0.0.8-preview through 4.0.1).
Package versions and binary catalog header versions are different numbers.

Format / package family                         Read       Write
ResourceLocationList, 0.0.8-0.0.22 previews       Yes        Existing locations
JSON, 3 integers per entry, 0.0.26-0.0.27         Yes        Yes
JSON, 4 integers per entry, 0.1-0.6 previews      Yes        Yes
JSON, 5 integers per entry, 0.7-0.8 previews      Yes        Yes
JSON, 7 integers with m_Keys, 1.1.3-1.16.7        Yes        Yes
JSON, 7 integers without m_Keys, 1.16.8-4.0.1     Yes        Yes
Binary v1, 28-byte header, 1.21.3-1.21.19         Yes        Yes
Binary v1, 32-byte header, 1.21.20-1.29.0         Yes        Yes
Binary v2, 28-byte header, 2.0.3-2.0.6            Yes        Yes
Binary v2, 32-byte header, 2.0.8-3.1.0            Yes        Yes
Binary v3, 32-byte header, 4.0.0-4.0.1            Yes        Yes
UnityFS bundle containing one catalog TextAsset  Yes        Yes

JSON field casing, entry layout, legacy object tags, old primary-key tables,
compact internal-ID prefixes and unknown top-level fields are preserved.
AssetBundleRequestOptions retains unknown JSON fields, field presence and
integer settings such as RedirectLimit=-1. Timeout, RedirectLimit and RetryCount
are now int properties, matching Unity's JSON representation.

Binary output preserves the header version and early/late header layout.
Addressables 1.28+ reverses dynamic-string parts without changing the v1 header
version. The reader detects the order from serialized type identities. If the
order is ambiguous, use FromBinaryData(bytes, reverseDynamicStrings: false)
for the original order or true for the later order. Newly constructed v1 catalogs
with no specified order use basic strings, which both implementations can read.
BinaryHasBuildResultHash and BinaryReverseDynamicStrings expose these settings.
Binary v3 supports simple assembly names and omitted core-library assembly names.
Existing binary key/data type identities are retained when saving.

ToBundle preserves JSON/binary payload format, TextAsset name and other assets.
The catalog need not be the first TextAsset or the first serialized file.
Input and output bundle paths/streams must be different. GetCatalogFileType
accepts UTF-8 BOM/JSON whitespace and leaves the seekable stream position intact.

Limits:
- ResourceLocationList supports editing existing locations and dependencies;
  its Resources view indexes stored address strings, GUIDs and labels. Replacing
  that index, adding new locations or converting it to binary is not supported.
- Unknown binary serialization adapters need their own schema/implementation.
  Unknown JSON objects retain their type identity and JSON payload.
- Future layouts, encrypted/custom catalogs and big-endian binary catalogs are
  not claimed to be supported. Unrecognized layout versions are rejected.
- Format conversion is not a migration between arbitrary package versions.
  Prefer saving back to the input format. Catalog .hash files, remote caches,
  signatures and deployed game files are not updated automatically.

Validation includes standalone official BinaryStorageBuffer reader/writer
cross-checks for all 60 packages containing binary catalogs (engine-native
facilities stubbed), JSON key-codec checks against 140 packages, synthetic JSON
layouts and UnityFS bundles, and full
semantic roundtrips/CRC edits of three real game catalogs. This is not a claim
that every package version was tested inside a Unity Editor or game player.

Official source packages: https://packages.unity.com/com.unity.addressables

---

To use the "Example" command line app to...

- patch catalog CRCs, run `Example patchcrc path/to/catalog.json` (replace .json with .bin if your game uses .bin)
- search for assets, run `Example searchasset path/to/catalog.json` and then type the key to search for

---

Using AddressablesTools is simple. Use either
AddressablesCatalogFileParser.FromBundle("path/to/file.bundle");
or
AddressablesCatalogFileParser.FromJsonString(File.ReadAllText("path/to/file.json"));
or
AddressablesCatalogFileParser.FromBinaryData(File.ReadAllBytes("path/to/file.bin"));

Save with ToJsonString, ToBinaryData or ToBundle, respectively. Prefer writing
to a new file and validating it before replacing a deployed catalog.

From there, you can access the `Resources` dictionary which contains a mapping from an object (usually a string or number) to a list of resource locations.
Dependencies are populated for both JSON and binary catalogs; JSON locations
also retain DependencyKey. Shared locations remain shared after binary loading.

In the `searchasset` example below, we can look up an asset string and find all of the bundles that are needed to load it. To do so, we find all keys that contain the substring we're searching for and that have resource locations with a `ProviderId` of `BundledAssetProvider`. After that, we can look up the `Dependency` id back into the `Resources` dictionary to find all of the necessary bundles. In this list, the first item is always the bundle that contains the asset, and all other bundles are dependencies needed by the first bundle.

This can be useful if you want to know what bundles you need to load in order to load an asset in a tool without having to load every bundle in the game.

---

If you're still confused, maybe check out https://github.com/nesrak1/SeaOfStarsSpriteExtractor for a more "real-life" example.

---

The "Example" program contains two tools: searchasset and patchcrc.

The searchasset command takes an argument to the catalog.json or catalog.bundle file. It will then ask you for a string to search for and will display any results that it finds.

The patchcrc command also takes an argument to catalog.json or catalog.bundle. It sets the m_Crc of all entries to 0, effectively disabling all CRC checks.

---

This software is not sponsored by or affiliated with Unity Technologies or its affiliates. "Unity" is a registered trademark of Unity Technologies or its affiliates in the U.S. and elsewhere.
