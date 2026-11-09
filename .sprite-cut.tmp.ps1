$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$refs=@([Drawing.Bitmap].Assembly.Location,[Drawing.Color].Assembly.Location,[Drawing.PointF].Assembly.Location,(Join-Path $PSHOME 'System.Runtime.dll'),(Join-Path $PSHOME 'System.Private.Windows.Core.dll'),(Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll'))
Add-Type -ReferencedAssemblies $refs -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class SpriteCut {
    public static void Crop(Bitmap sheet,string output,int x0,int x1,int y0,int y1) {
        int w=x1-x0,h=y1-y0;
        var pixels=new Color[w*h];
        var visited=new bool[w*h];
        var queue=new int[w*h];
        var keep=new bool[w*h];
        int largest=0;
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) pixels[y*w+x]=sheet.GetPixel(x+x0,y+y0);
        for(int n=0;n<pixels.Length;n++){
            if(visited[n]||pixels[n].A<165) continue;
            visited[n]=true;
            int end=1;queue[0]=n;
            for(int start=0;start<end;start++) {
                int current=queue[start];
                int x=current%w,y=current/w;
                if(x>0) Add(current-1);
                if(x<w-1) Add(current+1);
                if(y>0) Add(current-w);
                if(y<h-1) Add(current+w);
            }
            if(end>largest){
                largest=end;
                Array.Clear(keep,0,keep.Length);
                for(int i=0;i<end;i++) keep[queue[i]]=true;
            }
            void Add(int next) {
                if(visited[next]||pixels[next].A<165) return;
                visited[next]=true;
                queue[end++]=next;
            }
        }
        int l=w,r=0,t=h,b=0;
        using var clean=new Bitmap(w,h);
        for(int y=0;y<h;y++) for(int x=0;x<w;x++){
            int n=y*w+x;
            var p=pixels[n];
            if(p.A<65)continue;
            bool near=keep[n]||(x>0&&keep[n-1])||(x<w-1&&keep[n+1])||(y>0&&keep[n-w])||(y<h-1&&keep[n+w]);
            if(!near)continue;
            int a=Math.Clamp((p.A-65)*255/160,0,255);
            clean.SetPixel(x,y,Color.FromArgb(a,p.R,p.G,p.B));
            l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);
        }
        if(l>=r||t>=b)throw new Exception("Empty sprite: "+output);
        var bounds=Rectangle.FromLTRB(l,t,r+1,b+1);
        double scale=Math.Min(90.0/bounds.Width,91.0/bounds.Height);
        int sw=(int)Math.Round(bounds.Width*scale),sh=(int)Math.Round(bounds.Height*scale);
        using var sprite=new Bitmap(96,100);
        using(var g=Graphics.FromImage(sprite)){
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            g.DrawImage(clean,new Rectangle((96-sw)/2,96-sh,sw,sh),bounds,GraphicsUnit.Pixel);
        }
        sprite.Save(output,ImageFormat.Png);

    }
}
"@
$sheet=[Drawing.Bitmap]::new((Resolve-Path '.sprite-sheet.tmp.png').Path)
$rows=@(
  @(0,182,361,528,704,887),
  @(0,180,357,522,699,887),
  @(0,179,358,531,706,887),
  @(0,179,355,531,711,887),
  @(0,182,358,527,715,887),
  @(0,179,360,529,714,887)
)
$columns=@(0,292,588,890,1181,1478,1774)
$catalog=Get-Content 'Map Edition/Play/Config/buildings.json' -Raw | ConvertFrom-Json
$dest=(Resolve-Path 'Map Edition/Play/wwwroot/assets').Path
for($i=0;$i -lt $catalog.Count;$i++){
  $name=$catalog[$i].name.ToLowerInvariant().Replace(' ','-')
  $x=$i%6; $y=[int][Math]::Floor($i/6)
  [SpriteCut]::Crop($sheet,(Join-Path $dest "$name.png"),$columns[$x],$columns[$x+1],$rows[$x][$y],$rows[$x][$y+1])
}
$sheet.Dispose()

