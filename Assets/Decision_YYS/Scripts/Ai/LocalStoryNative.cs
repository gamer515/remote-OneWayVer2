using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public enum LocalStoryTestModel { Qwen, Gemma }

/// <summary>Installed model adapters shared by tests and the real next-run pipeline. No downloads.</summary>
public static class LocalStoryTestModels
{
    public const string QwenFile = "Qwen3-4B-Instruct-2507-Q4_K_M.gguf";
    public const string GemmaFile = "gemma-3-4b-it-q4_0.gguf";
    public const long GemmaBytes = 3155051328;
    public const string GemmaSha256 = "76aed0a8285b83102f18b5d60e53c70d09eb4e9917a20ce8956bd546452b56e2";
    private static string verifiedGemmaStamp;
    public static string Name(LocalStoryTestModel model) => model == LocalStoryTestModel.Qwen ?
        "Qwen3-4B-Instruct-2507 Q4_K_M" : model == LocalStoryTestModel.Gemma ? "Gemma 3 4B IT QAT Q4_0" :
        throw new ArgumentOutOfRangeException(nameof(model));
    public static string PathFor(string root, LocalStoryTestModel model)
    {
        if (model == LocalStoryTestModel.Qwen) return Path.Combine(root, QwenFile);
        if (model != LocalStoryTestModel.Gemma) throw new ArgumentOutOfRangeException(nameof(model));
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(root, "..", "..", "..", "Tests", "LocalStoryBenchmark", "Models", GemmaFile));
#else
        throw new PlatformNotSupportedException("Gemma는 현재 Unity Editor에서만 사용할 수 있습니다. 배포 빌드에는 포함되지 않아 원문을 사용합니다. 자동으로 Qwen으로 전환하지 않습니다.");
#endif
    }
    // Gemma's existing benchmark adapter: preserve instructions exactly, merge system/user for its chat template.
    public static string Messages(LocalStoryTestModel model, string messages)
    {
        if (model == LocalStoryTestModel.Qwen) return messages;
        if (model != LocalStoryTestModel.Gemma) throw new ArgumentOutOfRangeException(nameof(model));
        var input = JArray.Parse(messages);
        if (input.Count != 2 || (string)input[0]["role"] != "system" || (string)input[1]["role"] != "user" ||
            input[0]["content"]?.Type != JTokenType.String || input[1]["content"]?.Type != JTokenType.String)
            throw new InvalidOperationException("Gemma 메시지 구성 확인 실패. 다른 모델로 대체하지 않습니다.");
        return new JArray(new JObject { ["role"] = "user", ["content"] =
            (string)input[0]["content"] + "\n\n" + (string)input[1]["content"] }).ToString(Formatting.None);
    }
    // Called inside the single inference gate. Verify the user-installed official file before native loading.
    public static void VerifyGemma(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Gemma 파일이 없습니다. Tests/LocalStoryBenchmark/Models에 직접 설치한 모델을 넣으세요. Qwen으로 대체하지 않습니다.", path);
        if (file.Length != GemmaBytes) throw new InvalidOperationException("Gemma 모델 크기가 일치하지 않습니다. Qwen으로 대체하지 않습니다.");
        string stamp = file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
        if (stamp == verifiedGemmaStamp) return;
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        { token.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, read, buffer, 0); }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        string hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        if (hash != GemmaSha256) throw new InvalidOperationException("Gemma 모델 SHA256 검증 실패. Qwen으로 대체하지 않습니다.");
        verifiedGemmaStamp = stamp;
    }
}

/// <summary>Code-only test presets. Recommended values come from the Qwen model card; proofreading is experimental.</summary>
public sealed class LocalStorySampling
{
    public string Name { get; }
    private readonly string values;
    private LocalStorySampling(string name, string values) { Name=name; this.values=values; }
    public static LocalStorySampling Legacy() => new LocalStorySampling("기존", "\"temperature\":0.65,\"top_p\":0.9");
    public static LocalStorySampling Recommended() => new LocalStorySampling("Qwen 권장", "\"temperature\":0.7,\"top_p\":0.8,\"top_k\":20,\"min_p\":0");
    public static LocalStorySampling Comparison() => new LocalStorySampling("공통 비교 (Qwen 권장 값)", Recommended().values);
    public static LocalStorySampling Proofreading() => new LocalStorySampling("교정 시험", "\"temperature\":0.25,\"top_p\":0.8,\"top_k\":20,\"min_p\":0");
    public string Parameters(int seed) => "{\"n_predict\":1800,"+values+",\"seed\":"+seed+",\"cache_prompt\":false}";
}

