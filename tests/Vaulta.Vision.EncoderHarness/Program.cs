using System.Diagnostics;
using System.Text.Json;
using Vaulta.Vision.Encoding;
using Vaulta.Vision.Domain;
if(args.Length<2) throw new ArgumentException("Usage: <manifest> <image-directory>");
using var encoder=new OnnxImageEncoder(args[0]);
// Real runtime also rejects extreme aspect ratios before allocating an enormous resize.
using(var extreme=new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(10,3000))
{
    using var bytes=new MemoryStream(); await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(extreme,bytes); bytes.Position=0;
    try { await encoder.EncodeAsync(bytes,default); throw new InvalidOperationException("Unsafe aspect ratio was accepted."); }
    catch(InvalidDataException) { }
}
var files=Directory.GetFiles(args[1],"*.png").Order(StringComparer.Ordinal).ToArray();
if(files.Length<2) throw new InvalidOperationException("At least two real reference images are required.");
var vectors=new List<float[]>(); var timings=new List<double>();
foreach(var file in files)
{
    await using var input=File.OpenRead(file); var watch=Stopwatch.StartNew(); var encoded=await encoder.EncodeAsync(input,default); watch.Stop();
    if(encoded.Vector.Length!=encoder.Identity.Dimension || Math.Abs(encoded.Vector.Sum(x=>(double)x*x)-1)>0.00001) throw new InvalidOperationException("Invalid real model vector.");
    vectors.Add(encoded.Vector); timings.Add(watch.Elapsed.TotalMilliseconds);
}
var top1=0;
for(var i=0;i<files.Length;i++)
{
    await using var input=File.OpenRead(files[i]); var query=(await encoder.EncodeAsync(input,default)).Vector;
    var scores=vectors.Select((vector,index)=>new { index,score=vector.Zip(query,(a,b)=>(double)a*b).Sum() }).OrderByDescending(x=>x.score).ToArray();
    if(scores[0].index==i) top1++;
}
if(top1!=files.Length) throw new InvalidOperationException("Official-reference smoke retrieval failed.");
Console.WriteLine(JsonSerializer.Serialize(new { encoder=encoder.Identity,references=files.Length,top1,timingsMs=timings,kind="official-artwork-smoke-not-phone-benchmark" }));
