using System.Collections.Generic;
using System.IO;
using TormentaVTT.Parsers;

namespace TormentaVTT.Importers
{
    public static class JsonImporter
    {
        public static IReadOnlyList<T> LoadDefinitions<T>(string path)
        {
            if (!File.Exists(path))
                return new List<T>();

            var content = File.ReadAllText(path);
            return JsonContentParser.ParseArray<T>(content) ?? new List<T>();
        }
    }
}
