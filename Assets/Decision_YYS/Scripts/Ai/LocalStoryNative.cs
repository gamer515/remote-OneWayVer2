using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private static readonly object VerificationSync = new object();
    public static string Name(LocalStoryTestModel model) => model == LocalStoryTestModel.Qwen ?
        "Qwen3-4B-Instruct-2507 Q4_K_M" : model == LocalStoryTestModel.Gemma ? "Gemma 3 4B IT QAT Q4_0" :
        throw new ArgumentOutOfRangeException(nameof(model));
    public static string PathFor(string root, LocalStoryTestModel model)
    {
        if (model != LocalStoryTestModel.Gemma && model!=LocalStoryTestModel.Qwen) throw new ArgumentOutOfRangeException(nameof(model));
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(root, "..", "..", "..", "Tests", "LocalStoryBenchmark", "Models", model==LocalStoryTestModel.Gemma?GemmaFile:QwenFile));
#else
        return PackagedPath(root, model);
#endif
    }
    public static string PackagedPath(string root, LocalStoryTestModel model) => model == LocalStoryTestModel.Gemma ?
        Path.Combine(root, GemmaFile) : model == LocalStoryTestModel.Qwen ? Path.Combine(root, QwenFile) :
        throw new ArgumentOutOfRangeException(nameof(model));
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
    // Preparation and inference share a process-local cache; every new process still hashes the entire file.
    public static LocalStoryModelVerification VerifyGemma(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        lock (VerificationSync)
        {
            token.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("선택한 Gemma 파일이 없습니다. 빌드의 StreamingAssets/LocalStory 모델 패키지를 확인하세요. Editor에서는 Tests/LocalStoryBenchmark/Models를 사용합니다. Qwen으로 대체하지 않습니다.", path);
            if (file.Length != GemmaBytes) throw new InvalidOperationException("Gemma 모델 크기가 일치하지 않습니다. Qwen으로 대체하지 않습니다.");
            string stamp = file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            bool cached = stamp == verifiedGemmaStamp;
            if (!cached)
            {
                string hash = LocalStoryModelHash.FileHash(path, token);
                if (hash != GemmaSha256) throw new InvalidOperationException("Gemma 모델 SHA256 검증 실패. Qwen으로 대체하지 않습니다.");
                file.Refresh();
                if (!file.Exists || file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks != stamp)
                    throw new IOException("검사 중 Gemma 파일이 바뀌었습니다. 다시 검사해야 합니다.");
                verifiedGemmaStamp = stamp;
            }
            return new LocalStoryModelVerification { provider = LocalStoryModelHash.ProviderName, modelPath = file.FullName,
                cached = cached, bytesHashed = cached ? 0 : file.Length, seconds = watch.Elapsed.TotalSeconds };
        }
    }
}

/// <summary>Code-only test presets. Recommended values come from the Qwen model card; proofreading is experimental.</summary>
public sealed class LocalStorySampling
{
    public string Name { get; }
    private readonly string values;
    private readonly int maxTokens;
    private LocalStorySampling(string name, string values, int maxTokens = 1800) { Name=name; this.values=values; this.maxTokens=maxTokens; }
    public LocalStorySampling WithTokenLimit(int limit) => new LocalStorySampling(Name, values, Math.Max(128, Math.Min(1800, limit)));
    public static LocalStorySampling Legacy() => new LocalStorySampling("기존", "\"temperature\":0.65,\"top_p\":0.9");
    public static LocalStorySampling Recommended() => new LocalStorySampling("Qwen 권장", "\"temperature\":0.7,\"top_p\":0.8,\"top_k\":20,\"min_p\":0");
    public static LocalStorySampling Comparison() => new LocalStorySampling("공통 비교 (Qwen 권장 값)", Recommended().values);
    public static LocalStorySampling Proofreading() => new LocalStorySampling("교정 시험", "\"temperature\":0.25,\"top_p\":0.8,\"top_k\":20,\"min_p\":0");
    public string Parameters(int seed) => "{\"n_predict\":"+maxTokens+","+values+",\"seed\":"+seed+",\"cache_prompt\":true}";
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
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ConstructFromCommand(IntPtr command);
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
    private string modelFile, runtimeFile;
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

