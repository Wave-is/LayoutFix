using System.Reflection;
using System.Runtime.Loader;

namespace LayoutFix.Services;

internal static class TranslationWorkerLauncher
{
    public static async Task<int> RunAsync(string pipeName, string? modelType)
    {
        var workerAssemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "translation-worker",
            "LayoutFix.TranslationWorker.dll");
        if (!File.Exists(workerAssemblyPath)) return 4;

        var loadContext = new WorkerLoadContext(workerAssemblyPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(workerAssemblyPath);
            var hostType = assembly.GetType(
                "LayoutFix.TranslationWorker.OfflineTranslationWorkerHost",
                throwOnError: true)!;
            var runMethod = hostType.GetMethod(
                "RunAsync",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), typeof(string)],
                modifiers: null)
                ?? throw new MissingMethodException(hostType.FullName, "RunAsync");
            var task = runMethod.Invoke(null, [pipeName, modelType]) as Task<int>
                ?? throw new InvalidOperationException("Translation worker returned an invalid task.");
            return await task;
        }
        catch
        {
            return 1;
        }
    }

    private sealed class WorkerLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public WorkerLoadContext(string componentAssemblyPath)
            : base("LayoutFix.TranslationWorker", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(componentAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? 0 : LoadUnmanagedDllFromPath(path);
        }
    }
}
