using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// No model loads or player-save writes. May also be compiled outside Unity.
public static class LocalStoryNativePathCheck
{
    public static string Run()
    {
        int checks = 0;
        Action<bool, string> check = (ok, reason) =>
        { if (!ok) throw new Exception(reason); checks++; };
        string root = Path.Combine(Path.GetTempPath(), "한글 사용자 😀", "바탕 화면", "게임 프로젝트", "Assets", "StreamingAssets", "LocalStory");
        foreach (bool gpu in new[] { true, false })
        foreach (LocalStoryTestModel model in new[] { LocalStoryTestModel.Gemma, LocalStoryTestModel.Qwen })
        {
            string modelPath = LocalStoryTestModels.PathFor(root, model);
            string command = LocalStoryNative.InitializationCommand(modelPath, gpu);
            // Parse using LlamaLib 2.0.5's quote/space rules, not a shell's rules.
            var args = new List<string>();
            string[] segments = command.Split('"');
            for (int i = 0; i < segments.Length; i++)
                if ((i & 1) == 1) args.Add(segments[i]);
                else args.AddRange(segments[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            check(args[0] == "-m" && args[1] == Path.GetFullPath(modelPath), "Unicode/spaces changed the model path");
            check(args[2] == "--alias" && args[3] == "LocalStory", "Native ANSI filename conversion was not bypassed");
            check(args[13] == (gpu ? "24" : "0"), "GPU/CPU layer count changed");
            check(string.Join(" ", args.GetRange(4, args.Count - 4)) ==
                "-t 6 -np 1 -c 4096 -b 256 -ngl " + (gpu ? "24" : "0") + " --context-shift -fa off", "Existing inference settings changed");
            check(modelPath.StartsWith(Path.GetFullPath(Path.Combine(root, "..", "..", "..")), StringComparison.Ordinal), "Model points outside the current project");
        }
        foreach (string bad in new[] { "", " ", "model\".gguf", "model\n.gguf", "model\r.gguf", "model\0.gguf" })
        {
            bool rejected = false;
            try { LocalStoryNative.InitializationCommand(bad, false); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "Unsafe native argument accepted");
        }
        // Exercise the production UTF-8 allocator as well as the returned native char* reader.
        var utf8 = typeof(LocalStoryNative).GetNestedType("Utf8", BindingFlags.NonPublic);
        string text = root + " / 로키: 자네, 안녕! 😀";
        using (var buffer = (IDisposable)Activator.CreateInstance(utf8, new object[] { text }))
        {
            var pointer = (IntPtr)utf8.GetProperty("Pointer").GetValue(buffer);
            check(LocalStoryNative.ReadUtf8(pointer) == text, "Native UTF-8 round trip corrupted Unicode");
            var bytes = Encoding.UTF8.GetBytes(text);
            check(Marshal.ReadByte(pointer, bytes.Length) == 0, "Missing native string terminator");
        }
        check(LocalStoryNative.ReadUtf8(IntPtr.Zero) == "", "Null native response changed");
        bool missing = false;
        try { LocalStoryTestModels.VerifyGemma(Path.Combine(root, "missing.gguf"), CancellationToken.None); }
        catch (FileNotFoundException) { missing = true; }
        check(missing, "Missing per-device Gemma was silently substituted");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); bool cancelled = false;
            try { LocalStoryTestModels.VerifyGemma("missing.gguf", cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Model verification ignored cancellation");
        }
        return checks + " checks passed: Unicode/spaces, native alias/settings, UTF-8 pointers, project-relative model paths, missing model and cancellation. No native inference executed.";
    }
}
