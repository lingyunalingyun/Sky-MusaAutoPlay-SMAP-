using System.Security.Cryptography;
using System.Text.Json;
using SMAP_WPF.Updates;

if (args.Length != 6) throw new ArgumentException("Usage: UpdatePack published-directory output-directory alpha|stable version key-id private-key-file");
using var signer = ECDsa.Create(); signer.ImportFromPem(File.ReadAllText(args[5]));
if (signer.KeySize != 256) throw new InvalidDataException("A P-256 release key is required.");
var manifest = UpdateBuilder.Build(args[0], args[1], args[2], args[3]);
byte[] signed = UpdateBuilder.Sign(manifest, args[4], signer);
string path = Path.Combine(args[1], $"SMAP-{args[2]}-manifest.json");
File.WriteAllBytes(path, signed);
Console.WriteLine(JsonSerializer.Serialize(new { manifest.Version, manifest.Channel, manifest.ReleaseId,
    files = manifest.Files.Length, packs = manifest.Packs.Length, bytes = manifest.Packs.Sum(p => p.Size), manifestPath = Path.GetFullPath(path) }));
