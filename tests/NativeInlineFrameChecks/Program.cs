using CampaignCore;
using ModCompatibility;
using Iced.Intel;
using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
if(args.Length!=2)throw new ArgumentException("native-snapshot previous-native-baseline.json");
var baseline=CompatibilityBaseline.Read(args[1]);
var output=Console.Out;NativeImage image;
try { Console.SetOut(TextWriter.Null);image=new NativeImage(args[0]); } finally { Console.SetOut(output); }
if(image.GameHash!="0E1B540314D591A17215BBC46FD17C2605CCFA58A7D30418D62453FFF0FEB3B6")throw new InvalidDataException("This regression requires the reviewed26289478snapshot.");
int passed=0,failed=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
void Run(string label,Action action){try{action();}catch(Exception ex){failed++;Console.WriteLine("FAIL "+label+": "+ex.Message);}}
void Reject(Action action,string label){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Check(rejected,label);}
var trio=baseline.Sites.Single(s=>s.Site.Kind=="inline_eq"&&s.Site.Method.Contains("BenchwarmerMenu.<<Confirm>")&&s.Site.Query=="trio");
var oldschool=baseline.Sites.Single(s=>s.Site.Kind=="inline_eq"&&s.Site.Method.Contains("CanadaCurseCatalog")&&s.Site.Query=="oldschool");
NativeComparisonSite Read(SiteContract site,int? index=null,string? query=null)=>InlineComparisonContract.Read(image,site.Site.Method,index??site.InstructionIndex,site.Site.Feature,query??site.Site.Query);
foreach(var site in new[]{trio,oldschool})Run("reviewed comparison "+site.Site.Query,()=>{var result=Read(site);Check(result.Kind=="inline_eq"&&result.Query==site.Site.Query,"reviewed "+site.Site.Query+" comparison admits exact native argument block");});
var method=image.Resolve(trio.Site.Method);var body=image.Instructions(method.Rva);string fullHash=image.BodyHash(method);
const ulong reviewedFrameEnd=0x42A452E;
Run("meaningful adjacent helper retained",()=>Check(body.Any(i=>i.IP>=reviewedFrameEnd&&i.Mnemonic==Mnemonic.Sub&&i.Op0Register==Register.RSP),"full method instruction coverage retains adjacent helper stack prologues"));
Run("wrong offset",()=>Reject(()=>Read(trio,trio.InstructionIndex+1),"comparison must start at the reviewed instruction"));
Run("wrong literal",()=>Reject(()=>Read(trio,query:"oldschool"),"different squad literal remains rejected"));
void Mutate(int at,string hex,string label){var original=body[at];var decoder=Decoder.Create(64,new ByteArrayCodeReader(Convert.FromHexString(hex)));decoder.IP=original.IP;decoder.Decode(out var changed);try{body[at]=changed;Reject(()=>Read(trio),label);}finally{body[at]=original;}}
Run("native call overload",()=>Mutate(trio.InstructionIndex+14,"E800000000","different SequenceEqual call target remains rejected"));
Run("string layout",()=>Mutate(trio.InstructionIndex+9,"4C634118","different string length offset remains rejected"));
Run("nonvolatile register",()=>Mutate(trio.InstructionIndex+10,"4883C314","skipped nonvolatile register modification remains rejected"));
Run("store",()=>Mutate(trio.InstructionIndex+10,"488918","skipped memory store remains rejected"));
Run("callback MethodInfo",()=>Mutate(trio.InstructionIndex+13,"4531C0","non-null native MethodInfo setup remains rejected"));
Run("null branch",()=>Mutate(trio.InstructionIndex+3,"7400","divergent null branch remains rejected"));
Run("owner frame mutation",()=>Mutate(trio.InstructionIndex-1,"4883EC20","stack adjustment after owner branching remains rejected"));
Run("full body hash coverage",()=>{int at=Array.FindIndex(body,i=>i.IP>=reviewedFrameEnd&&i.Mnemonic==Mnemonic.Sub&&i.Op0Register==Register.RSP);var original=body[at];var decoder=Decoder.Create(64,new ByteArrayCodeReader(new byte[]{0x90}));decoder.IP=original.IP;decoder.Decode(out var changed);try{body[at]=changed;Check(image.BodyHash(method)!=fullHash,"protected body hash still detects changes in adjacent helper code");}finally{body[at]=original;}});
Run("exact owner frame",()=>Check(image.FrameEnd(method.Rva)==reviewedFrameEnd,"trio frame ends at its reviewed runtime-function boundary"));
Run("interior address is not a frame entry",()=>Reject(()=>image.FrameEnd(method.Rva+1),"an address inside a frame cannot masquerade as its owner"));
Run("second adjacent-helper owner",()=>{
    var hide=image.Resolve("Tape2Tape.Hockey.UI.ChooseMetaMenu.<Hide>d__#$$MoveNext");
    Check(image.FrameEnd(hide.Rva)==0x42C2054,"ChooseMeta Hide has its own exact reviewed frame end");
    Check(image.Instructions(hide.Rva).Any(i=>i.IP>=0x42C2054&&i.Mnemonic==Mnemonic.Sub&&i.Op0Register==Register.RSP),"ChooseMeta adjacent helper remains covered by full instructions");
});
// Mutate only the in-memory PE directory entry. Native inputs on disk remain unchanged.
using(var pe=new PEReader(new MemoryStream(image.Data,false))) {
    var directory=pe.PEHeaders.PEHeader!.ExceptionTableDirectory;
    var section=pe.PEHeaders.SectionHeaders.Single(s=>directory.RelativeVirtualAddress>=s.VirtualAddress
        && directory.RelativeVirtualAddress+directory.Size<=s.VirtualAddress+s.SizeOfRawData);
    int tableOffset=section.PointerToRawData+directory.RelativeVirtualAddress-section.VirtualAddress;
    int entry=Enumerable.Range(0,directory.Size/12).Single(i=>BinaryPrimitives.ReadUInt32LittleEndian(image.Data.AsSpan(tableOffset+i*12,4))==method.Rva);
    int entryOffset=tableOffset+entry*12;
    void ChangeEntry(int field,uint value,Action action,string label) {
        uint saved=BinaryPrimitives.ReadUInt32LittleEndian(image.Data.AsSpan(entryOffset+field,4));
        try { BinaryPrimitives.WriteUInt32LittleEndian(image.Data.AsSpan(entryOffset+field,4),value);Reject(action,label); }
        finally { BinaryPrimitives.WriteUInt32LittleEndian(image.Data.AsSpan(entryOffset+field,4),saved); }
    }
    Run("missing exact PE entry",()=>ChangeEntry(0,checked((uint)method.Rva+1),()=>Read(trio),"missing exact owner entry fails closed"));
    Run("invalid PE extent",()=>ChangeEntry(4,checked((uint)method.Rva),()=>Read(trio),"empty runtime-function extent fails closed"));
    uint insideInstruction=checked((uint)body.Last(i=>i.NextIP<=reviewedFrameEnd&&i.Length>1).IP+1);
    Run("invalid PE code boundary",()=>ChangeEntry(4,insideInstruction,()=>Read(trio),"frame end inside an instruction fails closed"));
    Run("missing unwind data",()=>ChangeEntry(8,0,()=>Read(trio),"missing runtime-function unwind data fails closed"));
    var map=baseline.Sites.Single(s=>s.Site.Kind=="inline_eq"&&s.Site.Method.Contains("MapObject$$SetOpponents"));
    var mapMethod=image.Resolve(map.Site.Method);
    Run("chained owner extent",()=>Check(image.FrameEnd(mapMethod.Rva)==0x38DFC31,"contiguous unwind continuation belongs to the exact MapObject frame"));
    Run("chained fragment is not a primary owner",()=>Reject(()=>image.FrameEnd(0x38DF91D),"a chained fragment cannot omit its primary frame prologue"));
    int child=Enumerable.Range(0,directory.Size/12).Single(i=>BinaryPrimitives.ReadUInt32LittleEndian(image.Data.AsSpan(tableOffset+i*12,4))==0x38DF91D);
    uint unwind=BinaryPrimitives.ReadUInt32LittleEndian(image.Data.AsSpan(tableOffset+child*12+8,4));
    var unwindSection=pe.PEHeaders.SectionHeaders.Single(s=>unwind>=s.VirtualAddress&&unwind<s.VirtualAddress+s.SizeOfRawData);
    int unwindOffset=checked(unwindSection.PointerToRawData+(int)unwind-unwindSection.VirtualAddress);
    int parentOffset=unwindOffset+4+((image.Data[unwindOffset+2]+1)&~1)*2;
    uint parentBegin=BinaryPrimitives.ReadUInt32LittleEndian(image.Data.AsSpan(parentOffset,4));
    Run("unowned chain parent",()=>{
        try{BinaryPrimitives.WriteUInt32LittleEndian(image.Data.AsSpan(parentOffset,4),parentBegin+1);Reject(()=>Read(map),"chained frame requires an exact owned parent tuple");}
        finally{BinaryPrimitives.WriteUInt32LittleEndian(image.Data.AsSpan(parentOffset,4),parentBegin);}
    });
    Run("cyclic chain parent",()=>{
        byte[] saved=image.Data.AsSpan(parentOffset,12).ToArray();
        try{image.Data.AsSpan(tableOffset+child*12,12).CopyTo(image.Data.AsSpan(parentOffset,12));Reject(()=>Read(map),"chained frame cannot name itself as its parent");}
        finally{saved.CopyTo(image.Data.AsSpan(parentOffset,12));}
    });
    int tailOffset=pe.PEHeaders.SectionHeaders.Where(s=>0x38DFC30>=s.VirtualAddress&&0x38DFC30<s.VirtualAddress+s.SizeOfRawData)
        .Select(s=>s.PointerToRawData+0x38DFC30-s.VirtualAddress).Single();
    Run("only verified terminal trap padding may be absent",()=>{
        byte saved=image.Data[tailOffset];
        try{image.Data[tailOffset]=0xaa;Reject(()=>Read(map),"unaccounted native store cannot be treated as omitted trap padding");}
        finally{image.Data[tailOffset]=saved;}
    });
}
Run("patch outside owner frame",()=>{
    int at=trio.InstructionIndex;var saved=body[at];var changed=saved;changed.IP=reviewedFrameEnd;
    try {body[at]=changed;Reject(()=>Read(trio),"patch block outside exact owner frame remains rejected");}finally{body[at]=saved;}
});
Run("incoming edge from adjacent helper",()=>{
    int at=Array.FindIndex(body,i=>i.IP>=reviewedFrameEnd);var saved=body[at];
    byte[] jump=new byte[5];jump[0]=0xe9;BitConverter.GetBytes(checked((int)((long)body[trio.InstructionIndex].IP+1-(long)saved.IP-5))).CopyTo(jump,1);
    var decoder=Decoder.Create(64,new ByteArrayCodeReader(jump));decoder.IP=saved.IP;decoder.Decode(out var changed);
    try{body[at]=changed;Reject(()=>Read(trio),"incoming edges from adjacent covered code cannot enter displaced bytes");}finally{body[at]=saved;}
});
foreach(var site in baseline.Sites.Where(s=>s.Site.Kind=="inline_eq"))
    Run("full inline inventory "+site.Site.Method+"/"+site.Site.Query,()=>{
        var contract=Read(site);
        Check(contract.Query==site.Site.Query&&contract.Inline.LeftRegister==site.Site.Inline.LeftRegister,
            "reviewed inline frame and register contract retained: "+site.Site.Method+"/"+site.Site.Query);
    });
Run("unmodified image",()=>Check(image.BodyHash(method)==fullHash,"diagnostics restore original full protected body"));
Console.WriteLine($"{passed} checks passed; {failed} failed; native metadata/disassembly only, no game execution.");return failed==0?0:1;
