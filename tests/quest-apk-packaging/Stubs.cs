namespace UnityEditor.Android
{
    public interface IPostGenerateGradleAndroidProject
    {
        int callbackOrder { get; }
        void OnPostGenerateGradleAndroidProject(string path);
    }
}
namespace UnityEngine
{
    public static class Debug { public static void Log(object value) { } }
}
