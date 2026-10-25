using System.Text;

namespace KnitSprite.CreativeMode.AI
{
    /// <summary>
    /// Minimal JSON helpers. JsonUtility can't serialize a bare string or an anonymous object,
    /// and Newtonsoft isn't installed, so request bodies are built as strings and every
    /// user- or model-supplied string goes through Quote().
    /// </summary>
    public static class Json
    {
        /// <summary>Returns s as a JSON string literal, including the surrounding quotes.</summary>
        public static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
