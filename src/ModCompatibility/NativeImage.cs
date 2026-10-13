using System.Security.Cryptography;
using System.Text;
using System.Reflection.PortableExecutable;
using CampaignCore;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;

namespace ModCompatibility;

// LibCpp2IL has process-wide state. The updater serializes access and Studio runs
// this before artwork extraction, which also uses LibCpp2IL.
public sealed class NativeImage
{
    public readonly byte[] Data;
    public readonly string GameHash, MetadataHash;
    public readonly ulong ImageBase;
    private readonly SortedDictionary<ulong,List<Il2CppMethodDefinition>> methods = new();
    private readonly Dictionary<string,List<Il2CppMethodDefinition>> names = new(StringComparer.Ordinal);
    private readonly ulong[] starts;
    private readonly Dictionary<ulong,Instruction[]> decoded = new();
    private readonly Dictionary<ulong,ulong> tableStarts = new();
    private ulong End(ulong rva) => starts[Array.BinarySearch(starts,rva)+1];
    public static string Name(Il2CppMethodDefinition m) => NativeSymbolNames.Stable(m.DeclaringType!.FullName+"$$"+m.Name);
    public static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public NativeImage(string game)
    {
        Data = File.ReadAllBytes(Path.Combine(game,"GameAssembly.dll"));
        var metadata = File.ReadAllBytes(Path.Combine(game,"Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat"));
        GameHash = Convert.ToHexString(SHA256.HashData(Data)); MetadataHash = Convert.ToHexString(SHA256.HashData(metadata));
        var version = LibCpp2IlMain.DetermineUnityVersion(Path.Combine(game,"UnityPlayer.dll"),Path.Combine(game,"Tape to Tape_Data"));
        LibCpp2IlMain.Reset();
        if(!LibCpp2IlMain.Initialize(Data,metadata,version)) throw new InvalidDataException("Could not read this game's IL2CPP metadata.");
        foreach(var m in LibCpp2IlMain.TheMetadata!.methodDefs.Where(m=>m.MethodPointer!=0))
        {
            if(!methods.TryGetValue(m.Rva,out var same)) methods[m.Rva]=same=new();
            same.Add(m);
            var key=Name(m); if(!names.TryGetValue(key,out var named)) names[key]=named=new();
            named.Add(m);
        }
        var first=methods.First().Value[0]; ImageBase=first.MethodPointer-first.Rva;
        if(ImageBase!=0x180000000UL) throw new InvalidDataException("Unsupported game binary layout; Windows x64 required.");
        starts=methods.Keys.Concat(LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.Keys.Where(a=>a>=ImageBase).Select(a=>a-ImageBase)).Distinct().Order().ToArray();
    }
    public Il2CppMethodDefinition Resolve(string name)
    {
        name=NativeSymbolNames.Stable(name);
        var parts=name.Split("::",2);
        var found=Find(parts[0]).Where(m=>parts.Length==1 || Signature(m)==parts[1]).ToArray();
        if(found.Length!=1)
            throw new InvalidDataException("Missing or ambiguous native method: "+name);
        return found[0];
    }
    public IEnumerable<Il2CppMethodDefinition> Find(string name) => names.TryGetValue(NativeSymbolNames.Stable(name),out var found)?found:[];
    public int SharedMethodCount(ulong rva) => methods[rva].Count;
    public string Identity(Il2CppMethodDefinition method) => Name(method)+"::"+Signature(method);
    public Il2CppMethodDefinition Containing(string name,ulong site)
    {
        int index=Array.BinarySearch(starts,site);if(index<0) index=~index-1;
        var found=Find(name).Where(m=>index>=0 && m.Rva==starts[index]).ToArray();
        if(found.Length!=1) throw new InvalidDataException("Missing native patch owner: "+name);
        return found[0];
    }
    public byte[] Bytes(ulong rva,int length)
    {
        long offset=LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(ImageBase+rva);
        if(offset<0 || offset+length>Data.Length) throw new InvalidDataException("Native address is outside the game image.");
        return Data.AsSpan(checked((int)offset),length).ToArray();
    }
    public Instruction[] Instructions(ulong rva)
    {
        if(decoded.TryGetValue(rva,out var existing)) return existing;
        var index=Array.BinarySearch(starts,rva);
        if(index<0 || index+1>=starts.Length || starts[index+1]-rva>4*1024*1024) throw new InvalidDataException("Unknown native method boundary.");
        var decoder=Iced.Intel.Decoder.Create(64,new ByteArrayCodeReader(Bytes(rva,checked((int)(starts[index+1]-rva)))));
        decoder.IP=rva; var result=new List<Instruction>();
        ulong codeEnd=starts[index+1];
        while(decoder.IP<codeEnd)
        {
            decoder.Decode(out var instruction);
            if(instruction.IsInvalid) throw new InvalidDataException("Invalid native instruction at "+instruction.IP.ToString("x"));
            result.Add(instruction);
            // MSVC places RVA jump tables after an int3 at the end of async state
            // machines. Their indexed memory operand gives the exact data boundary.
            if(!instruction.IsIPRelativeMemoryOperand && instruction.MemoryIndex!=Register.None && instruction.MemoryIndexScale==4
                && instruction.MemoryDisplacement64>instruction.IP && instruction.MemoryDisplacement64<codeEnd)
                codeEnd=instruction.MemoryDisplacement64;
        }
        if(codeEnd<starts[index+1]) tableStarts[rva]=codeEnd;
        while(result.Count>0 && result[^1].Mnemonic==Mnemonic.Int3) result.RemoveAt(result.Count-1);
        return decoded[rva]=result.ToArray();
    }
    public string? Literal(ulong rva) => LibCpp2IlMain.GetLiteralByAddress(ImageBase+rva);
    public string Signature(Il2CppMethodDefinition method) => NativeSymbolNames.Stable(method.HumanReadableSignature);
    public string BodyHash(Il2CppMethodDefinition method)
    {
        var instructions=Instructions(method.Rva); ulong end=instructions[^1].NextIP;
        var text=new StringBuilder(Signature(method)+"\n");
        foreach(var ins in instructions)
        {
            text.Append(ins.Code).Append(':');
            for(int n=0;n<ins.OpCount;n++)
            {
                var kind=ins.GetOpKind(n); text.Append(kind).Append('=');
                if(kind==OpKind.Register) text.Append(ins.GetOpRegister(n));
                else if(kind==OpKind.NearBranch64)
                {
                    var target=ins.NearBranchTarget;
                    if(target>=method.Rva && target<end) text.Append("local:").Append(target-method.Rva);
                    else if(methods.TryGetValue(target,out var targetMethods)) text.AppendJoin('|',targetMethods.Select(Name).Distinct().Order());
                    else text.Append("runtime:").Append(Convert.ToHexString(Bytes(target,12)));
                }
                else if(kind==OpKind.Memory)
                {
                    text.Append(ins.MemoryBase).Append(',').Append(ins.MemoryIndex).Append(',').Append(ins.MemoryIndexScale).Append(',').Append(ins.MemorySize);
                    if(ins.IsIPRelativeMemoryOperand)
                    {
                        var literal=Literal(ins.IPRelativeMemoryAddress);
                        text.Append(literal==null ? ",reloc" : ",literal:"+literal);
                    }
                    else if(ins.MemoryDisplacement64>=method.Rva && ins.MemoryDisplacement64<End(method.Rva)) text.Append(",local:").Append(ins.MemoryDisplacement64-method.Rva);
                    else text.Append(',').Append(ins.MemoryDisplacement64);
                }
                else if(kind.ToString().StartsWith("Immediate",StringComparison.Ordinal)) text.Append(ins.GetImmediate(n));
                else text.Append(ins.ToString());
                text.Append(';');
            }
            text.AppendLine();
        }
        if(tableStarts.TryGetValue(method.Rva,out var table))
        {
            var data=Bytes(table,checked((int)(End(method.Rva)-table)));
            for(int i=0;i<data.Length;i+=4)
            {
                if(i+4>data.Length) { text.Append(Convert.ToHexString(data.AsSpan(i)));break; }
                uint value=BitConverter.ToUInt32(data,i);
                text.Append(value>=method.Rva && value<table?"target:"+(value-method.Rva):"data:"+value).Append(';');
            }
        }
        return Digest(text.ToString());
    }
    public string[] SquadCallInventory()
    {
        var query=Resolve("Tape2Tape.CampaignUtil$$IsPlayingSquad").Rva;
        var rows=new List<string>();
        // Scan candidate rel32 calls, then verify their containing method's instruction boundary.
        using var pe=new PEReader(new MemoryStream(Data,false));
        foreach(var part in pe.PEHeaders.SectionHeaders.Where(s=>(s.SectionCharacteristics&SectionCharacteristics.MemExecute)!=0))
        {
        var code=Data.AsSpan(part.PointerToRawData,part.SizeOfRawData).ToArray();
        var section=checked((ulong)part.VirtualAddress);
        for(int i=0;i+5<=code.Length;i++)
        {
            if(code[i]!=0xe8 && code[i]!=0xe9) continue;
            ulong at=section+(uint)i;
            if((long)at+5+BitConverter.ToInt32(code,i+1)!=(long)query) continue;
            int index=Array.BinarySearch(starts,at); if(index<0) index=~index-1;
            if(index<0 || !methods.TryGetValue(starts[index],out var owner)) throw new InvalidDataException("Unidentified squad-query caller.");
            var body=Instructions(starts[index]);
            var pos=Array.FindIndex(body,v=>v.IP==at);
            if(pos<0 || body[pos].Length!=5 || body[pos].FlowControl is not (FlowControl.Call or FlowControl.UnconditionalBranch)) continue;
            string? literal=null;
            foreach(var ins in body.Skip(Math.Max(0,pos-10)).Take(Math.Min(10,pos)).Reverse())
                if(ins.Mnemonic==Mnemonic.Mov && ins.Op0Kind==OpKind.Register && ins.Op0Register==Register.RDX && ins.IsIPRelativeMemoryOperand)
                { literal=Literal(ins.IPRelativeMemoryAddress); if(literal!=null) break; }
            rows.Add(string.Join('|',owner.Select(Name).Distinct().Order())+"|"+(literal??"dynamic"));
        }
        }
        if(rows.Count==0) throw new InvalidDataException("No native squad checks found; executable scan failed.");
        return rows.Order().ToArray();
    }
}
