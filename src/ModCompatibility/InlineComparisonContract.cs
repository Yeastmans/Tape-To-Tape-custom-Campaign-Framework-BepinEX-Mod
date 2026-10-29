using CampaignCore;
using Iced.Intel;

namespace ModCompatibility;

// Accept only the two reviewed MSVC string equality sequences. The callback
// bypasses argument preparation and returns AL at the native call continuation.
// No stores, nonvolatile register writes, or stack changes may be skipped.
public static class InlineComparisonContract
{
    public const string SequenceEqual = "System.SpanHelpers$$SequenceEqual::System.Boolean SequenceEqual(ref System.Byte first, ref System.Byte second, System.UInt64 length)";
    public static NativeComparisonSite Read(NativeImage image, string methodName, int index, string feature, string query)
    {
        var method=image.Resolve(methodName); var body=image.Instructions(method.Rva);
        if(index<1 || index+15>=body.Length) throw new InvalidDataException("Truncated inline comparison.");
        var block=body.Skip(index).Take(15).ToArray();
        string left=block[0].ToString() switch { "cmp rcx,rdx"=>"rcx", "cmp rbx,rdx"=>"rbx", _=>throw new InvalidDataException("Unsupported comparison registers.") };
        void Require(bool valid, [System.Runtime.CompilerServices.CallerLineNumber] int line=0) { if(!valid) throw new InvalidDataException($"Inline comparison contract changed at rule {line}: "+methodName); }
        ulong frameEnd=image.FrameEnd(method.Rva);
        var frame=body.TakeWhile(x=>x.IP<frameEnd).ToArray();
        Require(index+15<frame.Length);
        Require(frame.All(x=>x.IP>=method.Rva && x.NextIP<=frameEnd));
        // Instructions removes terminal trap padding. Verify every omitted byte
        // up to the PE frame end, so no meaningful instruction can be skipped.
        if(frame[^1].NextIP<frameEnd)
            Require(image.Bytes(frame[^1].NextIP,checked((int)(frameEnd-frame[^1].NextIP))).All(b=>b==0xcc));
        string S(int i)=>block[i].ToString();
        Require(block[1].Mnemonic==Mnemonic.Je && block[1].Length is 2 or 6);
        Require(S(2)==$"test {left},{left}" && block[3].Mnemonic==Mnemonic.Je && S(4)=="test rdx,rdx" && block[5].Mnemonic==Mnemonic.Je);
        Require(S(6)=="mov eax,[rdx+10h]" && S(7)==$"cmp [{left}+10h],eax" && block[8].Mnemonic==Mnemonic.Jne);
        Require(block[3].NearBranchTarget==block[5].NearBranchTarget && block[3].NearBranchTarget==block[8].NearBranchTarget);
        Require(S(9)==$"movsxd r8,dword ptr [{left}+10h]" && S(13)=="xor r9d,r9d");
        var setup=block.Skip(10).Take(3).Select(x=>x.ToString()).Order().ToArray();
        Require(setup.SequenceEqual(new[]{"add r8,r8","add rdx,14h",left=="rcx"?"add rcx,14h":"lea rcx,[rbx+14h]"}.Order()));
        Require(block[14].FlowControl==FlowControl.Call && block[14].Length==5 && block[14].NearBranchTarget==image.Resolve(SequenceEqual).Rva);
        ulong end=block[14].NextIP;
        Require(block[1].NearBranchTarget>=end && block[3].NearBranchTarget>=end);
        // Incoming edges may target the comparison, but none may enter displaced bytes.
        Require(!body.Any(x=>x.Op0Kind==OpKind.NearBranch64 && x.NearBranchTarget>block[0].IP && x.NearBranchTarget<block[2].IP));
        string? literal=null;
        foreach(var previous in body.Skip(Math.Max(0,index-8)).Take(Math.Min(index,8)).Reverse())
            if(previous.Mnemonic==Mnemonic.Mov && previous.Op0Register==Register.RDX && previous.IsIPRelativeMemoryOperand)
            { literal=image.Literal(previous.IPRelativeMemoryAddress); if(literal!=null) break; }
        Require(literal==query);
        // Prove only the owner's exact PE runtime-function frame. Instructions
        // and BodyHash intentionally retain adjacent compiler helper coverage.
        // Every owner has a fixed prologue frame. Early-return epilogues may
        // precede a later basic block in address order, but may only unwind to
        // a return/tailcall. No path can change RSP and rejoin the body frame.
        int stack=8; bool branched=false;
        for(int n=0;n<frame.Length;n++)
        {
            var ins=frame[n];
            if(ins.FlowControl is FlowControl.ConditionalBranch or FlowControl.UnconditionalBranch or FlowControl.IndirectBranch) branched=true;
            if(ins.Mnemonic==Mnemonic.Push) { Require(!branched); stack-=8; }
            else if(ins.Mnemonic==Mnemonic.Sub && ins.Op0Register==Register.RSP) { Require(!branched); stack-=checked((int)ins.GetImmediate(1)); }
            else if(ins.Mnemonic==Mnemonic.Pop || (ins.Op0Kind==OpKind.Register && ins.Op0Register==Register.RSP))
            {
                Require(branched); int unwound=stack;
                for(;;n++)
                {
                    Require(n<frame.Length && n!=index);ins=frame[n];
                    if(ins.Mnemonic==Mnemonic.Add && ins.Op0Register==Register.RSP) unwound+=checked((int)ins.GetImmediate(1));
                    else if(ins.Mnemonic==Mnemonic.Mov && ins.Op0Register==Register.RSP && ins.Op1Register==Register.R11)
                    {
                        int saved=n-1;
                        while(saved>=0 && frame[saved].Mnemonic is Mnemonic.Mov or Mnemonic.Movaps && frame[saved].Op0Kind==OpKind.Register && frame[saved].Op1Kind==OpKind.Memory && frame[saved].Op0Register is not (Register.R11 or Register.RSP) && frame[saved].MemoryBase==Register.R11) saved--;
                        Require(saved>=0 && frame[saved].Mnemonic==Mnemonic.Lea && frame[saved].Op0Register==Register.R11 && frame[saved].MemoryBase==Register.RSP && frame[saved].MemoryIndex==Register.None);
                        unwound+=checked((int)frame[saved].MemoryDisplacement64);
                    }
                    else if(ins.Mnemonic==Mnemonic.Pop && ins.Op0Register!=Register.RSP) unwound+=8;
                    else if(ins.FlowControl==FlowControl.Return || (ins.FlowControl==FlowControl.UnconditionalBranch && (ins.NearBranchTarget<frame[0].IP || ins.NearBranchTarget>=frameEnd))) { Require(unwound==8);break; }
                    else Require(ins.Mnemonic is Mnemonic.Mov or Mnemonic.Lea or Mnemonic.Nop && ins.Op0Register!=Register.RSP);
                }
            }
        }
        Require((stack&15)==0);
        var contract=new NativeInlineComparison { LeftRegister=left,EqualBranchRva=checked((int)block[1].NearBranchTarget),BlockBytes=Convert.ToHexString(image.Bytes(block[0].IP,checked((int)(end-block[0].IP)))) };
        return new NativeComparisonSite { Feature=feature,Query=query,Kind="inline_eq",Method=image.Identity(method),Rva=checked((int)block[0].IP),Target=checked((int)end),Bytes=Convert.ToHexString(image.Bytes(block[0].IP,checked((int)(block[2].IP-block[0].IP)))),Inline=contract };
    }
}