    public static string InitializationCommand(string modelPath, bool gpu, JObject configuration=null)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || modelPath.IndexOfAny(new[] { '"', '\0', '\r', '\n' }) >= 0)
            throw new ArgumentException("모델 경로가 비어 있거나 지원하지 않는 문자를 포함합니다.", nameof(modelPath));
        // LlamaLib 2.0.5 derives an unnamed model's name through std::filesystem::path
        // and the Windows ANSI code page. An explicit ASCII alias skips that conversion.
        // Keep the actual path UTF-8: llama.cpp's model file reader supports Unicode.
        // This is an in-process C API argument string, never a shell/process command.
        var settings=configuration??LocalStoryPrompt.Policy["inference"] as JObject;
        int threads=(int)settings["cpuThreads"],layers=(int)settings["gpuLayers"],context=(int)settings["contextSize"],batch=(int)settings["batchSize"];
        string flash=(string)settings["flashAttention"];
        if(threads<1 || threads>128 || layers<0 || layers>256 || context<1024 || context>32768 || batch<1 || batch>context ||
            (flash!="on" && flash!="off" && flash!="auto"))throw new InvalidDataException("Invalid PromptPolicy.inference settings.");
        return "-m \"" + Path.GetFullPath(modelPath) + "\" --alias LocalStory" +
            " -t "+threads+" -np 1 -c "+context+" -b "+batch+" -ngl "+(gpu?layers:0)+" --context-shift -fa "+flash;
    }

    public LocalStoryNative(string runtimeDirectory, string modelPath, bool gpu, JObject configuration=null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IntPtr.Size != 8) throw new PlatformNotSupportedException("현재 내장 LLM은 Windows x64용입니다.");
        modelFile = Path.GetFullPath(modelPath);
        if (!File.Exists(modelFile)) throw new FileNotFoundException("로컬 모델이 없습니다. 이 기기에 모델을 설치하세요.", modelFile);
        if (!Directory.Exists(runtimeDirectory)) throw new FileNotFoundException("로컬 추론 DLL 폴더가 없습니다. 이 기기에서 SetupLocalStoryModel.ps1 -Part Runtime을 실행하세요.", runtimeDirectory);
        string[] files = Directory.GetFiles(runtimeDirectory, "*.dll", SearchOption.AllDirectories);
        string selected = Array.Find(files, p => Path.GetFileName(p).Contains(gpu ? "tinyblas" : "avx2") && Path.GetFileName(p).StartsWith("llamalib"));
        if (selected == null) throw new FileNotFoundException("로컬 추론 DLL이 없습니다 (" + (gpu ? "tinyblas" : "avx2") + ").", runtimeDirectory);
        runtimeFile = Path.GetFullPath(selected);
        // SEARCH_DLL_LOAD_DIR + DEFAULT_DIRS: no PATH/current-directory DLL injection.
        library = LoadLibraryEx(runtimeFile, IntPtr.Zero, 0x100 | 0x1000);
        if (library == IntPtr.Zero) throw new InvalidOperationException("[LocalStoryNative/dll-load] Win32=" + Marshal.GetLastWin32Error() + " / DLL=" + runtimeFile);
        try
        {
            status = Bind<StatusCall>("LLM_Status_Code"); message = Bind<MessageCall>("LLM_Status_Message");
            stop = Bind<ServiceCall>("LLM_Stop"); delete = Bind<ServiceCall>("LLM_Delete");
            grammar = Bind<SetString>("LLM_Set_Grammar"); parameters = Bind<SetString>("LLM_Set_Completion_Parameters");
            template = Bind<StringCall>("LLM_Apply_Template"); completion = Bind<CompletionCall>("LLM_Completion");
            cancel = Bind<CancelCall>("LLM_Cancel");
            using (var command = new Utf8(InitializationCommand(modelFile, gpu, configuration)))
                service = Bind<ConstructFromCommand>("LLMService_From_Command")(command.Pointer);
            Check("model-initialize");
            if (service == IntPtr.Zero) throw new InvalidOperationException("[LocalStoryNative/model-initialize] 내장 모델 초기화 실패 / 모델=" + modelFile);
            Bind<ServiceCall>("LLM_Start")(service); Check("service-start");
            Backend = gpu ? "CUDA tinyBLAS (GPU layer limit: "+(int)(configuration??LocalStoryPrompt.Policy["inference"])["gpuLayers"]+")" : "CPU AVX2";
        }
        catch { Dispose(); throw; }
    }

    public string Generate(string messagesJson, string outputGrammar, int seed, CancellationToken token, Action<string> progress, LocalStorySampling sampling = null)
    {
        token.ThrowIfCancellationRequested();
        using (var p = new Utf8((sampling ?? LocalStorySampling.Legacy()).Parameters(seed))) parameters(service, p.Pointer);
        using (var g = new Utf8(outputGrammar)) grammar(service, g.Pointer);
        Check("grammar-and-sampling");
        string prompt;
        using (var m = new Utf8(messagesJson)) prompt = ReadUtf8(template(service, m.Pointer));
        Check("chat-template");
        StreamCall callback = ptr => { progress?.Invoke(ReadUtf8(ptr)); };
        using (token.Register(() => cancel(service, 0)))
        using (var p = new Utf8(prompt))
        {
            string output = ReadUtf8(completion(service, p.Pointer, callback, 0, false));
            GC.KeepAlive(callback);
            token.ThrowIfCancellationRequested(); Check("completion"); return output;
        }
    }
    private void Check(string stage)
    {
        if (status != null && status() < 0) throw new InvalidOperationException(
            "[LocalStoryNative/" + stage + "] " + ReadUtf8(message()) + " / 모델=" + modelFile + " / DLL=" + runtimeFile);
    }
    public void Dispose()
    {
        if (service != IntPtr.Zero) { stop?.Invoke(service); delete?.Invoke(service); service = IntPtr.Zero; }
        if (library != IntPtr.Zero) { FreeLibrary(library); library = IntPtr.Zero; }
    }
}

