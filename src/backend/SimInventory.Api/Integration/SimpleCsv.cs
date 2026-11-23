using System.Text;

namespace SimInventory.Api.Integration;

/// <summary>Minimal RFC 4180 reader: quoted fields, escaped quotes, commas and newlines inside quotes.</summary>
public static class SimpleCsv
{
    public sealed record Table(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

    public static Table Parse(string text)
    {
        var records = ReadRecords(text.TrimStart('﻿')).Where(r => r.Any(f => f.Length > 0)).ToList();
        if (records.Count == 0) throw new FormatException("The CSV file is empty.");
        var headers = records[0].Select(h => h.Trim().ToLowerInvariant().Replace(' ', '_')).ToList();
        if (headers.Distinct().Count() != headers.Count) throw new FormatException("The CSV header contains duplicate columns.");
        var rows = new List<IReadOnlyDictionary<string, string>>();
        for (var i = 1; i < records.Count; i++)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count; c++)
                row[headers[c]] = c < records[i].Count ? records[i][c].Trim() : string.Empty;
            rows.Add(row);
        }
        return new Table(headers, rows);
    }

    private static IEnumerable<List<string>> ReadRecords(string text)
    {
        var field = new StringBuilder();
        var record = new List<string>();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': inQuotes = true; break;
                case ',': record.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    record.Add(field.ToString()); field.Clear();
                    yield return record; record = new List<string>();
                    break;
                default: field.Append(ch); break;
            }
        }
        if (inQuotes) throw new FormatException("The CSV file has an unterminated quoted field.");
        if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); yield return record; }
    }
}
