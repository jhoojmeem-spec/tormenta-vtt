using System.Collections.Generic;

namespace TormentaVTT.Importers
{
    public static class PdfImporter
    {
        public static bool CanImport(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.EndsWith(".pdf", System.StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<string> ExtractText(string path)
        {
            // Placeholder: inserir pipeline de extração e conversão de PDF para JSON.
            return new List<string>();
        }
    }
}
