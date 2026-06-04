using System;
using System.IO;
using System.Text.Json;
using TormentaVTT.Services;
using TormentaVTT.Models;

namespace TormentaVTT.Importers
{
    public sealed class DocumentImporter
    {
        private readonly PdfImportService _pdfImportService;
        private readonly TextContentParser _textContentParser;

        public DocumentImporter()
        {
            _pdfImportService = new PdfImportService();
            _textContentParser = new TextContentParser();
        }

        public bool TryImportDocument(string inputPath, string contentDir, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                message = "Arquivo de documento não encontrado.";
                return false;
            }

            var raw = string.Empty;
            var extension = Path.GetExtension(inputPath).ToLowerInvariant();
            if (extension == ".txt")
            {
                raw = File.ReadAllText(inputPath);
            }
            else if (extension == ".pdf")
            {
                var tempJsonPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(inputPath) + "_import.json");
                if (!_pdfImportService.TryImportToJson(inputPath, tempJsonPath, out var error))
                {
                    message = error;
                    return false;
                }

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(tempJsonPath));
                    if (doc.RootElement.TryGetProperty("raw", out var rawProperty))
                        raw = rawProperty.GetString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    message = "Falha ao ler texto extraído do PDF: " + ex.Message;
                    return false;
                }
                finally
                {
                    try { File.Delete(tempJsonPath); } catch { }
                }
            }
            else
            {
                message = "Tipo de documento não suportado. Use .txt ou .pdf.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                message = "Documento vazio ou texto não extraído.";
                return false;
            }

            if (!Directory.Exists(contentDir))
                Directory.CreateDirectory(contentDir);

            var rawJsonPath = Path.Combine(contentDir, "imported_document.json");
            File.WriteAllText(rawJsonPath, JsonSerializer.Serialize(new { source = Path.GetFileName(inputPath), imported_at = DateTime.UtcNow.ToString("o"), raw }, new JsonSerializerOptions { WriteIndented = true }));

            var parsed = _textContentParser.Parse(raw);
            _textContentParser.SaveParsedOutput(parsed, contentDir);

            message = $"Documento importado e parseado. Raw salvo em {rawJsonPath}. Parsed em: classes_parsed.json, races_parsed.json, powers_parsed.json, spells_parsed.json, conditions_parsed.json, threats_parsed.json.";
            return true;
        }
    }
}
