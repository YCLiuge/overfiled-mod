using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using AssetStudio;

var root = Path.GetFullPath(args[0]);
var ffmpeg = args[1];
var limit = args.Length>2 ? int.Parse(args[2]) : int.MaxValue;
NativeLibrary.SetDllImportResolver(typeof(FMOD.System).Assembly, (name,asm,paths) =>
    name.Contains("fmod",StringComparison.OrdinalIgnoreCase) ? NativeLibrary.Load(@"C:\Users\86156\Desktop\工具\AssetStudio.net6.0-windows_v0.16.53\x64\fmod.dll") : IntPtr.Zero);
var rows = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(root,"disk-map.json")));
var results = new List<object>();
Directory.CreateDirectory(Path.Combine(root,"tracks"));
foreach(var row in rows.Take(limit)) {
    int index = row.GetProperty("index").GetInt32();
    string bundle = row.GetProperty("bundle").GetString();
    var manager = new AssetsManager();
    try {
        string path = row.GetProperty("path").GetString();
        if(string.IsNullOrEmpty(path)) throw new Exception("No disk path");
        var logOutput = Console.Out;
        try { Console.SetOut(TextWriter.Null); manager.LoadFiles(new[]{path}); }
        finally { Console.SetOut(logOutput); }
        var clips=manager.assetsFileList.SelectMany(f=>f.Objects).OfType<AudioClip>().ToArray();
        if(clips.Length==0) throw new Exception("No AudioClip objects found");
        foreach(var clip in clips) {
            var data = new AudioClipConverter(clip).ConvertToWav();
            if(data==null || data.Length<44) throw new Exception("Decoder returned no WAV data");
            using var reader = new BinaryReader(new MemoryStream(data));
            if(new string(reader.ReadChars(4))!="RIFF") throw new Exception("Not RIFF");
            reader.ReadUInt32(); if(new string(reader.ReadChars(4))!="WAVE") throw new Exception("Not WAVE");
            int rate=0,channels=0,bits=0,format=0,offset=0,size=0;
            while(reader.BaseStream.Position+8<=data.Length) {
                string tag=new string(reader.ReadChars(4)); int len=reader.ReadInt32(); long end=reader.BaseStream.Position+len;
                if(end>data.Length || len<0) throw new Exception("Invalid WAV chunk");
                if(tag=="fmt ") {format=reader.ReadInt16();channels=reader.ReadInt16();rate=reader.ReadInt32();reader.ReadInt32();reader.ReadInt16();bits=reader.ReadInt16();}
                if(tag=="data") {offset=(int)reader.BaseStream.Position;size=len;}
                reader.BaseStream.Position=end+(len&1);
            }
            if(rate<=0||channels<=0||size<=0||format!=1||bits!=16) throw new Exception($"Unexpected PCM {format}/{bits}");
            long nonzero=0;int peak=0;double sq=0;
            for(int p=offset;p<offset+size;p+=2) {int v=BitConverter.ToInt16(data,p);if(v!=0)nonzero++;peak=Math.Max(peak,Math.Abs(v));sq+=(double)v*v;}
            if(nonzero==0) throw new Exception("Audio is entirely silent");
            string safe=string.Join("_",clip.m_Name.Split(Path.GetInvalidFileNameChars()));
            string stem=$"{index:D3}_{safe}";
            string wav=Path.Combine(root,"tracks",stem+".wav"),flac=Path.Combine(root,"tracks",stem+".flac");
            File.WriteAllBytes(wav,data);
            var start=new ProcessStartInfo(ffmpeg){UseShellExecute=false};
            foreach(var a in new[]{"-hide_banner","-loglevel","error","-nostdin","-y","-i",wav,"-c:a","flac",flac})start.ArgumentList.Add(a);
            using(var process=Process.Start(start)){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("FLAC encode failed");}
            // Delete only the exact temporary WAV just written, inside the verified tracks directory.
            if(Path.GetDirectoryName(Path.GetFullPath(wav))!=Path.GetFullPath(Path.Combine(root,"tracks")))throw new Exception("Invalid WAV path");
            File.Delete(wav);
            results.Add(new{index,bundle,name=clip.m_Name,file=Path.GetFileName(flac),seconds=(double)size/(rate*channels*2),rate,channels,bits,peak,rms=Math.Sqrt(sq/(size/2))/32768,nonzero,sourceCompression=clip.m_CompressionFormat.ToString(),bytes=new FileInfo(flac).Length,status="ok"});
            Console.WriteLine($"OK {index:D3}: {clip.m_Name} ({(double)size/(rate*channels*2):F1}s)");
        }
    } catch(Exception ex) {results.Add(new{index,bundle,status="failed",error=ex.ToString()});Console.WriteLine($"FAILED {index}: {ex.Message}");}
    finally {manager.Clear();GC.Collect();}
    File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
}
