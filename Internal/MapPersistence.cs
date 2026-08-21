using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Polaris.Map.Internal
{
    internal static class MapPersistence
    {
        static readonly UTF8Encoding Utf8 = new(false, true);
        const string OwnershipHeader = "PolarisMap owned TMAP v1";

        internal static string PersistNew(string key, byte[] body)
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "m2d");
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Game map directory does not exist: {directory}.");
            }

            string mapPath = Path.Combine(directory, key + ".tmap");
            string listPath = Path.Combine(directory, "__m2d_list.dat");
            if (File.Exists(mapPath))
            {
                throw new IOException($"Refusing to overwrite an existing map file: {mapPath}.");
            }
            if (!File.Exists(listPath))
            {
                throw new FileNotFoundException("The game's map list was not found.", listPath);
            }

            string[] existing = File.ReadAllLines(listPath, Utf8);
            if (existing.Any(line => string.Equals(line.Trim(), key, StringComparison.Ordinal)))
            {
                throw new IOException($"Map key already exists in __m2d_list.dat: {key}.");
            }

            AtomicCreate(mapPath, body);
            try
            {
                string newline = DetectNewline(File.ReadAllText(listPath, Utf8));
                string list = string.Join(newline, existing);
                if (list.Length > 0 && !list.EndsWith(newline, StringComparison.Ordinal))
                {
                    list += newline;
                }
                list += key + newline;
                AtomicReplace(listPath, Utf8.GetBytes(list));
            }
            catch (Exception ex)
            {
                throw new IOException(
                    $"The map file was created, but __m2d_list.dat could not be updated. The orphan file is: {mapPath}.", ex);
            }

            return mapPath;
        }

        /// <summary>写入 .pmap 管理的 TMAP；替换时校验所有权 sidecar。</summary>
        internal static string PersistOwned(string key, byte[] body)
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "m2d");
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Game map directory does not exist: {directory}.");

            string mapPath = Path.Combine(directory, key + ".tmap");
            string ownerPath = mapPath + ".polaris-map";
            string listPath = Path.Combine(directory, "__m2d_list.dat");
            if (!File.Exists(listPath))
                throw new FileNotFoundException("The game's map list was not found.", listPath);

            string expectedOwner = OwnershipHeader + "\n" + key + "\n";
            bool mapExists = File.Exists(mapPath);
            bool ownerMatches = File.Exists(ownerPath)
                && string.Equals(File.ReadAllText(ownerPath, Utf8), expectedOwner, StringComparison.Ordinal);
            if (mapExists && !ownerMatches)
                throw new IOException($"Refusing to overwrite a map not owned by PolarisMap: {mapPath}.");

            string[] existing = File.ReadAllLines(listPath, Utf8);
            bool listed = existing.Any(line => string.Equals(line.Trim(), key, StringComparison.Ordinal));
            if (!mapExists && listed && !ownerMatches)
                throw new IOException($"Map key already belongs to the game's map list: {key}.");

            if (mapExists) AtomicReplace(mapPath, body);
            else AtomicCreate(mapPath, body);

            try
            {
                if (File.Exists(ownerPath)) AtomicReplace(ownerPath, Utf8.GetBytes(expectedOwner));
                else AtomicCreate(ownerPath, Utf8.GetBytes(expectedOwner));

                if (!listed)
                {
                    string original = File.ReadAllText(listPath, Utf8);
                    string newline = DetectNewline(original);
                    string list = original;
                    if (list.Length > 0 && !list.EndsWith(newline, StringComparison.Ordinal)) list += newline;
                    list += key + newline;
                    AtomicReplace(listPath, Utf8.GetBytes(list));
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"The .pmap TMAP was written, but its ownership/list metadata failed: {mapPath}.", ex);
            }

            return mapPath;
        }

        static void AtomicCreate(string path, byte[] content)
        {
            string temporary = path + ".polaris-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, content);
                File.Move(temporary, path);
            }
            finally
            {
                TryDeleteTemporary(temporary);
            }
        }

        static void AtomicReplace(string path, byte[] content)
        {
            string temporary = path + ".polaris-" + Guid.NewGuid().ToString("N") + ".tmp";
            string backup = path + ".polaris-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak";
            try
            {
                File.WriteAllBytes(temporary, content);
                File.Replace(temporary, path, backup, true);
            }
            finally
            {
                TryDeleteTemporary(temporary);
            }
        }

        static string DetectNewline(string value)
            => value.Contains("\r\n") ? "\r\n" : "\n";

        static void TryDeleteTemporary(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup only. Never mask the write/replace exception.
            }
        }
    }
}
