#!/usr/bin/env python3
"""Execute the shipping town-cloth contact geometry without starting Unity.

The production methods are extracted verbatim. A local Vector3/Mathf shim
supplies only arithmetic, so the checks exercise the same C# triangles and
segment math that the headset uses. This does not replace the native PhysX or
headset image check.
"""

from pathlib import Path
import os
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceCloth.cs'
METHODS = (
    'DistanceSquaredToSegment', 'OverlapsProbeBox', 'DistanceSquaredSegmentTriangle',
    'ClosestPointOnTriangle', 'ClosestFraction', 'DistanceSquaredSegments',
)


def extract_method(source: str, name: str) -> str:
    anchor = source.index(f'private static ', source.index('internal sealed class TownServiceCloth'))
    while True:
        start = source.find('private static ', anchor)
        if start < 0:
            raise AssertionError(f'production method missing: {name}')
        opening = source.find('{', start)
        if opening < 0:
            raise AssertionError(f'production body missing: {name}')
        signature = source[start:opening]
        if f' {name}(' in signature:
            depth = 0
            for at in range(opening, len(source)):
                if source[at] == '{':
                    depth += 1
                elif source[at] == '}':
                    depth -= 1
                    if depth == 0:
                        return source[start:at + 1]
            raise AssertionError(f'unclosed production body: {name}')
        anchor = opening + 1


SHIM = r'''
using System;
using System.Diagnostics;
using static ProductionGeometry;

struct Vector3
{
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
    public static Vector3 one => new(1f,1f,1f);
    public float sqrMagnitude => x*x+y*y+z*z;
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator *(Vector3 a, float n) => new(a.x*n,a.y*n,a.z*n);
    public static Vector3 Min(Vector3 a, Vector3 b) => new(MathF.Min(a.x,b.x),MathF.Min(a.y,b.y),MathF.Min(a.z,b.z));
    public static Vector3 Max(Vector3 a, Vector3 b) => new(MathF.Max(a.x,b.x),MathF.Max(a.y,b.y),MathF.Max(a.z,b.z));
    public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
    public static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
}
static class Mathf
{
    public static float Max(float a,float b) => MathF.Max(a,b);
    public static float Min(float a,float b) => MathF.Min(a,b);
    public static float Abs(float a) => MathF.Abs(a);
    public static float Sqrt(float a) => MathF.Sqrt(a);
    public static float Clamp01(float a) => Math.Clamp(a,0f,1f);
}
static class ProductionGeometry
{
'''

