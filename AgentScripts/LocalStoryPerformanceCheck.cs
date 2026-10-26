using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Real Windows hash equivalence and model preparation. Never creates or edits player saves.</summary>
public static class LocalStoryPerformanceCheck
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Reference(byte[] bytes)
    {
        using var sha=SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
    }
    private sealed class ShortReadStream : MemoryStream
    {
        private readonly int maxRead;
        private readonly Action onRead;
        public ShortReadStream(byte[] data,int maxRead,Action onRead=null):base(data) {this.maxRead=maxRead;this.onRead=onRead;}
        public override int Read(byte[] buffer,int offset,int count)
        {int read=base.Read(buffer,offset,Math.Min(count,maxRead));onRead?.Invoke();return read;}
    }
    public static object Regression()
    {
        int checks=0;
        void Assert(bool value,string message) {Check(value,message);checks++;}
        var random=new System.Random(37191);
        foreach(int length in new[]{0,1,3,55,56,63,64,65,4097,1024*1024-1,1024*1024,1024*1024+37,3*1024*1024+11})
        {
            var bytes=new byte[length];random.NextBytes(bytes);
            using var data=new MemoryStream(bytes);
            Assert(LocalStoryModelHash.Compute(data,CancellationToken.None)==Reference(bytes),"Windows SHA256 mismatch at "+length);
            Assert(data.CanRead,"Hash helper took ownership of caller stream");
        }
        byte[] text=Encoding.UTF8.GetBytes("로키: 자네, 안녕! 😀 abc");
        using(var data=new ShortReadStream(text,7))
            Assert(LocalStoryModelHash.Compute(data,CancellationToken.None)==Reference(text),"Short-read or UTF8 bytes were lost");
        using(var data=new MemoryStream(Encoding.ASCII.GetBytes("abc")))
            Assert(LocalStoryModelHash.Compute(data,CancellationToken.None)=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","Known SHA256 vector differs");
        using(var data=new MemoryStream(text))
            Assert(LocalStoryModelHash.Compute(data,CancellationToken.None,false)==Reference(text),"Portable managed path differs");
        using(var stop=new CancellationTokenSource())
        {
            stop.Cancel();bool cancelled=false;
            try {LocalStoryModelHash.FileHash("missing.gguf",stop.Token);} catch(OperationCanceledException) {cancelled=true;}
            Assert(cancelled,"Cancelled hash touched the file");
            cancelled=false;
            try {LocalStoryInference.PrepareGemma("missing.gguf",stop.Token).GetAwaiter().GetResult();} catch(OperationCanceledException) {cancelled=true;}
            Assert(cancelled,"Cancelled preparation entered the native gate or touched the model");
        }
        using(var stop=new CancellationTokenSource())
        using(var data=new ShortReadStream(new byte[1024*1024+7],17,()=>stop.Cancel()))
        {
            bool cancelled=false;
            try {LocalStoryModelHash.Compute(data,stop.Token);} catch(OperationCanceledException) {cancelled=true;}
            Assert(cancelled,"Streaming cancellation was ignored");
        }
        using(var data=new ShortReadStream(text,7,()=>throw new IOException("injected read failure")))
        {
            bool failed=false;
            try {LocalStoryModelHash.Compute(data,CancellationToken.None);} catch(IOException) {failed=true;}
            Assert(failed,"Read failure was hidden or a partial digest was returned");
        }
        string folder=Path.GetFullPath("tmp/LocalStoryPerformance");Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,"한글 경로 😀.bin");
        File.WriteAllBytes(file,text);
        try {Assert(LocalStoryModelHash.FileHash(file)==Reference(text),"Unicode file hash differs");}
        finally {File.Delete(file);}
        using(var data=new MemoryStream(text))
            Assert(LocalStoryModelHash.Compute(data,CancellationToken.None)==Reference(text),"Hashing did not recover after cancellation/read failure");
        return new {passed=true,checks,provider=LocalStoryModelHash.ProviderName,fullStreaming=true,realSavesUntouched=true};
    }
    public static async Task<object> NativeSmoke()
    {
        Check(!Application.isPlaying&&!LocalStoryRuntimeService.IsBusy,"Stop gameplay before native performance checks.");
        LocalStoryLifetime.CancelAndWait();
        string root=Path.Combine(Application.streamingAssetsPath,"LocalStory");
        string path=LocalStoryTestModels.PathFor(root,LocalStoryTestModel.Gemma);
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        int ticks=0;void Tick() {ticks++;} EditorApplication.update+=Tick;
        var result=new JObject { ["passed"]=false,["model"]="Gemma",["realSavesUntouched"]=true };
        try
        {
            // Always hash all bytes, even if another check already filled the in-memory verification cache.
            var fullWatch=System.Diagnostics.Stopwatch.StartNew();
            string hash=await Task.Run(()=>LocalStoryModelHash.FileHash(path,stop.Token));
            double fullSeconds=fullWatch.Elapsed.TotalSeconds;
            Check(hash==LocalStoryTestModels.GemmaSha256,"Installed Gemma checksum differs");
            Check(ticks>0,"Editor did not tick while hashing");
            result["freshFullFileHashSeconds"]=fullSeconds;result["bytesHashed"]=new FileInfo(path).Length;
            result["sha256"]=hash;result["editorTicksWhileHashing"]=ticks;
            var prepared=await LocalStoryInference.PrepareGemma(path,stop.Token);
            result["preparation"]=JObject.Parse(JsonUtility.ToJson(prepared));
            Check(!LocalStoryInference.HasResidentModel,"Preparation allocated a model or GPU memory");
            var repeated=await Task.WhenAll(Enumerable.Range(0,3).Select(_=>LocalStoryInference.PrepareGemma(path,stop.Token)));
            Check(repeated.All(v=>v.cached&&v.bytesHashed==0),"Repeated preparation rehashed the unchanged model");
            result["preparationModelResident"]=LocalStoryInference.HasResidentModel;
            string messages=LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,
                "[{\"role\":\"system\",\"content\":\"지정된 JSON만 출력한다.\"},{\"role\":\"user\",\"content\":\"연결 확인\"}]");
            var first=await LocalStoryInference.Generate(root,messages,"root ::= \"{\\\"step\\\":1}\"",12345,stop.Token,null,LocalStorySampling.Comparison(),path);
            var second=await LocalStoryInference.Generate(root,messages,"root ::= \"{\\\"step\\\":2}\"",12345,stop.Token,null,LocalStorySampling.Comparison(),path);
            Check(first.json.Trim()=="{\"step\":1}"&&second.json.Trim()=="{\"step\":2}","Actual Gemma output differs");
            Check(first.verificationCached&&first.verificationBytes==0,"Inference ignored menu preparation");
            Check(second.sessionReused&&second.modelLoadSeconds==0,"Second request reloaded the model");
            result["first"]=JObject.Parse(JsonUtility.ToJson(first));result["second"]=JObject.Parse(JsonUtility.ToJson(second));
            result["passed"]=true;
        }
        catch(Exception e) {result["error"]=e.ToString();}
        finally {EditorApplication.update-=Tick;LocalStoryLifetime.CancelAndWait();}
        string folder=Path.GetFullPath("tmp/LocalStoryPerformance");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Result.json"),result.ToString());
        return result;
    }
}
