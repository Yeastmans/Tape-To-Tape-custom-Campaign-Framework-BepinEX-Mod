using System;
using System.Collections.Generic;

namespace EndlessMode;

internal static class NativeInlineComparisonStub
{
    // Entered by JMP inside a native method with RSP % 16 == 0. Callback ABI:
    // byte callback(Il2CppString* left, Il2CppString* right), Windows x64.
    // 0 means run native comparison, 1 means unequal, 2 means equal.
    internal static byte[] BuildInlineComparisonStub(IntPtr callback, IntPtr nativeFallthrough,
        IntPtr nativeEqual, IntPtr customResume, byte[] comparison, string leftRegister)
    {
        if(leftRegister != "rcx" && leftRegister != "rbx") throw new ArgumentException("Unsupported left register.");
        if(comparison.Length!=3 || comparison[0]!=0x48 || comparison[1]!=0x3b || comparison[2]!=(leftRegister=="rcx"?0xca:0xda))
            throw new ArgumentException("Unsupported comparison instruction.");
        var code=new List<byte>();
        void Emit(params byte[] bytes)=>code.AddRange(bytes);
        void Jump(IntPtr address) { Emit(0xff,0x25,0,0,0,0); code.AddRange(BitConverter.GetBytes(address.ToInt64())); }
        void Restore()
        {
            for(int i=0;i<6;i++) Emit(0xf3,0x0f,0x6f,(byte)(0x44+i*8),0x24,(byte)(0x20+i*16));
            Emit(0x48,0x81,0xc4,0x88,0,0,0,0x41,0x5b,0x41,0x5a,0x41,0x59,0x41,0x58,0x5a,0x59,0x58);
        }
        // Seven volatile GPRs + 0x88 bytes retain 16-byte alignment. First
        // 32 bytes are callback shadow space; XMM0..5 occupy 0x20..0x7f.
        Emit(0x50,0x51,0x52,0x41,0x50,0x41,0x51,0x41,0x52,0x41,0x53);
        Emit(0x48,0x81,0xec,0x88,0,0,0);
        for(int i=0;i<6;i++) Emit(0xf3,0x0f,0x7f,(byte)(0x44+i*8),0x24,(byte)(0x20+i*16));
        if(leftRegister=="rbx") Emit(0x48,0x89,0xd9); // mov rcx,rbx
        Emit(0x48,0xb8);code.AddRange(BitConverter.GetBytes(callback.ToInt64()));Emit(0xff,0xd0);
        Emit(0x84,0xc0,0x0f,0x84);int nativeOffset=code.Count;Emit(0,0,0,0);
        Emit(0xfe,0xc8,0x88,0x84,0x24,0xb8,0,0,0); // dec al; saved RAX.low = result
        Restore();Jump(customResume);
        int native=code.Count;
        var displacement=BitConverter.GetBytes(native-nativeOffset-4);
        for(int i=0;i<4;i++) code[nativeOffset+i]=displacement[i];
        Restore();code.AddRange(comparison);
        Emit(0x75,0x0e);Jump(nativeEqual);Jump(nativeFallthrough);
        return code.ToArray();
    }
}
