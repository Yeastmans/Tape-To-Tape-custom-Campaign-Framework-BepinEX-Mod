using EndlessMode;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

if(args.Length is not (2 or 3))throw new ArgumentException("first-generated-interop second-generated-interop [compiled-runtime.dll]");
Func<byte[],string> Hash=GeneratedInteropIdentity.Hash;
if(args.Length==3) {
    var runtime=System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
    Hash=runtime.GetType("EndlessMode.GeneratedInteropIdentity",true)!.GetMethod("Hash",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.CreateDelegate<Func<byte[],string>>();
}
int checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
foreach(var (name,expected) in new[] {
    ("MainScriptsAssembly.dll","683F11DAFDEDF6803FC6DC146F17284A15D2C6FF28E41DD4E3C0B074170092D3"),
    ("spine-csharp.dll","C89305CD267A018E70E5C44492635885C0AD300E28B0E7E6B7DB11B9A28D3F6E") }) {
    var first=File.ReadAllBytes(Path.Combine(args[0],name));var second=File.ReadAllBytes(Path.Combine(args[1],name));
    var before=(byte[])first.Clone();
    Check(!SHA256.HashData(first).SequenceEqual(SHA256.HashData(second)),name+" independent generations have different full-file hashes");
    string fingerprint=Hash(first);
    Check(fingerprint==Hash(second),name+" independent equivalent generation is accepted");
    Check(fingerprint==expected,name+" exact reviewed content fingerprint");
    Check(first.SequenceEqual(before),name+" hashing preserves caller-owned bytes");
    using var pe=new PEReader(new MemoryStream(first,false));var reader=pe.GetMetadataReader();var module=reader.GetModuleDefinition();
    int mvidOffset=pe.PEHeaders.MetadataStartOffset+reader.GetHeapMetadataOffset(HeapIndex.Guid)+(MetadataTokens.GetHeapOffset(module.Mvid)-1)*16;
    int generationOffset=pe.PEHeaders.MetadataStartOffset+reader.GetTableMetadataOffset(TableIndex.Module);
    int stringsOffset=pe.PEHeaders.MetadataStartOffset+reader.GetHeapMetadataOffset(HeapIndex.String);
    int blobsOffset=pe.PEHeaders.MetadataStartOffset+reader.GetHeapMetadataOffset(HeapIndex.Blob);
    var method=reader.MethodDefinitions.Select(reader.GetMethodDefinition).First(m=>m.RelativeVirtualAddress!=0);
    int bodyRva=method.RelativeVirtualAddress;var section=pe.PEHeaders.SectionHeaders.Single(s=>bodyRva>=s.VirtualAddress && bodyRva<s.VirtualAddress+s.SizeOfRawData);
    int bodyOffset=section.PointerToRawData+bodyRva-section.VirtualAddress;
    bool Accepted(byte[] bytes){try{return Hash(bytes)==fingerprint;}catch(BadImageFormatException){return false;}catch(InvalidDataException){return false;}}
    var altered=(byte[])first.Clone();Guid.NewGuid().TryWriteBytes(altered.AsSpan(mvidOffset,16));
    Check(Accepted(altered),name+" only module ID randomness is ignored");
    foreach(var (offset,label) in new[] { (bodyOffset+1,"method body"), (stringsOffset+1,"member strings"),
        (blobsOffset+1,"signature blobs"), (generationOffset,"module metadata"), (mvidOffset-1,"byte before MVID"),
        (mvidOffset+16,"byte after MVID"), (pe.PEHeaders.CoffHeaderStartOffset+4,"PE timestamp"), (first.Length-1,"image tail") }) {
        altered=(byte[])first.Clone();altered[offset]^=1;
        Check(!Accepted(altered),name+" changed "+label+" is rejected");
    }
    Check(!Accepted(first[..128]),name+" truncated image is rejected");
    altered=(byte[])first.Clone();Array.Clear(altered,mvidOffset,16);
    Check(!Accepted(altered),name+" empty module ID is rejected");
}
Console.WriteLine($"{checks} generated interop identity checks passed; actual independently generated assemblies, no game execution.");