/// <summary>Windows x64 embedded LlamaLib 2.0.5. No process, HTTP client or server.</summary>
public sealed class LocalStoryNative : IDisposable
{
    private IntPtr library, service;
    public string Backend { get; private set; }
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32")] private static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Construct(IntPtr path, int slots, int threads, int gpu,
        [MarshalAs(UnmanagedType.I1)] bool flash, int context, int batch, [MarshalAs(UnmanagedType.I1)] bool embedding, int loras, IntPtr loraPaths);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ServiceCall(IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetString(IntPtr instance, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr StringCall(IntPtr instance, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CancelCall(IntPtr instance, int slot);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StreamCall(IntPtr text);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CompletionCall(IntPtr instance, IntPtr prompt, StreamCall callback, int slot,
        [MarshalAs(UnmanagedType.I1)] bool responseJson);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StatusCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr MessageCall();
    private ServiceCall stop, delete;
    private SetString grammar, parameters;
    private StringCall template;
    private CompletionCall completion;
    private CancelCall cancel;
    private StatusCall status;
    private MessageCall message;
    private T Bind<T>(string name) where T : Delegate
    {
        IntPtr ptr = GetProcAddress(library, name);
        if (ptr == IntPtr.Zero) throw new EntryPointNotFoundException(name);
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    // All native char* are UTF-8, including Korean paths. LPStr uses the Windows code page.
    private sealed class Utf8 : IDisposable
    {
        public IntPtr Pointer { get; }
        public Utf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes((value ?? "") + "\0");
            Pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, Pointer, bytes.Length);
        }
        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
    public static string ReadUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "";
        int length = 0;
        while (Marshal.ReadByte(ptr, length) != 0) length++;
        var bytes = new byte[length];
        Marshal.Copy(ptr, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    public LocalStoryNative(string runtimeDirectory, string modelPath, bool gpu)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException("현재 내장 LLM 테스트는 Windows x64용입니다.");
        if (!File.Exists(modelPath)) throw new FileNotFoundException("로컬 모델이 없습니다. SetupLocalStoryModel.ps1을 실행하세요.", modelPath);
        string[] files = Directory.GetFiles(runtimeDirectory, "*.dll", SearchOption.AllDirectories);
        string selected = Array.Find(files, p => Path.GetFileName(p).Contains(gpu ? "tinyblas" : "avx2") && Path.GetFileName(p).StartsWith("llamalib"));
        if (selected == null) throw new FileNotFoundException("로컬 추론 DLL이 없습니다.");
        // SEARCH_DLL_LOAD_DIR + DEFAULT_DIRS: no PATH/current-directory DLL injection.
        library = LoadLibraryEx(Path.GetFullPath(selected), IntPtr.Zero, 0x100 | 0x1000);
        if (library == IntPtr.Zero) throw new InvalidOperationException("내장 DLL 로드 실패: " + Marshal.GetLastWin32Error());
        try
        {
            status = Bind<StatusCall>("LLM_Status_Code"); message = Bind<MessageCall>("LLM_Status_Message");
            stop = Bind<ServiceCall>("LLM_Stop"); delete = Bind<ServiceCall>("LLM_Delete");
            grammar = Bind<SetString>("LLM_Set_Grammar"); parameters = Bind<SetString>("LLM_Set_Completion_Parameters");
            template = Bind<StringCall>("LLM_Apply_Template"); completion = Bind<CompletionCall>("LLM_Completion");
            cancel = Bind<CancelCall>("LLM_Cancel");
            using (var path = new Utf8(modelPath))
                service = Bind<Construct>("LLMService_Construct")(path.Pointer, 1, 6, gpu ? 24 : 0, false, 4096, 256, false, 0, IntPtr.Zero);
            Check();
            if (service == IntPtr.Zero) throw new InvalidOperationException("내장 모델 초기화 실패");
            Bind<ServiceCall>("LLM_Start")(service); Check();
            Backend = gpu ? "CUDA tinyBLAS (24 layers)" : "CPU AVX2";
        }
        catch { Dispose(); throw; }
    }

    public string Generate(string messagesJson, string outputGrammar, int seed, CancellationToken token, Action<string> progress, LocalStorySampling sampling = null)
    {
        token.ThrowIfCancellationRequested();
        using (var p = new Utf8((sampling ?? LocalStorySampling.Legacy()).Parameters(seed))) parameters(service, p.Pointer);
        using (var g = new Utf8(outputGrammar)) grammar(service, g.Pointer);
        Check();
        string prompt;
        using (var m = new Utf8(messagesJson)) prompt = ReadUtf8(template(service, m.Pointer));
        Check();
        StreamCall callback = ptr => { progress?.Invoke(ReadUtf8(ptr)); };
        using (token.Register(() => cancel(service, 0)))
        using (var p = new Utf8(prompt))
        {
            string output = ReadUtf8(completion(service, p.Pointer, callback, 0, false));
            GC.KeepAlive(callback);
            token.ThrowIfCancellationRequested(); Check(); return output;
        }
    }
    private void Check()
    {
        if (status != null && status() < 0) throw new InvalidOperationException(ReadUtf8(message()));
    }
    public void Dispose()
    {
        if (service != IntPtr.Zero) { stop?.Invoke(service); delete?.Invoke(service); service = IntPtr.Zero; }
        if (library != IntPtr.Zero) { FreeLibrary(library); library = IntPtr.Zero; }
    }
}

/// <summary>One generation at a time. Owns native lifetime entirely on a worker thread.</summary>
public static class LocalStoryInference
{
    private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
    public static async Task<LocalStoryModelOutput> Generate(string root, string messages, string grammar,
        int seed, CancellationToken token, Action<string> progress, LocalStorySampling sampling = null, string modelPath = null)
    {
        // Existing callers keep Qwen. Only explicitly selected test callers supply another installed model.
        string selectedModel = modelPath ?? Path.Combine(root, LocalStoryTestModels.QwenFile);
        using var local = CancellationTokenSource.CreateLinkedTokenSource(token);
        LocalStoryLifetime.Register(local);
        bool acquired = false;
        try
        {
            token = local.Token;
            await Gate.WaitAsync(token).ConfigureAwait(false); acquired = true;
            var worker = Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                string runtime = Path.Combine(root, "runtimes", "win-x64", "native");
                if (Path.GetFileName(selectedModel) == LocalStoryTestModels.GemmaFile)
                    LocalStoryTestModels.VerifyGemma(selectedModel, token);
                LocalStoryNative engine;
                string fallback = null;
                try { engine = new LocalStoryNative(runtime, selectedModel, true); }
                catch (Exception e) when (!(e is FileNotFoundException))
                { token.ThrowIfCancellationRequested(); fallback = e.Message; engine = new LocalStoryNative(runtime, selectedModel, false); }
                using (engine)
                    return new LocalStoryModelOutput { json = engine.Generate(messages, grammar, seed, token, progress, sampling), backend = engine.Backend, fallback = fallback };
            }, token);
            LocalStoryLifetime.Track(worker);
            return await worker.ConfigureAwait(false);
        }
        finally { if(acquired) Gate.Release(); LocalStoryLifetime.Unregister(local); }
    }
}
public sealed class LocalStoryModelOutput { public string json, backend, fallback; }

public static class LocalStoryLifetime
{
    private static readonly object Sync = new object();
    private static readonly System.Collections.Generic.List<Task> Workers = new System.Collections.Generic.List<Task>();
    private static readonly System.Collections.Generic.HashSet<CancellationTokenSource> Requests = new System.Collections.Generic.HashSet<CancellationTokenSource>();
    public static void Register(CancellationTokenSource source) { lock(Sync) Requests.Add(source); }
    public static void Unregister(CancellationTokenSource source) { lock(Sync) Requests.Remove(source); }
    public static void CancelAndWait()
    {
        lock(Sync) foreach(var request in Requests) request.Cancel();
        WaitForWorkers();
    }
    public static void Track(Task task)
    {
        lock(Sync) Workers.Add(task);
        task.ContinueWith(t=> { lock(Sync) Workers.Remove(t); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    public static void WaitForWorkers()
    {
        Task[] tasks; lock(Sync) tasks=Workers.ToArray();
        try { Task.WaitAll(tasks); } catch(AggregateException) { /* cancelled/failed work already finished */ }
    }
}
