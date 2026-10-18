using System;
using System.IO;
using System.Text;

public static class GlesProgramChecks
{
    public static void Main(string[] args)
    {
        string vertex = "#version 300 es\nvoid main() { gl_Position = vec4(0.0); }";
        string fragment = "#version 300 es\nvoid main() { SV_Target0 = vec4(1.0); }";
        string valid = "#ifdef VERTEX\n" + vertex + "\n#endif\n#ifdef FRAGMENT\n" + fragment + "\n#endif\n";
        var actual = Parser.VerifiedGlesSections(Encoding.UTF8.GetBytes(valid), "fixture", 0);
        if (actual[0] != vertex || actual[1] != fragment) throw new Exception("section identity differs");
        int rejects = 0;
        foreach (string invalid in new[] {
            "#ifdef VERTEX\n" + vertex + "\n#endif\n",
            valid.Replace(vertex, vertex.Replace("void main()", "void other()")),
            valid.Replace(fragment, fragment.Replace("void main()", "void other()")),
            valid.Replace("#version 300 es", "#version 100"),
            valid.Replace("gl_Position", "not_Position"),
            valid.Replace("SV_Target0", "not_Target0"),
            ""
        })
        {
            try { Parser.VerifiedGlesSections(Encoding.UTF8.GetBytes(invalid), "fixture", 0); }
            catch (InvalidOperationException) { rejects++; continue; }
            throw new Exception("incomplete GLES program accepted");
        }
        int real = 0;
        foreach (string path in args)
        {
            var sections = Parser.VerifiedGlesSections(File.ReadAllBytes(path), "actual compiler payload", real);
            if (sections.Length != 2) throw new Exception("actual sections missing");
            real++;
        }
        Console.WriteLine("GLES combined program parser: positive + " + rejects + " failure controls; actual payloads=" + real);
    }
}
