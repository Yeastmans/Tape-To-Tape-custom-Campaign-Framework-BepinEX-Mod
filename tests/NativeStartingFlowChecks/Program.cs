using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

// Validate the compiled production emitter in this isolated process. No game is
// loaded or patched; a synthetic Win64 caller supplies the reviewed native ABI.
if (args.Length != 3) throw new ArgumentException("runtime.dll interop-directory loader-core-directory required");
if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
    throw new PlatformNotSupportedException("Windows x64 is required");
AssemblyLoadContext.Default.Resolving += (_, name) => {
    foreach (var dir in args.Skip(1).Prepend(Path.GetDirectoryName(Path.GetFullPath(args[0])))) {
        string path = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
    }
    return null;
};
var runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
var emit = runtime.GetType("EndlessMode.NativeSquadBehaviorBridge", true)!
    .GetMethod("BuildStartingFlowStub", BindingFlags.NonPublic | BindingFlags.Static)!;
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
foreach (bool managed in new[] { false, true })
for (byte intro = 0; intro < 2; intro++)
for (byte result = 0; result < 2; result++)
for (byte oldschool = 0; oldschool < 2; oldschool++) {
    IntPtr memory = Native.VirtualAlloc(IntPtr.Zero, 65536, 0x3000, 0x40);
    if (memory == IntPtr.Zero) throw new Exception("VirtualAlloc failed");
    try {
        IntPtr At(int offset) => memory + offset;
        const int output = 0x5000, vectors = 0x5200, state = 0x5400;
        long[] values = [0x11223344556677aa, 0x123456789, 0x223456789, 0x323456789,
            0x423456789, 0x523456789, 0x623456789]; // RAX,RCX,RDX,R8..R11
        long initialRsi = 0x5566778899000000 | intro, initialR14 = 0x123456789ab, title = At(0x5600).ToInt64();
        var pattern = Enumerable.Range(0, 96).Select(i => (byte)(i * 7 + 13)).ToArray();
        Marshal.Copy(pattern, 0, At(vectors), pattern.Length); Marshal.WriteByte(At(state + 0x58), oldschool);
        void Write(int offset, List<byte> bytes) => Marshal.Copy(bytes.ToArray(), 0, At(offset), bytes.Count);
        void Add(List<byte> code, params byte[] bytes) => code.AddRange(bytes);
        void Imm(List<byte> code, byte rex, byte opcode, long value) { Add(code, rex, opcode); code.AddRange(BitConverter.GetBytes(value)); }
        void Jump(List<byte> code, int offset) { Add(code, 0xff, 0x25, 0, 0, 0, 0); code.AddRange(BitConverter.GetBytes(At(offset).ToInt64())); }
        void Store(List<byte> code, byte rex, byte modrm, int offset) { Add(code, rex, 0x89, modrm, 0x24); code.AddRange(BitConverter.GetBytes(offset)); }
        void Xmm(List<byte> code, bool load, int i, int offset) { Add(code, 0xf3, 0x41, 0x0f, load ? (byte)0x6f : (byte)0x7f, (byte)(0x84 + i * 8), 0x24); code.AddRange(BitConverter.GetBytes(offset)); }
        var wrapper = new List<byte>();
        Add(wrapper, 0x53, 0x56, 0x57, 0x41, 0x54, 0x41, 0x56, 0x41, 0x57, 0x48, 0x83, 0xec, 0x28);
        Imm(wrapper, 0x49, 0xbc, At(output).ToInt64()); Store(wrapper, 0x49, 0xa4, 8);
        for (int i = 0; i < 6; i++) Xmm(wrapper, true, i, vectors - output + i * 16);
        for (int i = 0; i < 3; i++) Imm(wrapper, 0x48, (byte)(0xb8 + i), values[i]);
        for (int i = 3; i < 7; i++) Imm(wrapper, 0x49, (byte)(0xb8 + i - 3), values[i]);
        Imm(wrapper, 0x48, 0xbe, initialRsi); Imm(wrapper, 0x48, 0xbf, At(state).ToInt64());
        Imm(wrapper, 0x49, 0xbe, initialR14); Imm(wrapper, 0x49, 0xbf, title);
        Jump(wrapper, 0x1000); Write(0, wrapper);
        var callback = new List<byte>();
        Store(callback, 0x49, 0x8c, 16); Store(callback, 0x49, 0x94, 24); Store(callback, 0x4d, 0x84, 32); Store(callback, 0x49, 0xa4, 40);
        int calls = 0; long actualState = 0, actualTitle = 0; byte actualIntro = 255; byte returned = result;
        Native.Flow flow = (s, flag, t) => { calls++; actualState=s.ToInt64(); actualIntro=flag; actualTitle=t.ToInt64(); return returned; };
        if (managed) {
            Add(callback, 0x48, 0x83, 0xec, 0x28); Imm(callback, 0x48, 0xb8, Marshal.GetFunctionPointerForDelegate(flow).ToInt64());
            Add(callback, 0xff, 0xd0, 0x48, 0x83, 0xc4, 0x28, 0xc3);
        } else {
            for (int i = 0; i < 3; i++) Imm(callback, 0x48, (byte)(0xb8 + i), -100-i);
            for (int i = 0; i < 4; i++) Imm(callback, 0x49, (byte)(0xb8 + i), -200-i);
            for (int i = 0; i < 6; i++) Add(callback, 0x66, 0x0f, 0xef, (byte)(0xc0+i*9));
            Add(callback, 0xb0, result, 0xc3);
        }
        Write(0x2000, callback);
        var capture = new List<byte>();
        // SETZ records the flags left by the displaced OR without changing them.
        Add(capture, 0x41, 0x0f, 0x94, 0x84, 0x24, 0, 0, 0, 0);
        for (int i = 0; i < 3; i++) Store(capture, 0x49, (byte)(0x84+i*8), 64+i*8);
        for (int i = 3; i < 7; i++) Store(capture, 0x4d, (byte)(0x84+(i-3)*8), 64+i*8);
        Store(capture, 0x49, 0xb4, 120); Store(capture, 0x49, 0xbc, 128); Store(capture, 0x4d, 0xb4, 136); Store(capture, 0x4d, 0xbc, 144); Store(capture, 0x49, 0xa4, 48);
        for (int i = 0; i < 6; i++) Xmm(capture, false, i, 160+i*16);
        Add(capture, 0x48, 0x83, 0xc4, 0x28, 0x41, 0x5f, 0x41, 0x5e, 0x41, 0x5c, 0x5f, 0x5e, 0x5b, 0xc3);
        Write(0x3000, capture);
        var stub=(byte[])emit.Invoke(null, [At(0x2000), At(0x3000), Convert.FromHexString("400FB6C60A4758")])!;
        Check(stub.Length<=256, "stub fits the reserved code arena"); Marshal.Copy(stub, 0, At(0x1000), stub.Length);
        Check(Native.FlushInstructionCache(Native.GetCurrentProcess(), memory, 65536), "instruction cache flush");
        Marshal.GetDelegateForFunctionPointer<Native.Entry>(memory)(); GC.KeepAlive(flow);
        long Read(int offset) => Marshal.ReadInt64(At(output+offset));
        Check(Read(16)==At(state).ToInt64() && Read(24)==intro && Read(32)==title, "callback receives RDI state, SIL intro, R15 title");
        Check(Read(8)==Read(48) && (Read(40)&15)==8, "stack restoration and Win64 callback alignment");
        if(managed) Check(calls==1 && actualState==At(state).ToInt64() && actualIntro==intro && actualTitle==title, "managed reverse PInvoke arguments");
        for(int i=0;i<values.Length;i++) Check(Read(64+i*8)==(i==0 ? result|oldschool : values[i]), "volatile register/displaced result "+i);
        Check(Read(120)==((initialRsi&~255L)|result) && Read(128)==At(state).ToInt64() && Read(136)==initialR14 && Read(144)==title, "only SIL changes among reviewed nonvolatile registers");
        Check(Marshal.ReadByte(At(output))==((result|oldschool)==0?1:0), "displaced OR leaves the native branch flags");
        var actual=new byte[96]; Marshal.Copy(At(output+160),actual,0,actual.Length); Check(actual.SequenceEqual(pattern), "XMM0 through XMM5 restored");
    } finally { Native.VirtualFree(memory,0,0x8000); }
}
Console.WriteLine($"PASS {checks} compiled starting-flow ABI checks; no game process or gameplay execution.");
static class Native {
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void Entry();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate byte Flow(IntPtr state, byte intro, IntPtr title);
    [DllImport("kernel32")] public static extern IntPtr VirtualAlloc(IntPtr address,nuint size,uint allocation,uint protection);
    [DllImport("kernel32")] public static extern bool VirtualFree(IntPtr address,nuint size,uint freeType);
    [DllImport("kernel32")] public static extern bool FlushInstructionCache(IntPtr process,IntPtr address,nuint size);
    [DllImport("kernel32")] public static extern IntPtr GetCurrentProcess();
}
