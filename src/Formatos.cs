using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NotasRetencaoINSS
{
    public static class Formatos
    {
        public static readonly CultureInfo PtBr = new CultureInfo("pt-BR");

        public static string SoDigitos(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }

        public static string CnpjCpf(string doc)
        {
            string d = SoDigitos(doc);
            if (d.Length == 14) return Regex.Replace(d, @"^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$", "$1.$2.$3/$4-$5");
            if (d.Length == 11) return Regex.Replace(d, @"^(\d{3})(\d{3})(\d{3})(\d{2})$", "$1.$2.$3-$4");
            return doc ?? "";
        }

        public static string Dinheiro(decimal v)
        {
            return "R$ " + v.ToString("N2", PtBr);
        }

        /// <summary>Valor do XML fiscal (ponto como separador decimal).</summary>
        public static decimal Numero(string s)
        {
            decimal v;
            if (!string.IsNullOrEmpty(s) && decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out v)) return v;
            return 0m;
        }

        /// <summary>"2026-10-15T09:12:00-03:00" ou "2026-10-15" → "2026-10-15" (data do próprio documento, sem converter fuso).</summary>
        public static string DataIso(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = Regex.Match(s, @"^(\d{4}-\d{2}-\d{2})");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static DateTime? ParaData(string iso)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(iso) && DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }

        public static string DataBr(string iso)
        {
            DateTime? d = ParaData(iso);
            return d.HasValue ? d.Value.ToString("dd/MM/yyyy") : "";
        }

        /// <summary>Hora de Brasília, independente do fuso configurado no computador (sem horário de verão desde 2019).</summary>
        public static DateTime AgoraBrasilia()
        {
            return DateTime.UtcNow.AddHours(-3);
        }

        public static string AgoraIso()
        {
            return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        public static DateTime? LerIso(string s)
        {
            DateTime d;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParseExact(s, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }

        public static string EscaparXml(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
