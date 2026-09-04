using System.Text;

namespace Rivo.Settings.Application;

/// <summary>
/// Leitor mínimo de CSV (RFC 4180: campos entre aspas, aspas escapadas por
/// duplicação, vírgula como separador) — para a importação em massa
/// (Analytics & IA, ADR-047). Sem biblioteca externa: o âmbito é ler três
/// tabelas simples de nome/NIF/contactos, não um formato geral.
/// </summary>
internal static class CsvDocument
{
    /// <summary>
    /// <c>null</c> quando o ficheiro não tem sequer uma linha de cabeçalho.
    /// As linhas em branco (finais, sobretudo) são ignoradas.
    /// </summary>
    public static (IReadOnlyList<string> Header, IReadOnlyList<(int Line, IReadOnlyList<string> Fields)> Rows)? Parse(
        string content)
    {
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select((text, index) => (Line: index + 1, Text: text))
            .Where(l => l.Text.Trim().Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            return null;
        }

        var header = SplitLine(lines[0].Text);
        var rows = lines.Skip(1).Select(l => (l.Line, Fields: SplitLine(l.Text))).ToList();

        return (header, rows);
    }

    /// <summary>
    /// Nome da coluna (sem distinguir maiúsculas/minúsculas) → posição.
    /// <c>null</c> se alguma das colunas obrigatórias não estiver no
    /// cabeçalho — rejeitar o ficheiro inteiro em vez de linha a linha, o
    /// erro está na folha, não numa linha.
    /// </summary>
    public static Dictionary<string, int>? IndexColumns(IReadOnlyList<string> header, params string[] required)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Count; i++)
        {
            map[header[i].Trim()] = i;
        }

        return required.All(map.ContainsKey) ? map : null;
    }

    private static IReadOnlyList<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
