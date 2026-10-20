param([Parameter(Mandatory=$true)][string]$Folder)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$drawingRoot = Split-Path ([System.Drawing.Bitmap].Assembly.Location)
$references = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Rectangle].Assembly.Location) + @(Get-ChildItem -LiteralPath $drawingRoot -Filter 'System.Private.Windows*.dll' | ForEach-Object FullName)
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Drawing.Drawing2D;
public static class HeadCanvasFit {
 public static void Fit(string input,string output,string template,int left,int top,int width,int height) {
  using(var reference=new Bitmap(template)) {
   int l=512,t=512,r=0,b=0;
   for(int y=top;y<top+height;y++){int count=0;for(int x=left;x<left+width;x++)if(reference.GetPixel(x,y).A>32)count++;if(count>6){t=Math.Min(t,y);b=Math.Max(b,y);}}
   for(int x=left;x<left+width;x++){int count=0;for(int y=top;y<top+height;y++)if(reference.GetPixel(x,y).A>32)count++;if(count>6){l=Math.Min(l,x);r=Math.Max(r,x);}}
   left=l;top=t;width=r-l+1;height=b-t+1;
  }
  using(var src=new Bitmap(input)) using(var dst=new Bitmap(512,512,PixelFormat.Format32bppArgb)) {
   int x0=src.Width,y0=src.Height,x1=0,y1=0;
   for(int y=0;y<src.Height;y++)for(int x=0;x<src.Width;x++)if(src.GetPixel(x,y).A>32){x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
   if(x1<=x0 || y1<=y0)throw new Exception("Generated head has no visible artwork.");
   float scale=Math.Min((width-16f)/(x1-x0+1),(height-16f)/(y1-y0+1));float w=(x1-x0+1)*scale,h=(y1-y0+1)*scale;
   using(var g=Graphics.FromImage(dst)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.CompositingMode=CompositingMode.SourceCopy;g.DrawImage(src,new RectangleF(left+(width-w)/2,top+(height-h)/2,w,h),new RectangleF(x0,y0,x1-x0+1,y1-y0+1),GraphicsUnit.Pixel);}
   dst.Save(output,ImageFormat.Png);
  }
 }
}
'@
$Folder = (Resolve-Path -LiteralPath $Folder).Path
$project = Get-Content -LiteralPath (Join-Path $Folder 'head-project.json') -Raw | ConvertFrom-Json
foreach ($item in $project.Images | Where-Object Group -EQ 'Head') {
    $file = [IO.Path]::GetFileName($item.File)
    [HeadCanvasFit]::Fit((Join-Path $Folder ('generated-originals/'+$file)), (Join-Path $Folder $item.File), (Join-Path (Split-Path $Folder) ('Mathieu Template/'+$item.File)), $item.Left, $item.Top, $item.ContentWidth, $item.ContentHeight)
    if (Test-Path -LiteralPath (Join-Path $Folder ('helmet/'+$file))) { Copy-Item -LiteralPath (Join-Path $Folder $item.File) -Destination (Join-Path $Folder ('helmet/'+$file)) -Force }
}
Copy-Item -LiteralPath (Join-Path $Folder 'frames/S.png') -Destination (Join-Path $Folder 'portrait.png') -Force
