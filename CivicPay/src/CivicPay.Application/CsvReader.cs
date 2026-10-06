using System.Text;
namespace CivicPay.Application;
// RFC 4180-style quoted fields, including escaped quotes and embedded newlines.
public static class CsvReader
{
    public static IEnumerable<string[]> Parse(string text)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, closed = false, started = false;
        for (var i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                        closed = true;
                    }
                }
                else
                    field.Append(c);
            }
            else if (c == '"')
            {
                Rules.Require(!started && !closed, "INVALID_CSV", "Unexpected quote in CSV field.", 400);
                quoted = true;
                started = true;
            }
            else if (c == ',' || c == '\r' || c == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                started = closed = false;
                if (c != ',')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    yield return fields.ToArray();
                    fields.Clear();
                }
            }
            else
            {
                Rules.Require(!closed, "INVALID_CSV", "Unexpected characters after a quoted CSV field.", 400);
                field.Append(c);
                started = true;
            }
        }
        Rules.Require(!quoted, "INVALID_CSV", "Unterminated quoted CSV field.", 400);
        if (started || closed || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }
}
