using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TormentaVTT.Services
{
    public sealed class PdfImportService
    {
        // If path ends with .txt, read directly; if .pdf, try to use pdftotext (poppler).
        public bool TryImportToJson(string inputPath, string outputJsonPath, out string error)
        {
            error = string.Empty;
            try
            {
                if (!File.Exists(inputPath))
                {
                    error = "Arquivo de entrada não existe.";
                    return false;
                }

                string text;
                var ext = Path.GetExtension(inputPath).ToLowerInvariant();
                if (ext == ".txt")
                {
                    text = File.ReadAllText(inputPath);
                }
                else if (ext == ".pdf")
                {
                    // Try pdftotext
                    var txtTemp = Path.GetTempFileName();
                    var psi = new ProcessStartInfo("pdftotext")
                    {
                        Arguments = $"\"{inputPath}\" \"{txtTemp}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    try
                    {
                        using var p = Process.Start(psi);
                        if (p == null)
                        {
                            error = "Falha ao iniciar pdftotext.";
                            return false;
                        }

                        p.WaitForExit(10000);
                        if (File.Exists(txtTemp))
                        {
                            text = File.ReadAllText(txtTemp);
                            File.Delete(txtTemp);
                        }
                        else
                        {
                            error = "pdftotext falhou ou não está disponível.";
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        error = "Falha ao executar pdftotext: " + ex.Message;
                        return false;
                    }
                }
                else
                {
                    error = "Extensão não suportada.";
                    return false;
                }

                // Minimal JSON output: store raw text and metadata.
                var doc = new
                {
                    source = Path.GetFileName(inputPath),
                    imported_at = DateTime.UtcNow.ToString("o"),
                    raw = text
                };

                var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                var dir = Path.GetDirectoryName(outputJsonPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outputJsonPath, json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
