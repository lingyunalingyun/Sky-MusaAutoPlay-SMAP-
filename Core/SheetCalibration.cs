using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SMAP_WPF.Core;

public sealed record CapturePixels(byte[] Bgra, int Width, int Height, int Stride)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || (long)Width * Height > 16_000_000 || Stride < Width * 4
            || Bgra.LongLength < (long)Stride * Height) throw new ArgumentException("InvalidCapturePixels");
    }
    public bool HasContent()
    {
        Validate();int low=255,high=0;
        for(int y=0;y<Height;y+=Math.Max(1,Height/40))for(int x=0;x<Width;x+=Math.Max(1,Width/60))
        {int p=y*Stride+x*4,value=(Bgra[p]+Bgra[p+1]+Bgra[p+2])/3;low=Math.Min(low,value);high=Math.Max(high,value);}
        return high-low>=12&&high>16;
    }
}
public sealed record KeyLocation(string KeyId, double X, double Y, double Width, double Height);
public sealed record KeyDetection(IReadOnlyList<KeyLocation> Keys, double Confidence, string Reason);
public sealed record KeyVisualSignature(string KeyId, double[] Values);
public sealed record SheetCalibration
{
    public int Version { get; init; } = 1;
    public string CapabilityFingerprint { get; init; } = "";
    public string BindingFingerprint { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public uint Dpi { get; init; } = 96;
    public List<KeyLocation> Keys { get; init; } = new();
    public bool Manual { get; init; }
    public bool Confirmed { get; init; }
    public List<KeyVisualSignature> VisualSignatures { get; init; } = new();
    public static string Fingerprint(InstrumentCapability capability) => Hash(JsonSerializer.Serialize(capability));
    public static string Bindings(IEnumerable<KeyValuePair<string, ushort>> bindings) => Hash(JsonSerializer.Serialize(bindings.OrderBy(p => p.Key).ToArray()));
    static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public bool Matches(InstrumentCapability c, string bindings, int width, int height, uint dpi)
        => Version == 1 && Confirmed && Width == width && Height == height && Dpi == dpi
            && CapabilityFingerprint == Fingerprint(c) && BindingFingerprint == bindings && ValidLocations(c, Keys);
    public static bool ValidLocations(InstrumentCapability c, IReadOnlyList<KeyLocation> keys)
    {
        if (keys.Count != c.Keys.Count || keys.Select(k => k.KeyId).Distinct().Count() != keys.Count
            || c.Keys.Any(k => !keys.Any(p => p.KeyId == k.Id))) return false;
        if (keys.Any(k => !double.IsFinite(k.X) || !double.IsFinite(k.Y) || !double.IsFinite(k.Width) || !double.IsFinite(k.Height)
            || k.Width <= 0 || k.Height <= 0 || k.Width > .3 || k.Height > .4
            || k.X - k.Width / 2 < 0 || k.X + k.Width / 2 > 1 || k.Y - k.Height / 2 < 0 || k.Y + k.Height / 2 > 1)) return false;
        return keys.All(k => !keys.Any(other => other.KeyId != k.KeyId && Math.Abs(k.X-other.X) < Math.Min(k.Width,other.Width)*.5
            && Math.Abs(k.Y-other.Y) < Math.Min(k.Height,other.Height)*.5));
    }
}

public static class SheetKeyDetector
{
    sealed record Candidate(double X, double Y, double W, double H);
    enum PixelKind { Panel, Glyph, Edge }
    public const double MinimumConfidence = .9;
    public static List<KeyVisualSignature> Signatures(CapturePixels frame,IReadOnlyList<KeyLocation> keys)
    {
        frame.Validate();var result=new List<KeyVisualSignature>();
        foreach(var k in keys)
        {
            var values=new double[100];
            for(int y=0;y<10;y++)for(int x=0;x<10;x++)
            {
                int px=Math.Clamp((int)((k.X-k.Width/2+k.Width*(x+.5)/10)*frame.Width),0,frame.Width-1);
                int py=Math.Clamp((int)((k.Y-k.Height/2+k.Height*(y+.5)/10)*frame.Height),0,frame.Height-1);
                int i=py*frame.Stride+px*4;values[y*10+x]=(frame.Bgra[i]+frame.Bgra[i+1]+frame.Bgra[i+2])/3.0;
            }
            double mean=values.Average(),norm=Math.Sqrt(values.Sum(v=>(v-mean)*(v-mean)));
            result.Add(new(k.KeyId,values.Select(v=>(v-mean)/Math.Max(1,norm)).ToArray()));
        }
        return result;
    }
    public static bool SameVisualContext(IReadOnlyList<KeyVisualSignature> original,IReadOnlyList<KeyVisualSignature> current)
        => original.Count>0&&original.Count==current.Count&&original.All(a=>current.Any(b=>a.KeyId==b.KeyId&&a.Values.Length==100&&b.Values.Length==100
            && a.Values.Zip(b.Values,(x,y)=>x*y).Sum()>=.75));
    public static KeyDetection Detect(CapturePixels frame, InstrumentCapability capability)
    {
        var dark=DetectPass(frame,capability,PixelKind.Panel);
        if(dark.Keys.Count==capability.Keys.Count&&dark.Confidence>=MinimumConfidence)return dark;
        var glyph=DetectPass(frame,capability,PixelKind.Glyph);
        return glyph.Keys.Count==capability.Keys.Count&&glyph.Confidence>=MinimumConfidence?glyph:DetectPass(frame,capability,PixelKind.Edge);
    }
    static KeyDetection DetectPass(CapturePixels frame, InstrumentCapability capability,PixelKind kind)
    {
        frame.Validate(); capability.Validate();
        bool glyph=kind!=PixelKind.Panel,edge=kind==PixelKind.Edge;
        var mask=PixelMask(frame,kind,out int scale,out int w,out int h);var visited=new bool[mask.Length];
        var candidates = new List<Candidate>(); var queue = new Queue<int>();
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i] || visited[i]) continue;
            queue.Enqueue(i); visited[i]=true; int x0=w,y0=h,x1=0,y1=0,count=0;
            while(queue.TryDequeue(out int cell))
            {
                int x=cell%w,y=cell/w; count++; x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);
                void Add(int xx,int yy) { if(xx<0||xx>=w||yy<0||yy>=h)return; int n=yy*w+xx;if(mask[n]&&!visited[n]){visited[n]=true;queue.Enqueue(n);} }
                Add(x-1,y);Add(x+1,y);Add(x,y-1);Add(x,y+1);
                if(glyph){Add(x-1,y-1);Add(x+1,y-1);Add(x-1,y+1);Add(x+1,y+1);}
            }
            double bw=x1-x0+1,bh=y1-y0+1;
            if (bw/w is < .02 or > .12 || bh/h is < .025 or > .18 || bw/bh is < .65 or > 1.5 || count/(bw*bh)<(glyph?.08:.35)) continue;
            candidates.Add(new((x0+x1+1)*scale/2.0,(y0+y1+1)*scale/2.0,bw*scale,bh*scale));
        }
        candidates=candidates.Where(c=>!candidates.Any(outer=>outer!=c&&outer.W>c.W*1.2&&outer.H>c.H*1.2
            &&Math.Abs(outer.X-c.X)+c.W/2<outer.W/2&&Math.Abs(outer.Y-c.Y)+c.H/2<outer.H/2)).ToList();
        if(candidates.Count>100 || candidates.Count<capability.Keys.Count-(edge?capability.Columns-1:0)) return new(Array.Empty<KeyLocation>(),0,"InsufficientKeyCandidates");
        var rows = new List<List<Candidate>>();
        foreach(var c in candidates.OrderBy(c=>c.Y))
        {
            var row=rows.LastOrDefault(); if(row==null || Math.Abs(row.Average(k=>k.Y)-c.Y)>c.H*.35){row=new();rows.Add(row);} row.Add(c);
        }
        List<KeyLocation>? best=null; double bestScore=0;
        foreach(var row in rows)
        {
            if(row.Count!=capability.Columns)continue;
            var sorted=row.OrderBy(c=>c.X).ToArray();
            for(int offset=0;offset<=sorted.Length-capability.Columns;offset++)
            {
                var first=sorted.Skip(offset).Take(capability.Columns).ToArray();
                double[] dx=first.Zip(first.Skip(1),(a,b)=>b.X-a.X).ToArray();
                if(dx.Length>0 && (dx.Min()<first.Average(c=>c.W)*1.05 || dx.Max()/dx.Min()>1.14))continue;
                var matching=rows.Where(r=>r.Average(c=>c.Y)>=row.Average(c=>c.Y)-1)
                    .Select(r=>first.Select(f=>r.OrderBy(c=>Math.Abs(c.X-f.X)).First()).ToArray())
                    .Where(r=>r.Distinct().Count()==capability.Columns && r.Select((c,j)=>Math.Abs(c.X-first[j].X)/c.W).Max()<.2).ToArray();
                if(edge)
                {
                    // 背景可能把单个轮廓连成大块；缺少的中心仍必须逐个通过真实轮廓检查。
                    matching=rows
                        .Select(r=>first.Select(f=>r.Where(c=>Math.Abs(c.X-f.X)<c.W*.2).OrderBy(c=>Math.Abs(c.X-f.X)).FirstOrDefault()).ToArray())
                        .Where(r=>r.Count(c=>c!=null)>=Math.Max(2,(capability.Columns+1)/2)&&r.Where(c=>c!=null).Distinct().Count()==r.Count(c=>c!=null))
                        .Select(r=>
                        {
                            double y=r.Where(c=>c!=null).Select(c=>c!.Y).Order().ElementAt(r.Count(c=>c!=null)/2);
                            return first.Select(f=>f with{Y=y}).ToArray();
                        }).ToArray();
                }
                if(matching.Length!=capability.Rows)continue;
                var grid=matching.Take(capability.Rows).ToArray();
                double[] dy=grid.Zip(grid.Skip(1),(a,b)=>b.Average(c=>c.Y)-a.Average(c=>c.Y)).ToArray();
                if(dy.Length>0 && (dy.Min()<first.Average(c=>c.H)*1.05 || dy.Max()/dy.Min()>1.14))continue;
                var all=grid.SelectMany(r=>r).ToArray();
                double outline=edge?all.Min(c=>OutlineSupport(mask,w,h,c,scale)):1;
                if(outline<MinimumConfidence)continue;
                double score=1-Math.Min(.5,(all.Max(c=>c.W)/all.Min(c=>c.W)-1 + all.Max(c=>c.H)/all.Min(c=>c.H)-1)*.25);
                if(glyph)score=1-Math.Min(.5,((dx.Length>0?dx.Max()/dx.Min()-1:0)+(dy.Length>0?dy.Max()/dy.Min()-1:0)
                    +grid.Max(r=>(r.Max(c=>c.Y)-r.Min(c=>c.Y))/r.Average(c=>c.H)))*.4);
                score=Math.Min(score,outline);
                double glyphWidth=all.Select(c=>c.W).Order().ElementAt(all.Length/2)*1.25;
                double glyphHeight=all.Select(c=>c.H).Order().ElementAt(all.Length/2)*1.25;
                var locations=capability.Slots.Where(s=>s.KeyId!=null).Select(s=>{var c=grid[s.Row][s.Column];return new KeyLocation(s.KeyId!,c.X/frame.Width,c.Y/frame.Height,
                    (glyph?glyphWidth:c.W)/frame.Width,(glyph?glyphHeight:c.H)/frame.Height);}).ToList();
                if(score>bestScore && SheetCalibration.ValidLocations(capability,locations)){best=locations;bestScore=score;}
            }
        }
        return best == null || bestScore<MinimumConfidence ? new(Array.Empty<KeyLocation>(),bestScore,"ManualCalibrationRequired") : new(best,bestScore,"CandidateRequiresConfirmation");
    }
    public static bool VerifyLocations(CapturePixels frame,InstrumentCapability capability,IReadOnlyList<KeyLocation> keys)
    {
        frame.Validate();capability.Validate();if(!SheetCalibration.ValidLocations(capability,keys))return false;
        var mask=PixelMask(frame,PixelKind.Edge,out int scale,out int w,out int h);
        return keys.All(k=>OutlineSupport(mask,w,h,new(k.X*frame.Width,k.Y*frame.Height,k.Width*frame.Width,k.Height*frame.Height),scale)>=MinimumConfidence);
    }
    static bool[] PixelMask(CapturePixels frame,PixelKind kind,out int scale,out int w,out int h)
    {
        bool glyph=kind!=PixelKind.Panel,edge=kind==PixelKind.Edge;
        scale=Math.Max(1,(frame.Width+959)/960);w=(frame.Width+scale-1)/scale;h=(frame.Height+scale-1)/scale;
        var mask=new bool[w*h];var luminance=edge?new int[w*h]:Array.Empty<int>();
        if(edge)for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {int p=y*scale*frame.Stride+x*scale*4;luminance[y*w+x]=(frame.Bgra[p]+frame.Bgra[p+1]+frame.Bgra[p+2])/3;}
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int p=y*scale*frame.Stride+x*scale*4;
            int b=frame.Bgra[p],g=frame.Bgra[p+1],r=frame.Bgra[p+2],high=Math.Max(r,Math.Max(g,b)),low=Math.Min(r,Math.Min(g,b));
            mask[y*w+x]=glyph?r>=g-3&&r>=b-3&&r-Math.Min(g,b)>=8&&high>=120&&low>=90&&high-low<=85
                :high-low<=18&&high>=15&&high<=142;
            if(edge)
            {
                int lowNeighbor=luminance[y*w+x],highNeighbor=lowNeighbor;
                for(int yy=Math.Max(0,y-1);yy<=Math.Min(h-1,y+1);yy++)for(int xx=Math.Max(0,x-1);xx<=Math.Min(w-1,x+1);xx++)
                {lowNeighbor=Math.Min(lowNeighbor,luminance[yy*w+xx]);highNeighbor=Math.Max(highNeighbor,luminance[yy*w+xx]);}
                mask[y*w+x]=high>=190&&high-low<=95&&r>=g-8&&r>=b-8&&Math.Max(luminance[y*w+x]-lowNeighbor,highNeighbor-luminance[y*w+x])>=20;
            }
        }
        if(!glyph)return mask;
        var expanded=new bool[mask.Length];
        for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++)if(mask[y*w+x])
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)expanded[(y+dy)*w+x+dx]=true;
        return expanded;
    }
    static double OutlineSupport(bool[] mask,int width,int height,Candidate key,int scale)
    {
        double best=0,cx=key.X/scale,cy=key.Y/scale;
        int min=Math.Max(3,(int)(Math.Min(key.W,key.H)/scale*.28)),max=Math.Min(64,(int)Math.Ceiling(Math.Max(key.W,key.H)/scale*.7));
        for(int shape=0;shape<2;shape++)for(int radius=min;radius<=max;radius++)
        {
            int hits=0;
            for(int i=0;i<64;i++)
            {
                double angle=i*Math.PI/32,x=Math.Cos(angle),y=Math.Sin(angle),normal=shape==0?1:Math.Abs(x)+Math.Abs(y);
                int px=(int)Math.Round(cx+x*radius/normal),py=(int)Math.Round(cy+y*radius/normal);
                if(px>=0&&px<width&&py>=0&&py<height&&mask[py*width+px])hits++;
            }
            best=Math.Max(best,hits/64.0);if(best==1)return best;
        }
        return best;
    }
    public static bool Stable(KeyDetection a, KeyDetection b) => a.Confidence >= MinimumConfidence && b.Confidence >= MinimumConfidence
        && a.Keys.Count == b.Keys.Count && a.Keys.All(k=>b.Keys.Any(p=>p.KeyId==k.KeyId && Math.Abs(k.X-p.X)<.003 && Math.Abs(k.Y-p.Y)<.003));
}