/// <summary>One native session at a time; consecutive requests reuse the installed model.</summary>
public static class LocalStoryInference
{
    private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
    // All native access, including teardown, is serialized by Gate and runs off the Unity thread.
    private static LocalStoryNative session;
    private static string sessionIdentity, sessionFallback;
    private static long lastUseTicks;
    public static bool HasResidentModel => Volatile.Read(ref session) != null;

    private static string Identity(string runtime, string model)
    {
        var file = new FileInfo(model);
        if (!file.Exists) throw new FileNotFoundException("로컬 모델이 없습니다. 이 기기에 모델을 설치하세요.", model);
        return Path.GetFullPath(runtime) + "|" + file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
    }
    private static void ReleaseUnderGate()
    {
        var previous = session;
        session = null; sessionIdentity = null; sessionFallback = null;
        previous?.Dispose();
    }
    public static void ReleaseSession()
    {
        Gate.Wait();
        try
        {
            // Reload/quit callers may be on Unity's thread. Native shutdown stays on a worker.
            var release = Task.Run((Action)ReleaseUnderGate);
            release.GetAwaiter().GetResult();
        }
        finally { Gate.Release(); }
    }
    public static void ReleaseIfIdle(TimeSpan idleTime)
    {
        if (!HasResidentModel || DateTime.UtcNow.Ticks - Interlocked.Read(ref lastUseTicks) < idleTime.Ticks || !Gate.Wait(0)) return;
        var release = Task.Run(() =>
        {
            try { ReleaseUnderGate(); }
            finally { Gate.Release(); }
        });
        LocalStoryLifetime.Track(release);
    }
    // Only read/verify here. Do not allocate a native model or GPU memory on the menu.
    public static async Task<LocalStoryModelVerification> PrepareGemma(string modelPath, CancellationToken token)
    {
        using var local = CancellationTokenSource.CreateLinkedTokenSource(token);
        LocalStoryLifetime.Register(local);
        bool acquired = false;
        try
        {
            await Gate.WaitAsync(local.Token).ConfigureAwait(false); acquired = true;
            var worker = Task.Run(() => LocalStoryTestModels.VerifyGemma(modelPath, local.Token), local.Token);
            LocalStoryLifetime.Track(worker);
            return await worker.ConfigureAwait(false);
        }
        finally { if (acquired) Gate.Release(); LocalStoryLifetime.Unregister(local); }
    }
    public static async Task<LocalStoryModelOutput> Generate(string root, string messages, string grammar,
        int seed, CancellationToken token, Action<string> progress, LocalStorySampling sampling = null, string modelPath = null, bool reuseSession = true)
    {
        // Existing callers keep Qwen. Only explicitly selected test callers supply another installed model.
        string selectedModel = modelPath ?? Path.Combine(root, LocalStoryTestModels.QwenFile);
        // Load Unity Resources on the caller thread, before entering native/background work.
        var inference=(JObject)LocalStoryPrompt.Policy["inference"].DeepClone();
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
                var verifying = System.Diagnostics.Stopwatch.StartNew();
                LocalStoryModelVerification verification = null;
                if (Path.GetFileName(selectedModel) == LocalStoryTestModels.GemmaFile)
                    verification = LocalStoryTestModels.VerifyGemma(selectedModel, token);
                double verificationSeconds = verifying.Elapsed.TotalSeconds;
                string identity = Identity(runtime, selectedModel)+"|"+inference.ToString(Formatting.None);
                bool reused = reuseSession && session != null && sessionIdentity == identity;
                double loadSeconds = 0;
                if (!reused)
                {
                    ReleaseUnderGate();
                    var loading = System.Diagnostics.Stopwatch.StartNew();
                    try { session = new LocalStoryNative(runtime, selectedModel, true, inference); }
                    catch (Exception e) when (!(e is FileNotFoundException) && !(e is OperationCanceledException) && !(e is PlatformNotSupportedException))
                    {
                        token.ThrowIfCancellationRequested(); sessionFallback = e.Message;
                        try { session = new LocalStoryNative(runtime, selectedModel, false, inference); }
                        catch (Exception cpuError)
                        { throw new InvalidOperationException("GPU 초기화 실패: " + sessionFallback + " / CPU 초기화 실패: " + cpuError.Message, cpuError); }
                    }
                    sessionIdentity = identity;
                    loadSeconds = loading.Elapsed.TotalSeconds;
                }
                var generating = System.Diagnostics.Stopwatch.StartNew();
                double firstTextSeconds = -1;
                try
                {
                    string json = session.Generate(messages, grammar, seed, token, value =>
                    {
                        if (firstTextSeconds < 0 && !string.IsNullOrEmpty(value)) firstTextSeconds = generating.Elapsed.TotalSeconds;
                        progress?.Invoke(value);
                    }, sampling);
                    Interlocked.Exchange(ref lastUseTicks, DateTime.UtcNow.Ticks);
                    var output = new LocalStoryModelOutput { json = json, backend = session.Backend, fallback = sessionFallback,
                        verificationProvider = verification?.provider, verificationCached = verification?.cached ?? false,
                        verificationBytes = verification?.bytesHashed ?? 0,
                        sessionReused = reused, verificationSeconds = verificationSeconds, modelLoadSeconds = loadSeconds, generationSeconds = generating.Elapsed.TotalSeconds,
                        firstTextSeconds = firstTextSeconds };
                    if (!reuseSession) ReleaseUnderGate();
                    return output;
                }
                catch { ReleaseUnderGate(); throw; } // A failed/cancelled native request never poisons the next one.
            }, token);
            LocalStoryLifetime.Track(worker);
            return await worker.ConfigureAwait(false);
        }
        finally { if(acquired) Gate.Release(); LocalStoryLifetime.Unregister(local); }
    }
}
public sealed class LocalStoryModelOutput
{
    public string json, backend, fallback;
    public string verificationProvider;
    public bool verificationCached;
    public long verificationBytes;
    public bool sessionReused;
    public double verificationSeconds, modelLoadSeconds, generationSeconds, firstTextSeconds;
}

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
        LocalStoryInference.ReleaseSession();
    }
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterShutdown()
    {
        UnityEngine.Application.quitting -= CancelAndWait;
        UnityEngine.Application.quitting += CancelAndWait;
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