MAIN = r'''
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        assertions++;
    }
    static int assertions;
    static void Main()
    {
        // 13 columns across a 1.5 m altar leave 12.5 cm particle gaps.
        // The native triangle DOES intersect a fingertip at the cell centre.
        Vector3 a = new(.0625f,.70f,.025f), b = new(.0625f,.70f,-.025f);
        Vector3 p = new(0f,.68f,0f), q = new(.125f,.68f,0f), r = new(0f,.72f,0f);
        float vertexMiss = MathF.Min(DistanceSquaredToSegment(p,a,b,out _),
            MathF.Min(DistanceSquaredToSegment(q,a,b,out _), DistanceSquaredToSegment(r,a,b,out _)));
        float triangleHit = DistanceSquaredSegmentTriangle(a,b,p,q,r,out float crossing);
        Check(vertexMiss > .03f*.03f, "vertex-only gate must miss interior touch");
        Check(triangleHit < 1e-8f && crossing > 0f && crossing < 1f,
            "production triangle must detect interior touch");

        // In the user's video the glove back crosses the enchantress drape
        // while its previous palm-to-index capsule can remain outside it.
        Vector3 cloth = new(0f,0f,0f);
        Vector3 palm = new(0f,0f,.08f), fingertip = new(0f,0f,.17f);
        Vector3 wrist = new(0f,0f,-.07f);
        float oldGap = MathF.Sqrt(DistanceSquaredToSegment(cloth,palm,fingertip,out _))-.035f;
        float newGap = MathF.Sqrt(DistanceSquaredToSegment(cloth,wrist,fingertip,out float wristT))
            - (.055f+(.010f-.055f)*wristT);
        Check(oldGap > .018f, "old hand capsule should miss glove-back contact");
        Check(newGap < 0f, "new hand capsule should cover glove-back contact");

        // The tip keeps its 10 mm radius; a 40 mm air gap is NOT contact.
        float tipGap = MathF.Sqrt(DistanceSquaredToSegment(new(.04f,0f,.17f),wrist,fingertip,out float tipT))
            - (.055f+(.010f-.055f)*tipT);
        Check(tipGap > .018f, "taper must reject false fingertip hover");

        // Cloth triangles collapse near a pin/table lip. The nearest point
        // remains finite, including a point triangle and a collinear triangle.
        float point = DistanceSquaredSegmentTriangle(new(.02f,.03f,.04f),new(.02f,.03f,.04f),
            new(0f,0f,0f),new(0f,0f,0f),new(0f,0f,0f),out _);
        float line = DistanceSquaredSegmentTriangle(new(.02f,.03f,.04f),new(.02f,.03f,.04f),
            new(0f,0f,0f),new(.1f,0f,0f),new(.2f,0f,0f),out _);
        Check(float.IsFinite(point) && point > 0f, "collapsed triangle must stay finite");
        Check(float.IsFinite(line) && line > 0f, "collinear triangle must stay finite");

        // Bound the fallback: a nearby capsule considers only neighbouring
        // grid cells; a distant probe has no detailed triangle work at all.
        Vector3[] sheet = new Vector3[13*25];
        for(int row=0;row<25;row++) for(int column=0;column<13;column++)
            sheet[row*13+column]=new(-.75f+column*.125f,.95f-row*.02f,0f);
        int nearCandidates=0, farCandidates=0;
        Vector3 nearLower=new(-.02f,.61f,-.075f), nearUpper=new(.145f,.79f,.075f);
        Vector3 farLower=new(-.02f,.61f,.20f), farUpper=new(.145f,.79f,.35f);
        Stopwatch watch=Stopwatch.StartNew();
        for(int repetition=0;repetition<2000;repetition++)
        for(int row=0;row<24;row++) for(int column=0;column<12;column++)
        {
            int i=row*13+column;
            Vector3 tl=sheet[i],tr=sheet[i+1],bl=sheet[i+13],br=sheet[i+14];
            if(OverlapsProbeBox(tl,tr,bl,br,nearLower,nearUpper)) nearCandidates++;
            if(OverlapsProbeBox(tl,tr,bl,br,farLower,farUpper)) farCandidates++;
        }
        watch.Stop();
        Check(nearCandidates/2000<=50, "near fallback must be spatially bounded");
        Check(farCandidates==0, "far probe must skip all detailed triangles");
        Console.WriteLine($"production_geometry=PASS assertions={assertions} " +
            $"near_cells={nearCandidates/2000}/288 far_cells={farCandidates/2000}/288 " +
            $"broadphase_us_per_probe={watch.Elapsed.TotalMilliseconds*1000/2000/2:F2}");
    }
}
'''


def main():
    source = SOURCE.read_text()
    bodies = '\n\n'.join(extract_method(source, name).replace('private static ', 'internal static ', 1)
                         for name in METHODS)
    with tempfile.TemporaryDirectory(prefix='ghvr-town-cloth-geometry-') as folder:
        project = Path(folder)
        (project / 'Program.cs').write_text(SHIM + bodies + '\n}\n' + 'static class Program\n{\n' + MAIN)
        (project / 'TownClothGeometry.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
</Project>\n''')
        (project / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>\n')
        environment = os.environ.copy()
        environment['DOTNET_CLI_HOME'] = os.environ.get('DOTNET_CLI_HOME',
                                                      '/tmp/ghvr-town-cloth-dotnet')
        environment['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
        environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
        environment['DOTNET_NOLOGO'] = '1'
        dotnet = Path('/home/claw/.dotnet/dotnet')
        subprocess.run([str(dotnet), 'run', '--project', str(project / 'TownClothGeometry.csproj'),
                        '--configuration', 'Release', '--nologo'], check=True, env=environment)


if __name__ == '__main__':
    main()
