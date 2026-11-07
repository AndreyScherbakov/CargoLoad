using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace CargoLoad
{
    /// <summary>
    /// Минимальный читатель xlsx без внешних пакетов: xlsx — это ZIP с XML.
    /// Поддерживает первый лист, числовые и строковые (shared strings) ячейки.
    /// </summary>
    public static class SheetReader
    {
        public sealed class Sheet
        {
            public List<List<object?>> Rows = new(); // object? : string | double | null
            public int HeaderIndex { get; set; } = 0;

            public int RowCount => Rows.Count;

            public object? Cell(int row, int col)
            {
                if (row < 0 || row >= Rows.Count) return null;
                var r = Rows[row];
                if (col < 0 || col >= r.Count) return null;
                return r[col];
            }

            public int ColumnByName(string[] names)
            {
                for (int c = 0; c < (Rows.Count > 0 ? Rows[HeaderIndex].Count : 0); c++)
                {
                    string? h = Convert.ToString(Rows[HeaderIndex][c], CultureInfo.InvariantCulture)?.Trim();
                    if (h == null) continue;
                    foreach (var n in names)
                        if (string.Equals(h, n, StringComparison.OrdinalIgnoreCase))
                            return c;
                }
                return -1;
            }
        }

        public static Sheet ReadXlsx(string path)
        {
            var sheet = new Sheet();
            using var zip = ZipFile.OpenRead(path);

            // shared strings
            var shared = new List<string>();
            var ssEntry = zip.Entries.FirstOrDefault(e => e.FullName == "xl/sharedStrings.xml");
            if (ssEntry != null)
            {
                using var sr = new StreamReader(ssEntry.Open());
                var xd = XDocument.Load(sr);
                foreach (var si in xd.Descendants())
                {
                    if (si.Name.LocalName != "si") continue;
                    string text = string.Concat(si.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value));
                    shared.Add(text);
                }
            }

            // первый лист определяем по workbook.xml + workbook.xml.rels
            string? target = null;
            var wb = zip.GetEntry("xl/workbook.xml");
            if (wb != null)
            {
                using var sr = new StreamReader(wb.Open());
                var xd = XDocument.Load(sr);
                var sh = xd.Descendants().FirstOrDefault(e => e.Name.LocalName == "sheet");
                string? firstRid = sh?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                if (firstRid != null)
                {
                    var wbRel = zip.GetEntry("xl/_rels/workbook.xml.rels");
                    if (wbRel != null)
                    {
                        using var sr2 = new StreamReader(wbRel.Open());
                        var xd2 = XDocument.Load(sr2);
                        var rel = xd2.Descendants().FirstOrDefault(e =>
                            e.Name.LocalName == "Relationship" && e.Attribute("Id")?.Value == firstRid);
                        target = rel?.Attribute("Target")?.Value;
                    }
                }
            }

            string entryPath;
            if (target == null) entryPath = "xl/worksheets/sheet1.xml";
            else if (target.StartsWith("/")) entryPath = target.TrimStart('/');
            else if (target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) entryPath = target;
            else entryPath = "xl/" + target;

            var wsEntry = zip.GetEntry(entryPath)
                ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml"));
            if (wsEntry == null) throw new InvalidDataException("Лист не найден в xlsx");

            using (var sr = new StreamReader(wsEntry.Open()))
            {
                var xd = XDocument.Load(sr);
                foreach (var rowEl in xd.Descendants().Where(e => e.Name.LocalName == "row"))
                {
                    var row = new List<object?>();
                    int prevCol = -1;
                    foreach (var c in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
                    {
                        string? refAttr = c.Attribute("r")?.Value;
                        int col = ColIndex(refAttr);
                        if (col < 0) col = prevCol + 1;
                        while (row.Count < col) row.Add(null);

                        string type = c.Attribute("t")?.Value ?? "";
                        object? val = null;
                        var v = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                        if (v != null)
                        {
                            string raw = v.Value;
                            if (type == "s" && int.TryParse(raw, out int si) && si >= 0 && si < shared.Count)
                                val = shared[si];
                            else if (type == "inlineStr")
                            {
                                var t = c.Descendants().FirstOrDefault(e => e.Name.LocalName == "t");
                                val = t?.Value ?? raw;
                            }
                            else if (type == "b")
                                val = raw == "1" ? "да" : "нет";
                            else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                                val = d;
                            else
                                val = raw;
                        }
                        else
                        {
                            var t = c.Descendants().FirstOrDefault(e => e.Name.LocalName == "t");
                            if (t != null) val = t.Value;
                        }
                        row.Add(val);
                        prevCol = col;
                    }
                    // trim trailing nulls
                    while (row.Count > 0 && row[row.Count - 1] == null) row.RemoveAt(row.Count - 1);
                    if (row.Count > 0) sheet.Rows.Add(row);
                }
            }

            return sheet;
        }

        private static int ColIndex(string? refAttr)
        {
            if (string.IsNullOrEmpty(refAttr)) return -1;
            int col = 0;
            foreach (char ch in refAttr)
            {
                if (char.IsLetter(ch))
                    col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
                else break;
            }
            return col - 1;
        }

        public static Sheet ReadCsv(string path)
        {
            var sheet = new Sheet();
            using var reader = new StreamReader(path);
            char sep = DetectSep(reader);
            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = SplitCsv(line, sep);
                var row = new List<object?>();
                foreach (var p in parts)
                {
                    string s = p.Trim().Trim('"');
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        row.Add(d);
                    else
                        row.Add(s);
                }
                while (row.Count > 0 && row[row.Count - 1] == null) row.RemoveAt(row.Count - 1);
                if (row.Count > 0) sheet.Rows.Add(row);
            }
            return sheet;
        }

        private static char DetectSep(StreamReader reader)
        {
            string? line = reader.ReadLine();
            if (line == null) return ';';
            int semis = line.Count(c => c == ';');
            int commas = line.Count(c => c == ',');
            int tabs = line.Count(c => c == '\t');
            if (tabs >= semis && tabs >= commas) return '\t';
            if (semis >= commas) return ';';
            return ',';
        }

        private static List<string> SplitCsv(string line, char sep)
        {
            var result = new List<string>();
            int start = 0;
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (c == sep && !inQuotes)
                {
                    result.Add(line.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(line.Substring(start));
            return result;
        }
    }
}
