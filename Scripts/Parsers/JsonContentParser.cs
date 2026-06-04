using System.Collections.Generic;
using System.Text.Json;

namespace TormentaVTT.Parsers
{
    public static class JsonContentParser
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static IReadOnlyList<T>? ParseArray<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<List<T>>(json, Options);
            }
            catch
            {
                return null;
            }
        }
    }
}
